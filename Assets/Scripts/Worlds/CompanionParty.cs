using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace MusePico.Worlds
{
    /// <summary>
    /// The masters keep you company: they stand still, and when you walk away or turn properly
    /// round they walk to a new place beside you.
    ///
    /// <b>CORRECTION (16 Sep 2026).</b> The first port followed
    /// <c>muse-infinity/lib/museum3d.js:updateCompanions</c> literally — damp toward a
    /// camera-relative slot every single frame. In a browser that is fine. In a headset it made
    /// the masters feel "attached to my head" (Saul, on device): the visitor's head never truly
    /// stops moving, so neither did they, and three people drifted continuously in the periphery.
    /// The fix is hysteresis, not more damping. They now hold a FORMATION ANCHOR and ignore the
    /// visitor entirely until the visitor leaves <see cref="repositionDistance"/> or turns past
    /// <see cref="repositionAngle"/> — then they re-form, walk, and stop again.
    ///
    /// Three things follow from that, and all three are the point:
    ///   * standing still is the default state, so the walk animation only plays while they are
    ///     actually travelling;
    ///   * the slot maths is unchanged, it is just evaluated against the anchor instead of the
    ///     live head;
    ///   * they are separated from each other every frame, because two masters converging on
    ///     clamped slots used to end up inside one another.
    /// </summary>
    public class CompanionParty : MonoBehaviour
    {
        [Header("Who walks with you")]
        [Tooltip("The imported .glb prefabs, in slot order. muse-infinity's default three are " +
                 "Monet, Van Gogh and Socrates — app.js seeds selectedCompanions with exactly those.")]
        public GameObject[] avatars = new GameObject[0];

        [Tooltip("Every master is normalised to this height from its own bounds, because a Tripo " +
                 "mesh has no authored scale.")]
        public float humanHeight = 1.75f;

        [Header("Animation")]
        [Tooltip("Controller applied to any spawned master whose Animator has none. glTFast gives " +
                 "an imported .glb an Animator but never a controller, and that cannot be fixed on " +
                 "the imported asset itself — so it is supplied here instead.")]
        public RuntimeAnimatorController walkController;

        [Tooltip("Ground speed the walk clip was authored at, m/s. MEASURED: sample the clip, take " +
                 "foot.L's fore-aft excursion in hip space, scale to human height, double it " +
                 "because the body advances two step lengths per cycle. 1.055 m/s for this rig.")]
        public float clipGroundSpeed = 1.055f;

        [Tooltip("Smoothing on the measured speed, per second — raw per-frame speed makes the " +
                 "playback rate stutter visibly.")]
        [Range(0.5f, 20f)] public float speedSmoothing = 8f;

        [Header("Formation — muse-infinity slots, (back, side) in metres. Negative back = ahead.")]
        [Tooltip("Leave empty to use CompanionFormation.DefaultSlots: (-2.7,-1.6) (-2.7,+1.6) (-3.4,0).")]
        public Vector2[] slots = new Vector2[0];

        [Tooltip("Fraction of the remaining gap closed per frame at 60 fps, WHILE RE-FORMING. " +
                 "They do not move at all when settled.")]
        [Range(0.01f, 0.5f)] public float followDamping = CompanionFormation.ReferenceDampingPerFrame;

        [Header("When do they bother moving?")]
        [Tooltip("How far you may wander from the spot they formed around before they re-form. " +
                 "This is the setting that stops them feeling stuck to your head.")]
        [Range(0.3f, 6f)] public float repositionDistance = 1.6f;

        [Tooltip("How far you may turn before they re-form, degrees. Generous on purpose: looking " +
                 "at a painting must not send three people walking.")]
        [Range(15f, 180f)] public float repositionAngle = 70f;

        [Tooltip("Once re-forming, they stop when every master is this close to their slot and you " +
                 "have stopped walking.")]
        [Range(0.05f, 1.5f)] public float settleRadius = 0.35f;

        [Tooltip("Below this speed you count as standing still, m/s.")]
        [Range(0.02f, 1f)] public float visitorStillSpeed = 0.15f;

        [Header("Spacing")]
        [Tooltip("How close a master may ever get to YOU. Enforced after the walk-box clamp — " +
                 "walking into a wall pins their slots to the box edge, and without this they end " +
                 "up standing inside you.")]
        [Range(0.4f, 3f)] public float personalSpace = CompanionFormation.PersonalSpace;

        [Tooltip("How close two masters may stand to EACH OTHER. Without this they converge on " +
                 "clamped slots and overlap into one blob.")]
        [Range(0.3f, 2.5f)] public float companionSeparation = CompanionFormation.CompanionSeparation;

        [Header("World")]
        [Tooltip("Optional. When set, slots are clamped inside this walk box and the floor height " +
                 "comes from it. Left empty, the WalkRig in the scene supplies both.")]
        public WalkRig walkRig;

        class Member
        {
            public Transform transform;
            public Vector2 slot;
            public float footOffset;
            public Animator animator;
            public Vector3 lastPosition;
            public float smoothedSpeed;
            public float facingYaw;
        }

        static readonly int SpeedParam = Animator.StringToHash("Speed");
        static readonly int TalkingParam = Animator.StringToHash("Talking");

        readonly List<Member> _party = new List<Member>();
        readonly List<float> _hits = new List<float>();
        readonly RaycastHit[] _hitBuffer = new RaycastHit[32];

        XROrigin _origin;
        Camera _camera;
        Vector3 _anchorPos;
        float _anchorYaw;
        bool _reforming;
        bool _seeded;
        Vector3 _visitorLast;
        float _visitorSpeed;
        Vector2[] _targets = new Vector2[0];
        Vector2[] _slotOf = new Vector2[0];

        public int Count => _party.Count;
        /// <summary>True while the party is walking to new places. False when they are standing.</summary>
        public bool IsReforming => _reforming;
        public float SpeedOf(int i) => (i >= 0 && i < _party.Count) ? _party[i].smoothedSpeed : 0f;
        public bool IsAnimated(int i) => i >= 0 && i < _party.Count && _party[i].animator != null;

        /// <summary>
        /// The transform a companion is standing on, or null. Needed so something can be attached
        /// to them - a name plate, a voice, or the collider that makes them pointable.
        /// </summary>
        public Transform TransformOf(int i) => (i >= 0 && i < _party.Count) ? _party[i].transform : null;

        /// <summary>Put every master into, or out of, the talking animation.</summary>
        public void SetTalking(bool talking)
        {
            foreach (var m in _party)
                if (m.animator != null) m.animator.SetBool(TalkingParam, talking);
        }

        /// <summary>Put one master into, or out of, the talking animation.</summary>
        public void SetTalking(int index, bool talking)
        {
            if (index < 0 || index >= _party.Count) return;
            var a = _party[index].animator;
            if (a != null) a.SetBool(TalkingParam, talking);
        }

        void Start()
        {
            _origin = FindAnyObjectByType<XROrigin>();
            _camera = _origin != null ? _origin.Camera : Camera.main;
            if (walkRig == null) walkRig = FindAnyObjectByType<WalkRig>();

            if (avatars == null || avatars.Length == 0)
            {
                Debug.LogWarning("[CompanionParty] no avatars assigned — nobody will walk with you.");
                return;
            }
            Spawn();

            _targets = new Vector2[_party.Count];
            _slotOf = new Vector2[_party.Count];
            for (int i = 0; i < _party.Count; i++) _slotOf[i] = _party[i].slot;
        }

        void Spawn()
        {
            var useSlots = (slots != null && slots.Length > 0) ? slots : CompanionFormation.DefaultSlots;

            for (int i = 0; i < avatars.Length; i++)
            {
                var prefab = avatars[i];
                if (prefab == null) { Debug.LogWarning($"[CompanionParty] avatar slot {i} is empty."); continue; }

                var instance = Instantiate(prefab, transform);
                instance.name = "Companion_" + prefab.name;

                var bounds = MeasureRenderers(instance.transform);
                float scale = CompanionFormation.HeightNormalisingScale(bounds.size.y, humanHeight);
                instance.transform.localScale = Vector3.one * scale;

                var scaled = MeasureRenderers(instance.transform);
                float footOffset = -scaled.min.y;

                var animator = instance.GetComponentInChildren<Animator>(true);
                if (animator != null && animator.runtimeAnimatorController == null && walkController != null)
                    animator.runtimeAnimatorController = walkController;
                if (animator != null && animator.runtimeAnimatorController == null)
                {
                    Debug.LogWarning($"[CompanionParty] {prefab.name} has an Animator but no controller — " +
                                     "it will stand still. Assign a controller on the imported model.");
                    animator = null;
                }
                if (animator != null) animator.applyRootMotion = false;

                _party.Add(new Member
                {
                    transform = instance.transform,
                    slot = useSlots[i % useSlots.Length],
                    footOffset = footOffset,
                    animator = animator,
                    lastPosition = instance.transform.position,
                });

                Debug.Log($"[CompanionParty] {prefab.name}: native height {bounds.size.y:F2} m -> " +
                          $"scale {scale:F3}, footOffset {footOffset:F3}, slot {useSlots[i % useSlots.Length]}" +
                          $", animator={(animator != null ? "yes" : "none (will slide)")}");
            }
        }

        /// <summary>
        /// World-space bounds of every renderer under a root, or an empty box if none.
        ///
        /// A SkinnedMeshRenderer's own <c>bounds</c> are padded to cover the animation, so measuring
        /// one gives a taller figure than the mesh really is — the rigged Van Gogh reads 1.082 m
        /// against the static models' 0.982 m, which would normalise him ~9% short. Use the shared
        /// mesh's REST bounds, which is effectively what a static model already gives.
        /// </summary>
        static Bounds MeasureRenderers(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.position, Vector3.zero);

            bool any = false;
            var b = new Bounds();
            foreach (var r in renderers)
            {
                Bounds rb;
                var skinned = r as SkinnedMeshRenderer;
                if (skinned != null && skinned.sharedMesh != null)
                {
                    var local = skinned.sharedMesh.bounds;
                    var m = r.transform.localToWorldMatrix;
                    rb = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
                    var e = local.extents;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = local.center + new Vector3(
                            (c & 1) == 0 ? -e.x : e.x,
                            (c & 2) == 0 ? -e.y : e.y,
                            (c & 4) == 0 ? -e.z : e.z);
                        rb.Encapsulate(m.MultiplyPoint3x4(corner));
                    }
                }
                else rb = r.bounds;

                if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
            }
            return b;
        }

        void LateUpdate()
        {
            if (_party.Count == 0 || _origin == null) return;

            float dt = Time.deltaTime;
            var visitor = _origin.transform.position;
            float headYaw = _camera != null ? _camera.transform.eulerAngles.y : _origin.transform.eulerAngles.y;

            if (!_seeded)
            {
                _anchorPos = visitor; _anchorYaw = headYaw; _visitorLast = visitor;
                _seeded = true; _reforming = true;
            }

            // How fast is the visitor actually travelling? Used both to trigger a re-form and to
            // decide when they are allowed to settle again.
            float vInstant = dt > 0f ? Vector3.Distance(Flat(visitor), Flat(_visitorLast)) / dt : 0f;
            _visitorLast = visitor;
            float vk = dt > 0f ? 1f - Mathf.Exp(-6f * dt) : 1f;
            _visitorSpeed += (vInstant - _visitorSpeed) * vk;

            // --- the hysteresis: ignore the visitor entirely until they leave the box or turn ---
            float moved = Vector3.Distance(Flat(visitor), Flat(_anchorPos));
            float turned = Mathf.Abs(Mathf.DeltaAngle(_anchorYaw, headYaw));
            if (!_reforming && (moved > repositionDistance || turned > repositionAngle))
                _reforming = true;

            if (_reforming)
            {
                // While re-forming the anchor tracks the visitor, so they walk WITH someone who
                // keeps going rather than to where that person used to be.
                _anchorPos = visitor;
                _anchorYaw = Mathf.MoveTowardsAngle(_anchorYaw, headYaw, 180f * dt);
            }

            bool hasBox = walkRig != null;
            Vector4 box = hasBox ? walkRig.bounds * walkRig.worldScale : Vector4.zero;
            float visitorGround = hasBox ? walkRig.groundY * walkRig.worldScale : visitor.y;
            visitorGround = Mathf.Min(visitorGround, visitor.y);

            float yawRad = _anchorYaw * Mathf.Deg2Rad;
            for (int i = 0; i < _party.Count; i++)
            {
                var t = CompanionFormation.SlotTarget(_anchorPos, yawRad, _party[i].slot);
                if (hasBox) t = CompanionFormation.ClampToBounds(t, box);
                _targets[i] = t;
            }
            // Keep them out of each other, then out of the visitor. Order matters: separation can
            // push someone toward the visitor, so personal space is re-applied last (inside).
            CompanionFormation.Separate(_targets, visitor, _slotOf, companionSeparation, personalSpace);

            float damp = CompanionFormation.DampFactor(followDamping, dt);
            var eye = _camera != null ? _camera.transform.position : visitor;
            bool allSettled = true;

            for (int i = 0; i < _party.Count; i++)
            {
                var m = _party[i];
                var p = m.transform.position;
                float gap = Vector2.Distance(new Vector2(p.x, p.z), _targets[i]);
                if (gap > settleRadius) allSettled = false;

                if (_reforming)
                {
                    p.x += (_targets[i].x - p.x) * damp;
                    p.z += (_targets[i].y - p.z) * damp;
                }

                float ground = SampleGround(p.x, p.z, visitorGround);
                ground = CompanionFormation.ClampGroundToVisitor(ground, visitorGround);
                p.y = ground + m.footOffset;
                m.transform.position = p;

                // Turn to face the visitor's position — NOT their head direction, so simply
                // looking around never rotates anybody. Damped so it is a turn, not a snap.
                float want = CompanionFormation.FacingYawDegrees(p, new Vector3(eye.x, p.y, eye.z));
                m.facingYaw = Mathf.LerpAngle(m.facingYaw, want, dt > 0f ? 1f - Mathf.Exp(-3f * dt) : 1f);
                m.transform.rotation = Quaternion.Euler(0f, m.facingYaw, 0f);

                DriveAnimation(m, p, dt);
            }

            // Stop once everyone has arrived AND the visitor has stopped walking. Without the
            // second condition they settle mid-stride while you are still moving away, then
            // immediately re-trigger, which reads as a stutter.
            if (_reforming && allSettled && _visitorSpeed < visitorStillSpeed)
                _reforming = false;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>
        /// Drive the Animator from how fast this master is really moving: Idle when standing,
        /// Walk at a rate matched to their ground speed while travelling.
        ///
        /// Still a speed match, NOT foot IK — the rig has no IK chain, so a planted foot is never
        /// locked to the floor. The difference now is that they stand still most of the time, so
        /// the slide only exists while they are genuinely walking.
        /// </summary>
        void DriveAnimation(Member m, Vector3 position, float dt)
        {
            float instant = dt > 0f ? Vector3.Distance(Flat(position), Flat(m.lastPosition)) / dt : 0f;
            m.lastPosition = position;
            float k = dt > 0f ? 1f - Mathf.Exp(-speedSmoothing * dt) : 1f;
            m.smoothedSpeed += (instant - m.smoothedSpeed) * k;

            if (m.animator == null) return;
            float rate = clipGroundSpeed > 0.01f ? m.smoothedSpeed / clipGroundSpeed : 0f;
            m.animator.SetFloat(SpeedParam, rate);
        }

        /// <summary>Every downward hit under (x,z) reduced to one height by the same rule the
        /// visitor stands on.</summary>
        float SampleGround(float x, float z, float reference)
        {
            float worldScale = walkRig != null ? walkRig.worldScale : 1f;
            float top = reference + 50f * worldScale;
            int count = Physics.RaycastNonAlloc(new Vector3(x, top, z), Vector3.down,
                                                _hitBuffer, 120f * worldScale);
            _hits.Clear();
            for (int i = 0; i < count; i++) _hits.Add(_hitBuffer[i].point.y);
            return WalkGround.PickGroundHeight(_hits, reference, worldScale);
        }
    }
}
