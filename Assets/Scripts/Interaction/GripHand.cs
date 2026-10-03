using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// One hand's grip. Closing the grip takes the nearest grippable within reach of the hand, or,
    /// failing that, the first one along the controller's ray (her comfort rule: grabbables "can
    /// also be ray-grabbed at a distance"). Holding it, the hand's stick is the object's, not the
    /// locomotion's: <see cref="LocomotionLock"/> takes teleport and snap turn off that stick until
    /// the grip opens.
    /// </summary>
    public sealed class GripHand : MonoBehaviour
    {
        /// <summary>A grip closing this close to an object's collider takes it by hand.</summary>
        public const float NearReach = 0.1f;
        /// <summary>How far the ray reaches. Her grabbables sit within a step or two.</summary>
        public const float RayReach = 4f;

        public IHandSource Source { get; private set; }
        public IGrippable Held { get; private set; }
        public bool HeldByRay { get; private set; }

        /// <summary>Every hand alive, so a grab can tell whether the other hand already has it.</summary>
        public static readonly List<GripHand> All = new List<GripHand>();

        bool _wasGripping;
        readonly Collider[] _near = new Collider[16];
        readonly RaycastHit[] _hits = new RaycastHit[16];

        public static GripHand Attach(GameObject host, IHandSource source)
        {
            var hand = host.AddComponent<GripHand>();
            hand.Source = source;
            return hand;
        }

        void OnEnable() => All.Add(this);

        void OnDisable()
        {
            All.Remove(this);
            Drop();
        }

        void Update()
        {
            if (Source == null || Source.Aim == null) return;
            var gripping = Source.Grip;
            if (gripping && !_wasGripping) TryTake();
            else if (!gripping && _wasGripping) Drop();
            _wasGripping = gripping;
            Held?.Hold(this);
        }

        /// <summary>What the grip would take right now, and whether by ray. Null when nothing is in reach.</summary>
        public IGrippable Target(out bool byRay)
        {
            byRay = false;
            var aim = Source.Aim;
            IGrippable best = null;
            var bestD = float.MaxValue;
            var n = Physics.OverlapSphereNonAlloc(aim.position, NearReach, _near, ~0, QueryTriggerInteraction.Collide);
            for (var i = 0; i < n; i++)
            {
                var g = _near[i].GetComponentInParent<IGrippable>();
                if (g == null) continue;
                var d = Vector3.Distance(_near[i].ClosestPoint(aim.position), aim.position);
                if (d < bestD) { best = g; bestD = d; }
            }
            if (best != null) return best;

            var hits = Physics.RaycastNonAlloc(aim.position, aim.forward, _hits, RayReach, ~0, QueryTriggerInteraction.Collide);
            var nearest = float.MaxValue;
            for (var i = 0; i < hits; i++)
            {
                var g = _hits[i].collider.GetComponentInParent<IGrippable>();
                if (g == null || _hits[i].distance >= nearest) continue;
                best = g; nearest = _hits[i].distance;
            }
            byRay = best != null;
            return best;
        }

        void TryTake()
        {
            var target = Target(out var byRay);
            if (target == null) return;
            foreach (var other in All) if (other != this && other.Held == target) return;   // one hand per object
            if (!target.TryGrab(this, byRay)) return;
            Held = target;
            HeldByRay = byRay;
            LocomotionLock.Acquire(Source.Hand);
        }

        void Drop()
        {
            if (Held == null) return;
            var was = Held;
            Held = null;
            LocomotionLock.Release(Source.Hand);
            was.Release(this);
        }

        /// <summary>Let go from code: a station that refuses the piece mid-hold, or a scene change.</summary>
        public void ForceRelease() => Drop();
    }
}
