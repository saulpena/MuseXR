using System;
using MuseXR.Slots;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// A piece the visitor carries: the crane, the turtle, the grotto lamp. Her storyboards:
    ///
    ///   Before   it idle-rotates on its plinth "to signal grabbable" (Palace frame 1).
    ///   During   "the object follows the hand; the stick turns it in 15° steps, angle shown".
    ///   Released away from a slot it "floats back to its plinth; nothing falls to the floor".
    ///
    /// Taken by hand it keeps the grip it was taken with. Taken by ray it comes to the hand over a
    /// quarter second, then follows it. It never has a live rigidbody: nothing here can fall.
    ///
    /// No serialized tuning: <see cref="Make"/> configures it from code, so a scene's saved copy
    /// cannot silently override the values here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Holdable : MonoBehaviour, IGrippable
    {
        /// <summary>Idle spin on the plinth, degrees per second. Slow: a cue, not a show.</summary>
        public const float IdleSpinSpeed = 18f;
        /// <summary>A ray-grabbed piece reaches the hand in this long.</summary>
        public const float ReelSeconds = 0.25f;
        /// <summary>Where a ray-grabbed piece sits: this far ahead of the controller.</summary>
        public const float HeldAhead = 0.12f;

        public enum Mode { Resting, Held, Gliding, Seated }

        public Mode State { get; private set; } = Mode.Resting;
        public string Id { get; private set; } = string.Empty;
        public GripHand HeldBy { get; private set; }
        /// <summary>The hand that last held it: the one to buzz when it lands in a slot after release.</summary>
        public GripHand LastHeldBy { get; private set; }

        /// <summary>Spins while resting on its plinth. Off once a choice is kept.</summary>
        public bool IdleSpin { get; set; }

        /// <summary>Turn added by the stick while held, degrees (15° steps).</summary>
        public float StickDegrees => _stick.Degrees;

        /// <summary>Asked before a grab; a station refuses once its choice is kept.</summary>
        public Func<Holdable, bool> CanGrab;

        public event Action<Holdable> Grabbed;
        public event Action<Holdable> Moved;
        /// <summary>The grip opened. With no listener it floats home on its own.</summary>
        public event Action<Holdable> Released;
        /// <summary>A glide ended (home, or seated in a slot).</summary>
        public event Action<Holdable> Arrived;

        Vector3 _homePos;
        Quaternion _homeRot;
        Vector3 _baseLocal;                 // bottom-centre of its bounds, in its own space
        float _yawAtGrab;
        Vector3 _centreFrom, _nearOffset, _centreLocal;
        bool _byRay;
        float _reel;                        // 0..1 while a ray grab comes in
        readonly StickStepper _stick = new StickStepper();

        Vector3 _glideFrom, _glideTo;
        Quaternion _glideFromRot, _glideToRot;
        float _glideT, _glideSeconds, _glideDistance;
        bool _glideArc;
        Mode _afterGlide;

        public static Holdable Make(GameObject root, string id, bool idleSpin = true)
        {
            var h = root.GetComponent<Holdable>();
            if (h == null) h = root.AddComponent<Holdable>();
            h.Id = id;
            h.IdleSpin = idleSpin;
            h.EnsureCollider();
            h.SetHome();
            return h;
        }

        /// <summary>Where it rests and floats back to: its pose now.</summary>
        public void SetHome()
        {
            _homePos = transform.position;
            _homeRot = transform.rotation;
            _baseLocal = BaseLocal();
            _centreLocal = transform.InverseTransformPoint(Bounds().center);
        }

        public Vector3 HomePosition => _homePos;

        /// <summary>The bottom-centre of the piece, in the world. Slot distances are measured from here.</summary>
        public Vector3 BasePoint => transform.TransformPoint(_baseLocal);

        /// <summary>Its yaw in the world, degrees.</summary>
        public float Yaw => transform.eulerAngles.y;

        // ---- grabbing -----------------------------------------------------------------

        public bool TryGrab(GripHand hand, bool byRay)
        {
            if (HeldBy != null) return false;
            if (CanGrab != null && !CanGrab(this)) return false;
            HeldBy = LastHeldBy = hand;
            State = Mode.Held;
            _stick.Reset();
            var aim = hand.Source.Aim;
            // Her storyboard: "the object follows the hand; the stick turns it in 15° steps". The
            // piece stays upright and keeps its yaw; only the stick turns it, never the wrist.
            _yawAtGrab = transform.eulerAngles.y;
            var centre = Bounds().center;
            _centreFrom = centre - aim.position;                         // world offset, hand to centre
            _byRay = byRay;
            _nearOffset = _centreFrom;
            _reel = byRay ? 0f : 1f;
            Grabbed?.Invoke(this);
            return true;
        }

        public void Hold(GripHand hand)
        {
            if (hand != HeldBy) return;
            var aim = hand.Source.Aim;

            var step = _stick.Update(hand.Source.Stick.x);
            if (step != 0) hand.Source.Buzz(SlotRules.LightAmplitude, SlotRules.LightSeconds);

            if (_reel < 1f) _reel = Mathf.Min(1f, _reel + Time.deltaTime / ReelSeconds);
            // Where its centre should be: held where it was taken (by hand), or just ahead of the
            // controller (by ray), coming in smoothly from where it was.
            var want = _byRay ? aim.forward * HeldAhead : _nearOffset;
            var offset = Vector3.Lerp(_centreFrom, want, Mathf.SmoothStep(0f, 1f, _reel));
            var rot = Quaternion.Euler(0f, _yawAtGrab + _stick.Degrees, 0f) * LevelResidual();
            var pos = aim.position + offset - rot * Vector3.Scale(_centreLocal, transform.lossyScale);
            transform.SetPositionAndRotation(pos, rot);
            Moved?.Invoke(this);
        }

        public void Release(GripHand hand)
        {
            if (hand != HeldBy) return;
            HeldBy = null;
            State = Mode.Resting;
            if (Released != null) Released.Invoke(this);
            else FloatHome();
        }

        // ---- gliding ------------------------------------------------------------------

        /// <summary>Her float-back: an arc to the plinth, smooth at both ends.</summary>
        public void FloatHome()
        {
            var d = Vector3.Distance(transform.position, _homePos);
            Glide(_homePos, _homeRot, FloatHome_Duration(d), arc: true, Mode.Resting);
        }

        static float FloatHome_Duration(float d) => MuseXR.Slots.FloatHome.Duration(d);

        /// <summary>The snap into a slot: level, at its current yaw, its base on the slot's centre.</summary>
        public void SeatAt(Vector3 slotPoint, float yawDegrees)
        {
            var rot = Quaternion.Euler(0f, yawDegrees, 0f) * LevelResidual();
            var pos = slotPoint - rot * Vector3.Scale(_baseLocal, transform.lossyScale);
            Glide(pos, rot, MuseXR.Slots.FloatHome.SnapSeconds, arc: false, Mode.Seated);
        }

        void Glide(Vector3 to, Quaternion toRot, float seconds, bool arc, Mode after)
        {
            _glideFrom = transform.position; _glideFromRot = transform.rotation;
            _glideTo = to; _glideToRot = toRot;
            _glideT = 0f; _glideSeconds = seconds; _glideArc = arc;
            _glideDistance = Vector3.Distance(_glideFrom, to);
            _afterGlide = after;
            State = Mode.Gliding;
        }

        void Update()
        {
            if (State == Mode.Gliding)
            {
                _glideT += Time.deltaTime;
                var along = MuseXR.Slots.FloatHome.Along(_glideT, _glideSeconds);
                var lift = _glideArc ? MuseXR.Slots.FloatHome.Lift(_glideT, _glideSeconds, _glideDistance) : 0f;
                transform.SetPositionAndRotation(Vector3.Lerp(_glideFrom, _glideTo, along) + Vector3.up * lift,
                                                 Quaternion.Slerp(_glideFromRot, _glideToRot, along));
                if (_glideT >= _glideSeconds)
                {
                    transform.SetPositionAndRotation(_glideTo, _glideToRot);
                    State = _afterGlide;
                    Arrived?.Invoke(this);
                }
                return;
            }
            if (State == Mode.Resting && IdleSpin && Vector3.Distance(transform.position, _homePos) < 0.001f)
            {
                // Spin about the world's up through the base, so it turns on the spot.
                var basePoint = BasePoint;
                transform.RotateAround(basePoint, Vector3.up, IdleSpinSpeed * Time.deltaTime);
                _homePos = transform.position;
                _homeRot = transform.rotation;
            }
        }

        // ---- geometry -----------------------------------------------------------------

        /// <summary>Its rest rotation with yaw removed, so a piece modelled tilted stays tilted when seated.</summary>
        Quaternion LevelResidual()
        {
            var yaw = Quaternion.Euler(0f, _homeRot.eulerAngles.y, 0f);
            return Quaternion.Inverse(yaw) * _homeRot;
        }

        Bounds Bounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(transform.position, Vector3.one * 0.1f);
            var b = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        Vector3 CentreLocal()
        {
            var c = Bounds().center;
            return Quaternion.Inverse(transform.rotation) * (c - transform.position);
        }

        Vector3 BaseLocal()
        {
            var b = Bounds();
            var world = new Vector3(b.center.x, b.min.y, b.center.z);
            return transform.InverseTransformPoint(world);
        }

        void EnsureCollider()
        {
            if (GetComponentInChildren<Collider>() != null) return;
            var b = Bounds();
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = transform.InverseTransformPoint(b.center);
            var s = transform.lossyScale;
            box.size = new Vector3(b.size.x / Mathf.Max(1e-4f, s.x), b.size.y / Mathf.Max(1e-4f, s.y), b.size.z / Mathf.Max(1e-4f, s.z));
            box.isTrigger = true;   // picked by the grip's own queries; never pushes the visitor
        }
    }
}
