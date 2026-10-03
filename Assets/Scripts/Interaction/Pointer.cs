using System;
using System.Collections.Generic;
using MuseXR.Slots;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>Anything the trigger's ray can point at: a standee, a lantern, a card, an artwork.</summary>
    public interface IPointable
    {
        void HoverEnter(Pointer pointer);
        void HoverExit(Pointer pointer);
        void Select(Pointer pointer);
    }

    /// <summary>
    /// A plain pointable with C# events, for objects that just need "pointed at" and "chosen". Hover
    /// state is counted across hands, so two rays on one object hover it once.
    /// </summary>
    public sealed class Pointable : MonoBehaviour, IPointable
    {
        int _hovers;

        public string Id { get; set; } = string.Empty;
        public bool Hovered => _hovers > 0;
        /// <summary>Off: rays pass over it without hovering or selecting (a kept choice, a stage not yet open).</summary>
        public bool Interactive { get; set; } = true;

        public event Action<Pointable> Hovering;
        public event Action<Pointable> Unhovered;
        public event Action<Pointable, Pointer> Selected;

        /// <summary>Make <paramref name="go"/> pointable. Adds a collider fitted to its renderers if it has none.</summary>
        public static Pointable Make(GameObject go, string id)
        {
            var p = go.GetComponent<Pointable>();
            if (p == null) p = go.AddComponent<Pointable>();   // not ??: Unity's fake null defeats it
            p.Id = id;
            if (go.GetComponentInChildren<Collider>() == null)
            {
                var rs = go.GetComponentsInChildren<Renderer>();
                var box = go.AddComponent<BoxCollider>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    box.center = go.transform.InverseTransformPoint(b.center);
                    var s = go.transform.lossyScale;
                    box.size = new Vector3(b.size.x / Mathf.Max(1e-4f, s.x), b.size.y / Mathf.Max(1e-4f, s.y), Mathf.Max(0.02f, b.size.z / Mathf.Max(1e-4f, s.z)));
                }
                box.isTrigger = true;
            }
            return p;
        }

        public void HoverEnter(Pointer pointer)
        {
            if (!Interactive) return;
            if (_hovers++ == 0) Hovering?.Invoke(this);
        }

        public void HoverExit(Pointer pointer)
        {
            if (_hovers == 0) return;
            if (--_hovers == 0) Unhovered?.Invoke(this);
        }

        public void Select(Pointer pointer)
        {
            if (Interactive) Selected?.Invoke(this, pointer);
        }

        void OnDisable() { if (_hovers > 0) { _hovers = 0; Unhovered?.Invoke(this); } }
    }

    /// <summary>
    /// One hand's trigger ray (her "Trigger ... selects"). Each frame it finds the first pointable
    /// along the controller's forward, hovers it (with a light tick in the hand) and selects it on
    /// the trigger's press. It stands down while its hand holds something, so a carried piece is
    /// never also "pointed at". Objects carry <see cref="IPointable"/>, not an XRI interactable, so
    /// XRI's own Select (also the trigger) never fires on them twice.
    /// </summary>
    public sealed class Pointer : MonoBehaviour
    {
        public const float Reach = 8f;

        public IHandSource Source { get; private set; }
        public GripHand Grip { get; private set; }
        public IPointable Hovered { get; private set; }
        public Vector3 HitPoint { get; private set; }

        public static readonly List<Pointer> All = new List<Pointer>();

        readonly RaycastHit[] _hits = new RaycastHit[16];
        bool _wasTrigger;

        public static Pointer Attach(GameObject host, IHandSource source, GripHand grip)
        {
            var p = host.AddComponent<Pointer>();
            p.Source = source;
            p.Grip = grip;
            return p;
        }

        void OnEnable() => All.Add(this);

        void OnDisable()
        {
            All.Remove(this);
            SetHover(null);
        }

        void Update()
        {
            if (Source?.Aim == null) return;
            var busy = Grip != null && Grip.Held != null;
            SetHover(busy ? null : Find());
            var trigger = Source.Trigger;
            if (trigger && !_wasTrigger && Hovered != null) Hovered.Select(this);
            _wasTrigger = trigger;
        }

        IPointable Find()
        {
            var aim = Source.Aim;
            var n = Physics.RaycastNonAlloc(aim.position, aim.forward, _hits, Reach, ~0, QueryTriggerInteraction.Collide);
            IPointable best = null;
            var nearest = float.MaxValue;
            for (var i = 0; i < n; i++)
            {
                var p = _hits[i].collider.GetComponentInParent<IPointable>();
                if (p == null || _hits[i].distance >= nearest) continue;
                if (p is Pointable plain && !plain.Interactive) continue;
                best = p; nearest = _hits[i].distance; HitPoint = _hits[i].point;
            }
            return best;
        }

        void SetHover(IPointable next)
        {
            if (ReferenceEquals(next, Hovered)) return;
            Hovered?.HoverExit(this);
            Hovered = next;
            if (next == null) return;
            next.HoverEnter(this);
            Source?.Buzz(SlotRules.LightAmplitude * 0.6f, SlotRules.LightSeconds);
        }

        /// <summary>Whether any hand's ray is on <paramref name="target"/> right now.</summary>
        public static bool AnyOn(IPointable target)
        {
            foreach (var p in All) if (ReferenceEquals(p.Hovered, target)) return true;
            return false;
        }
    }
}
