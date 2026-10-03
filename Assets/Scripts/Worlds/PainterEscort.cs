using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.AI;

namespace MusePico.Worlds
{
    /// <summary>
    /// Monet and Picasso walk with the visitor through every world, on the NavMesh.
    ///
    /// All the deciding is <see cref="EscortBrain"/>'s, one per painter, and is tested in EditMode.
    /// This component only feeds it the visitor's head, walks the result with a
    /// <see cref="NavMeshAgent"/>, turns the painter to face the visitor when standing, and drives
    /// Painter.controller: <c>Speed</c> walks, <c>WalkStyle</c> picks the gait, <c>Listening</c>
    /// when the visitor looks at them from close by, <c>Talking</c> through <see cref="SetTalking"/>.
    ///
    /// <b>Why the worlds need nothing special.</b> In SplatPortal every world stands on the same
    /// invisible floor in shared coordinates, and a transition only swaps which splat is drawn. A
    /// painter two metres behind the visitor when a world is swapped is simply two metres behind
    /// them in the new one. A teleport is handled by <see cref="EscortSettings.warpDistance"/>.
    /// </summary>
    public class PainterEscort : MonoBehaviour, ICompanionBodies
    {
        [System.Serializable]
        public class Painter
        {
            public Animator animator;
            [Tooltip("The master this painter plays, as masters.json names them: monet, picasso.")]
            public string masterId;
            [Tooltip("-1 prefers the visitor's left, +1 the right.")]
            public float side = -1f;
            [Tooltip("Painter.controller WalkStyle for a short stroll. 0 Walk, 1 Casual, 2 Thoughtful, 3 Formal, 4 Generated.")]
            public int strollStyle = 1;
            [Tooltip("WalkStyle for keeping up with a walking visitor.")]
            public int briskStyle = 4;
            [Tooltip("Lower walks round a higher one when their paths cross.")]
            public int avoidancePriority = 50;

            [System.NonSerialized] public EscortBrain brain;
            [System.NonSerialized] public NavMeshAgent agent;
            [System.NonSerialized] public EscortOutput last;
            [System.NonSerialized] public int style;
            [System.NonSerialized] public bool walking, turning, talking, running, listening;
            [System.NonSerialized] public float facingYaw;
        }

        public Painter[] painters = new Painter[0];

        public EscortSettings settings = new EscortSettings();

        [Header("Gait")]
        [Tooltip("Ground speed each WalkStyle's clip was authored at, m/s, in WalkStyle order. " +
                 "MEASURED 28 Sep 2026 from the planted foot's speed relative to the hips, mean of " +
                 "Monet and Picasso: Walk 0.78, Casual 0.65, Thoughtful 0.56, Formal 0.76, Generated 1.32.")]
        public float[] clipGroundSpeed = { 0.78f, 0.65f, 0.56f, 0.76f, 1.32f };

        [Tooltip("Walks longer than this, metres, use the brisk gait.")]
        public float briskBeyond = 3f;

        [Tooltip("Further than this from the visitor, a painter hurries: the brisk gait played faster.")]
        public float hurryBeyond = 5f;

        [Tooltip("Playback is matched to ground speed within this range, so feet do not skate.")]
        public Vector2 playbackRange = new Vector2(0.75f, 1.45f);

        [Header("Catching up")]
        [Tooltip("Further behind than this while the visitor is still walking, a painter runs. The " +
                 "thumbstick moves the visitor at 2.5 m/s and the brisk walk tops out at 1.9.")]
        public float runBeyond = 5f;
        [Tooltip("A running painter drops back to a walk once this close.")]
        public float runUntil = 3f;
        [Tooltip("Ground speed the Run_Generated clip was authored at, m/s. MEASURED 29 Sep 2026 from the " +
                 "planted foot: 4.10 (3.9-4.3 across contact thresholds), matching a 0.82 m stance at " +
                 "a 0.625 s cycle.")]
        public float runClipSpeed = 4.1f;
        [Tooltip("Run playback range. At 0.75 the run covers 3.1 m/s - enough to gain on the visitor.")]
        public Vector2 runPlaybackRange = new Vector2(0.75f, 1.05f);

        [Header("Facing")]
        [Tooltip("A standing painter turns to face the visitor once they are this many degrees off.")]
        public float turnDeadband = 25f;
        [Tooltip("Degrees per second when turning on the spot.")]
        public float turnRate = 110f;

        [Header("What not to stand in front of")]
        [Tooltip("Works of art and anything else worth looking at. Every FocalObject in the scene is " +
                 "added automatically; destroyed or hidden ones are skipped.")]
        public Transform[] art = new Transform[0];

        [Header("Visitor")]
        [Tooltip("The painters' local avoidance steers round a capsule this wide at the visitor's head.")]
        public float visitorRadius = 0.35f;

        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int WalkStyleParam = Animator.StringToHash("WalkStyle");
        static readonly int TalkingParam = Animator.StringToHash("Talking");
        static readonly int TalkStyleParam = Animator.StringToHash("TalkStyle");
        static readonly int ListeningParam = Animator.StringToHash("Listening");
        static readonly int RunState = Animator.StringToHash("Run_Generated");

        /// <summary>WalkStyle while running. Painter.controller reaches Run_Generated only by name, and
        /// every other state through Any State on Speed + WalkStyle; a WalkStyle no walk answers to
        /// holds the run without editing the controller, and restoring it walks out again.</summary>
        const int HoldRunStyle = -1;

        Camera _head;
        NavMeshObstacle _visitorObstacle;
        Vector2 _headLast;
        float _headSpeed, _heading;
        bool _headSeeded;
        readonly List<Vector2> _artPoints = new List<Vector2>();
        Vector2[] _art = new Vector2[0];
        MuseXR.Worlds.FocalObject[] _focal = new MuseXR.Worlds.FocalObject[0];
        float _artRefreshIn;
        Vector2[] _others = new Vector2[0];

        public int Count => painters.Length;
        public EscortOutput StateOf(int i) => painters[i].last;
        public Transform TransformOf(int i) => painters[i].animator != null ? painters[i].animator.transform : null;

        public int IndexOf(string masterId)
        {
            for (int i = 0; i < painters.Length; i++) if (painters[i].masterId == masterId) return i;
            return -1;
        }

        void ICompanionBodies.SetTalking(int index, bool talking) => SetTalking(index, talking);

        /// <summary>
        /// While true the visitor is in a conversation: the panel takes the centre of the view, so
        /// a painter standing there steps aside to its edge at once (the one speaking holds its
        /// ground). Holding everyone still instead left the painter who was pointed at standing in
        /// front of the panel that opened (seen in Play Mode, 29 Sep 2026).
        /// </summary>
        public bool ClearCentre { get; set; }

        /// <summary>
        /// A question has been asked: every painter holds their ground, stops if walking, and only
        /// follows once the visitor has gone <see cref="EscortSettings.holdFollowDistance"/>.
        /// </summary>
        public bool Conversing { get; set; }

        /// <summary>Hold one painter in the Listen pose (while the other is speaking).</summary>
        public void SetListening(int index, bool listening)
        {
            if (index >= 0 && index < painters.Length) painters[index].listening = listening;
        }

        /// <summary>
        /// Start or stop one painter talking. <paramref name="style"/> is Painter.controller's
        /// TalkStyle (0 Talk ... 7 Right hand open); -1 keeps the current one. A talking painter
        /// holds their ground unless the visitor walks off.
        /// </summary>
        public void SetTalking(int index, bool talking, int style = -1)
        {
            if (index < 0 || index >= painters.Length || painters[index].animator == null) return;
            var p = painters[index];
            p.talking = talking;
            if (style >= 0) p.animator.SetInteger(TalkStyleParam, style);
            p.animator.SetBool(TalkingParam, talking);
        }

        void Start()
        {
            var origin = FindAnyObjectByType<XROrigin>();
            _head = origin != null ? origin.Camera : Camera.main;

            var obstacle = new GameObject("Visitor (NavMesh obstacle)");
            obstacle.transform.SetParent(transform, false);
            _visitorObstacle = obstacle.AddComponent<NavMeshObstacle>();
            _visitorObstacle.shape = NavMeshObstacleShape.Capsule;
            _visitorObstacle.radius = visitorRadius;
            _visitorObstacle.height = 1.8f;
            _visitorObstacle.center = new Vector3(0f, 0.9f, 0f);
            _visitorObstacle.carving = false;   // moves every frame; carving would rebuild the mesh constantly

            foreach (var p in painters)
            {
                if (p.animator == null) continue;
                p.animator.applyRootMotion = false;   // the walks play in place; the agent moves them
                p.brain = new EscortBrain(settings, p.side) { Walkable = Walkable };
                p.agent = p.animator.GetComponent<NavMeshAgent>();
                if (p.agent == null) p.agent = p.animator.gameObject.AddComponent<NavMeshAgent>();
                Configure(p);
                p.facingYaw = p.animator.transform.eulerAngles.y;
            }
            _others = new Vector2[Mathf.Max(0, painters.Length - 1)];
        }

        void Configure(Painter p)
        {
            var a = p.agent;
            a.radius = 0.3f;
            a.height = 1.75f;
            a.baseOffset = 0f;
            a.acceleration = 3f;          // eases into a step rather than launching
            a.angularSpeed = 240f;
            a.stoppingDistance = 0.1f;
            a.autoBraking = true;
            a.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            a.avoidancePriority = p.avoidancePriority;
            a.updateRotation = true;
            a.isStopped = true;

            if (!a.isOnNavMesh && NavMesh.SamplePosition(p.animator.transform.position, out var hit, 3f, NavMesh.AllAreas))
                a.Warp(hit.position);
            if (!a.isOnNavMesh)
            {
                Debug.LogWarning($"[PainterEscort] {p.animator.name} is not on a NavMesh — bake one on the floor.");
                return;
            }

            // The baked NavMesh sits a voxel above the physics floor (measured 0.08 m on the
            // SplatPortal floor), and an agent stands its transform on the NavMesh. Feet are at the
            // prefab origin, so lower the body onto the real floor or they hover.
            var onMesh = a.nextPosition;
            if (Physics.Raycast(onMesh + Vector3.up, Vector3.down, out var floor, 3f, ~0, QueryTriggerInteraction.Ignore))
                a.baseOffset = floor.point.y - onMesh.y;
        }

        bool Walkable(Vector2 xz)
        {
            float y = _head != null ? _head.transform.position.y - 1.5f : 0f;
            return NavMesh.SamplePosition(new Vector3(xz.x, y, xz.y), out var hit, 0.25f, NavMesh.AllAreas)
                   && Mathf.Abs(hit.position.x - xz.x) < 0.2f && Mathf.Abs(hit.position.z - xz.y) < 0.2f;
        }

        void Update()
        {
            if (_head == null) return;
            float dt = Time.deltaTime;
            var headPos = _head.transform.position;
            var visitor = new Vector2(headPos.x, headPos.z);
            float gazeYaw = _head.transform.eulerAngles.y;

            TrackVisitor(visitor, gazeYaw, dt);
            _visitorObstacle.transform.position = new Vector3(headPos.x, FloorUnder(headPos), headPos.z);
            GatherArt(dt);
            var artPoints = _art;

            for (int i = 0; i < painters.Length; i++)
            {
                var p = painters[i];
                if (p.brain == null || p.agent == null) continue;
                if (!p.agent.isOnNavMesh && !Reseat(p)) continue;

                int k = 0;
                for (int j = 0; j < painters.Length; j++)
                {
                    if (j == i || painters[j].brain == null) continue;
                    var o = painters[j];
                    _others[k++] = o.last.move ? o.last.destination : Flat(o.animator.transform.position);
                }

                var self = Flat(p.animator.transform.position);
                var output = p.brain.Tick(new EscortInput
                {
                    visitor = visitor, gazeYaw = gazeYaw, heading = _heading, visitorSpeed = _headSpeed,
                    self = self, others = _others, art = artPoints, holdGround = p.talking || Conversing, clearCentre = ClearCentre && !Conversing, dt = dt,
                });

                Act(p, output, visitor, dt);
                p.last = output;
            }
        }

        /// <summary>
        /// Stand every painter beside the visitor after a jump no one could walk: a door into another
        /// world, which in WorldDoors loads the next capture at its own position rather than swapping
        /// splats over a shared floor as SplatPortal does. Call it once the new world's NavMesh exists.
        /// Each painter takes its own side, a little behind (<see cref="WorldDoorLayout.ArrivalSlot"/>),
        /// faces the visitor, and starts afresh: its brain's memory of the old world is meaningless.
        /// </summary>
        public void ArriveBeside(Vector3 visitorFloor, float visitorYaw)
        {
            foreach (var p in painters)
            {
                if (p.animator == null) continue;
                var at = MuseXR.Worlds.WorldDoorLayout.ArrivalSlot(visitorFloor, visitorYaw, p.side);
                if (NavMesh.SamplePosition(at, out var hit, 2.5f, NavMesh.AllAreas)) at = hit.position;

                if (p.agent != null)
                {
                    p.agent.baseOffset = 0f;
                    if (!p.agent.Warp(at)) p.animator.transform.position = at;
                    p.agent.isStopped = true;
                }
                else p.animator.transform.position = at;

                var toVisitor = new Vector3(visitorFloor.x - at.x, 0f, visitorFloor.z - at.z);
                if (toVisitor.sqrMagnitude > 1e-4f) p.animator.transform.rotation = Quaternion.LookRotation(toVisitor);
                p.facingYaw = p.animator.transform.eulerAngles.y;
                p.brain = new EscortBrain(settings, p.side) { Walkable = Walkable };
                p.last = default;
                p.walking = p.turning = p.running = false;
            }
        }

        /// <summary>
        /// Put a painter back on the NavMesh at the nearest point. Happens when the walkable floor
        /// switches to the next world (<see cref="EscortNavMesh"/>): a painter still standing in
        /// the old world may be outside the new one's walls. They are behind the visitor at that
        /// moment, so the step is not seen.
        /// </summary>
        bool Reseat(Painter p)
        {
            var pos = p.animator.transform.position;
            if (!NavMesh.SamplePosition(pos, out var hit, 10f, NavMesh.AllAreas)) return false;
            p.agent.Warp(hit.position);
            p.facingYaw = p.animator.transform.eulerAngles.y;
            return p.agent.isOnNavMesh;
        }

        void TrackVisitor(Vector2 visitor, float gazeYaw, float dt)
        {
            if (!_headSeeded) { _headLast = visitor; _heading = gazeYaw; _headSeeded = true; }
            var v = dt > 0f ? (visitor - _headLast) / dt : Vector2.zero;
            _headLast = visitor;
            // A teleport is one enormous frame; do not let it read as a sprint.
            if (v.magnitude > 8f) v = Vector2.zero;
            _headSpeed = Mathf.Lerp(_headSpeed, v.magnitude, 1f - Mathf.Exp(-6f * dt));
            // Heading follows the direction of travel, not the head: the visitor may walk one way
            // while looking another, and the painters belong behind the walk.
            if (v.magnitude > 0.3f)
                _heading = Mathf.LerpAngle(_heading, EscortBrain.YawTo(Vector2.zero, v), 1f - Mathf.Exp(-4f * dt));
            else if (_headSpeed < settings.stillSpeed)
                _heading = gazeYaw;
        }

        void Act(Painter p, EscortOutput o, Vector2 visitor, float dt)
        {
            var agent = p.agent;
            var tr = p.animator.transform;

            if (o.warp)
            {
                var target = new Vector3(o.destination.x, tr.position.y, o.destination.y);
                if (NavMesh.SamplePosition(target, out var hit, 3f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                    p.facingYaw = tr.eulerAngles.y;
                }
            }

            if (o.move)
            {
                var target = new Vector3(o.destination.x, tr.position.y, o.destination.y);
                if (NavMesh.SamplePosition(target, out var hit, 1.5f, NavMesh.AllAreas))
                {
                    var snapped = Flat(hit.position);
                    if ((snapped - o.destination).sqrMagnitude > 0.0025f) p.brain.CorrectDestination(snapped);
                    if (agent.isStopped || (agent.destination - hit.position).sqrMagnitude > 0.04f)
                        agent.SetDestination(hit.position);
                }

                float remaining = Vector2.Distance(Flat(tr.position), p.brain.Destination);
                float fromVisitor = Vector2.Distance(Flat(tr.position), visitor);

                // Pick the gait when a walk starts, and only ever shift UP while walking: a shift
                // down mid-stride is a visible hitch for no benefit.
                if (!p.walking)
                {
                    p.walking = true;
                    p.style = remaining > briskBeyond ? p.briskStyle : p.strollStyle;
                }
                else if (p.style != p.briskStyle && (remaining > briskBeyond * 1.5f || fromVisitor > hurryBeyond))
                    p.style = p.briskStyle;

                // Run to catch up only while the visitor is still going; once they stop, the
                // painter is closing a gap that is no longer growing, and walks.
                bool visitorWalking = _headSpeed > settings.stillSpeed;
                if (!p.running && o.mode == EscortMode.Trailing && visitorWalking && fromVisitor > runBeyond)
                {
                    p.running = true;
                    p.animator.CrossFadeInFixedTime(RunState, 0.2f);
                }
                else if (p.running && (fromVisitor < runUntil || !visitorWalking || o.mode != EscortMode.Trailing))
                    p.running = false;

                float natural = NaturalSpeed(p.style);
                float speed = natural;
                if (p.style == p.briskStyle && fromVisitor > hurryBeyond) speed = natural * playbackRange.y;
                if (p.running) speed = Mathf.Clamp(_headSpeed + 0.6f, runClipSpeed * runPlaybackRange.x, runClipSpeed * runPlaybackRange.y);
                agent.speed = speed;
                agent.isStopped = false;
                agent.updateRotation = true;
                p.facingYaw = tr.eulerAngles.y;
            }
            else
            {
                if (!agent.isStopped) { agent.isStopped = true; agent.ResetPath(); }
                agent.updateRotation = false;
                p.walking = false;
                p.running = false;
                FaceVisitor(p, visitor, dt);
            }

            Animate(p, o);
        }

        void FaceVisitor(Painter p, Vector2 visitor, float dt)
        {
            var tr = p.animator.transform;
            float want = EscortBrain.YawTo(Flat(tr.position), visitor);
            float off = Mathf.Abs(Mathf.DeltaAngle(p.facingYaw, want));
            if (off > turnDeadband) p.turning = true;
            if (p.turning)
            {
                p.facingYaw = Mathf.MoveTowardsAngle(p.facingYaw, want, turnRate * dt);
                if (Mathf.Abs(Mathf.DeltaAngle(p.facingYaw, want)) < 3f) p.turning = false;
            }
            tr.rotation = Quaternion.Euler(0f, p.facingYaw, 0f);
        }

        void Animate(Painter p, EscortOutput o)
        {
            var an = p.animator;
            float v = p.agent.velocity.magnitude;
            bool stepping = v > 0.1f;

            an.SetInteger(WalkStyleParam, p.running ? HoldRunStyle : p.style);
            // While running, Speed must stay above the walk threshold even as the agent brakes,
            // or Any State takes it to Idle mid-stride.
            an.SetFloat(SpeedParam, p.running ? Mathf.Max(v, 1f) : stepping ? v : 0f);
            an.SetBool(ListeningParam, (o.listening || p.listening) && !p.talking);
            if (p.running)
                an.speed = Mathf.Clamp(v / runClipSpeed, runPlaybackRange.x, runPlaybackRange.y);
            else
                an.speed = stepping
                    ? Mathf.Clamp(v / NaturalSpeed(p.style), playbackRange.x, playbackRange.y)
                    : 1f;
        }

        float NaturalSpeed(int style)
            => style >= 0 && style < clipGroundSpeed.Length && clipGroundSpeed[style] > 0.05f
                ? clipGroundSpeed[style] : 1f;

        /// <summary>Art positions, refreshed a few times a second: the painting is carried about and
        /// worlds come and go, but not every frame.</summary>
        void GatherArt(float dt)
        {
            _artRefreshIn -= dt;
            if (_artRefreshIn > 0f) return;
            _artRefreshIn = 0.25f;
            _focal = FindObjectsByType<MuseXR.Worlds.FocalObject>(FindObjectsSortMode.None);

            _artPoints.Clear();
            foreach (var t in art)
                if (t != null && t.gameObject.activeInHierarchy) _artPoints.Add(Flat(t.position));
            foreach (var f in _focal)
                if (f != null && f.isActiveAndEnabled) _artPoints.Add(Flat(f.transform.position));
            _art = _artPoints.ToArray();
        }

        static float FloorUnder(Vector3 head)
            => NavMesh.SamplePosition(new Vector3(head.x, head.y - 1.5f, head.z), out var hit, 2f, NavMesh.AllAreas)
                ? hit.position.y : head.y - 1.6f;

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
    }
}
