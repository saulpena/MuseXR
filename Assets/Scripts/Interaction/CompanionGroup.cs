using System;
using System.Collections.Generic;
using MuseXR.Slots;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// The companions in the world (§3.4): who stands where, and who speaks when.
    ///
    ///   Marks       1.5-2.2 m from the visitor, within ±60° of forward, never behind, off the path,
    ///               from <see cref="CompanionMarks"/>. A blocked mark (a wall between, no floor) is
    ///               retried nearer, then mirrored.
    ///   Teleport    when the visitor jumps (a teleport or snap turn), every companion is put on its new
    ///               mark on the next frame - no walking, no clipping. <see cref="Remarked"/> fires so
    ///               the UI layer can fade them.
    ///   Turns       one at a time in her fixed order, A advances, 2 s auto-advance, and the gaze gate:
    ///               nobody starts speaking until the visitor looks within 60° of them.
    ///
    /// What each companion says comes from <see cref="LineFor"/> (DialogueClient in the journey; a
    /// canned line by default). When a line has played, call <see cref="LineFinished"/>; with no
    /// voice, <see cref="EstimateSeconds"/> ends it.
    /// </summary>
    public sealed class CompanionGroup : MonoBehaviour, IConfirmable
    {
        /// <summary>A step this large between frames is a teleport, not walking.</summary>
        public const float JumpDistance = 0.5f, JumpDegrees = 20f;
        /// <summary>
        /// A visitor who slowly turns round leaves the companions behind them. Past this angle from the
        /// gaze, for this long, they are re-marked in front again: "never behind".
        /// </summary>
        public const float BehindDegrees = 100f, BehindSeconds = 1.5f;

        public Transform Head { get; set; }

        /// <summary>
        /// True: the companions stand on marks around the visitor and follow (her §3.4, between
        /// chapters). False: they stay where a chapter's diagram put them (her chapter diagrams mark
        /// M/V/S): they turn to face the visitor and take turns WITHOUT the gaze gate. Measured in the
        /// Palace: standing at the court, her M and V marks are at the visitor's shoulders, so a gate
        /// that waits to be looked at left everyone silent until the visitor went looking for them.
        /// </summary>
        public bool FollowVisitor { get; set; } = true;
        public IReadOnlyList<string> Ids => _ids;
        public IReadOnlyDictionary<string, Transform> Figures => _figures;
        public TurnTaking Turns { get; private set; }

        /// <summary>The line a master says. Default: a canned line; the journey wires DialogueClient here.</summary>
        public Func<string, string> LineFor = id => Masters.Name(id) + " considers your question.";

        /// <summary>When true (no voice playing lines), a line ends after <see cref="EstimateSeconds"/>.</summary>
        public bool TimeLinesByLength { get; set; } = true;

        public event Action<string, string> LineStarted;   // id, line
        public event Action<string> LineEnded;
        public event Action TurnsFinished;
        public event Action Remarked;

        readonly List<string> _ids = new List<string>();
        readonly Dictionary<string, Transform> _figures = new Dictionary<string, Transform>();
        Vector3 _lastPos;
        float _lastYaw;
        float _lineLeft, _behindFor;
        bool _placedOnce;

        /// <summary>
        /// The companions, in speaking order, each with the transform that stands for them (a rigged
        /// master, or her standee fallback - "a chosen master must never vanish").
        /// </summary>
        public void Set(IReadOnlyList<string> speakingOrder, IReadOnlyDictionary<string, Transform> figures)
        {
            _ids.Clear(); _figures.Clear();
            foreach (var id in speakingOrder)
            {
                if (!figures.TryGetValue(id, out var f) || f == null) continue;
                _ids.Add(id); _figures[id] = f;
            }
            _placedOnce = false;
        }

        /// <summary>Put every companion on its mark around the visitor now.</summary>
        public void PlaceAll()
        {
            if (Head == null) return;
            for (var i = 0; i < _ids.Count; i++)
            {
                var at = MarkPosition(i);
                var to = Head.position - at; to.y = 0f;
                var rot = to.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(to.normalized, Vector3.up);
                _figures[_ids[i]].SetPositionAndRotation(at, rot);
            }
            _lastPos = Head.position; _lastYaw = Head.eulerAngles.y;
            _placedOnce = true;
        }

        /// <summary>The world position of mark <paramref name="order"/>, on the floor, the first unblocked candidate.</summary>
        public Vector3 MarkPosition(int order)
        {
            var eye = Head.position;
            var fwd = Flat(Head.forward);
            var floor = FloorBelow(eye);
            foreach (var m in CompanionMarks.Candidates(order))
            {
                var dir = Quaternion.Euler(0f, m.Bearing, 0f) * fwd;
                var p = new Vector3(eye.x, floor, eye.z) + dir * m.Distance;
                if (Blocked(new Vector3(eye.x, floor + 1f, eye.z), dir, m.Distance)) continue;   // a wall between
                return p;
            }
            var first = CompanionMarks.For(order);
            return new Vector3(eye.x, floor, eye.z) + Quaternion.Euler(0f, first.Bearing, 0f) * fwd * first.Distance;
        }

        readonly RaycastHit[] _hits = new RaycastHit[16];

        /// <summary>Anything solid on the line except the companions themselves.</summary>
        bool Blocked(Vector3 from, Vector3 dir, float distance)
        {
            var n = Physics.RaycastNonAlloc(from, dir, _hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < n; i++)
            {
                var t = _hits[i].collider.transform;
                var mine = false;
                foreach (var f in _figures.Values) if (t == f || t.IsChildOf(f)) { mine = true; break; }
                if (!mine) return true;
            }
            return false;
        }

        /// <summary>
        /// The floor the companions stand on. Under a floor-tracked XR Origin that is the origin's own
        /// height - exact, and it never hits the visitor's body. Otherwise the first upward-facing
        /// surface below the eye that is not a CharacterController: measured in the Editor, a plain
        /// downward ray struck the rig's own capsule and stood every companion at y 1.31.
        /// </summary>
        float FloorBelow(Vector3 eye)
        {
            var origin = Head != null ? Head.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() : null;
            if (origin != null) return origin.transform.position.y;
            var n = Physics.RaycastNonAlloc(eye, Vector3.down, _hits, 3f, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue; var y = eye.y - 1.6f;
            for (var i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider is CharacterController || h.normal.y < 0.7f || h.distance >= best) continue;
                best = h.distance; y = h.point.y;
            }
            return y;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized; }

        // ---- turns ---------------------------------------------------------------------

        /// <summary>Each in turn answers. Lines start only when the gaze gate is open.</summary>
        public void BeginTurns()
        {
            Turns = new TurnTaking(_ids);
            Turns.Started += id =>
            {
                var line = LineFor?.Invoke(id) ?? string.Empty;
                _lineLeft = EstimateSeconds(line);
                LineStarted?.Invoke(id, line);
            };
            Turns.Ended += id => LineEnded?.Invoke(id);
            Turns.Finished += () => { TurnsFinished?.Invoke(); ConfirmInput.Drop(this); };
            ConfirmInput.Take(this);
            Turns.Begin();
        }

        /// <summary>About 2.6 words a second, plus a breath: her lines are one sentence.</summary>
        public static float EstimateSeconds(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return 1f;
            return 1f + line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length / 2.6f;
        }

        public void LineFinished() => Turns?.LineFinished();

        /// <summary>The angle between the visitor's gaze and a companion, degrees, flat.</summary>
        public float AngleFromGaze(string id)
        {
            if (Head == null || id == null || !_figures.TryGetValue(id, out var f)) return 180f;
            return Vector3.Angle(Flat(Head.forward), Flat(f.position - Head.position));
        }

        public bool Confirm() { Turns?.Advance(); return Turns != null; }
        public bool Redo() => false;

        void Update()
        {
            if (Head == null && Camera.main != null) Head = Camera.main.transform;
            if (Head == null || _ids.Count == 0) return;

            if (!FollowVisitor) { _placedOnce = true; }
            else if (!_placedOnce) PlaceAll();
            else
            {
                var moved = Vector3.Distance(Flat3(Head.position), Flat3(_lastPos));
                var turned = Mathf.Abs(Mathf.DeltaAngle(Head.eulerAngles.y, _lastYaw));
                // A jump in a single frame is a teleport or a snap turn: re-mark. A slow walk or a
                // glance is not, so nobody shuffles about while the visitor looks round.
                var behind = false;
                foreach (var id in _ids) if (AngleFromGaze(id) > BehindDegrees) behind = true;
                _behindFor = behind ? _behindFor + Time.deltaTime : 0f;
                var speaking = Turns != null && Turns.Current == TurnTaking.Phase.Speaking;
                if (moved > JumpDistance || turned > JumpDegrees || (_behindFor > BehindSeconds && !speaking))
                {
                    PlaceAll(); _behindFor = 0f; Remarked?.Invoke();
                }
                _lastPos = Head.position; _lastYaw = Head.eulerAngles.y;
            }

            if (Turns == null) return;
            if (!FollowVisitor) FaceVisitor(Time.deltaTime);
            Turns.Tick(Time.deltaTime, FollowVisitor ? AngleFromGaze(Turns.Speaker) : 0f);
            if (TimeLinesByLength && Turns.Current == TurnTaking.Phase.Speaking)
            {
                _lineLeft -= Time.deltaTime;
                if (_lineLeft <= 0f) Turns.LineFinished();
            }
        }

        static Vector3 Flat3(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Slow enough to read as a person turning, not a snap.</summary>
        public const float TurnDegreesPerSecond = 90f;

        /// <summary>On their marks, each companion turns its body (yaw only) towards the visitor.</summary>
        void FaceVisitor(float dt)
        {
            foreach (var id in _ids)
            {
                var f = _figures[id];
                var to = Head.position - f.position; to.y = 0f;
                if (to.sqrMagnitude < 1e-4f) continue;
                f.rotation = Quaternion.RotateTowards(f.rotation, Quaternion.LookRotation(to.normalized, Vector3.up),
                                                      TurnDegreesPerSecond * dt);
            }
        }
    }
}
