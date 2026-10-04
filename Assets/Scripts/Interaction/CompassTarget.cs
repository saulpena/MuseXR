using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Something in a world the visitor can interact with, in the order the compass leads them to it.
    /// The compass points at the lowest-<see cref="order"/> target that is active and not yet done; when
    /// the visitor interacts with it (<see cref="MarkDone"/>, or its <see cref="Pointable"/> is selected)
    /// the compass moves on to the next. Inactive targets - a world not loaded yet - are skipped.
    /// </summary>
    public sealed class CompassTarget : MonoBehaviour
    {
        public int order;
        public string label;
        public bool Done { get; private set; }

        static readonly List<CompassTarget> All = new List<CompassTarget>();

        public static CompassTarget Add(GameObject go, int order, string label)
        {
            var t = go.GetComponent<CompassTarget>();
            if (t == null) t = go.AddComponent<CompassTarget>();
            t.order = order; t.label = label;
            return t;
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            var p = GetComponent<Pointable>();
            if (p != null) { p.Selected -= OnSelected; p.Selected += OnSelected; }
        }

        void OnDisable() => All.Remove(this);

        void OnSelected(Pointable _, Pointer __) => MarkDone();

        public void MarkDone() => Done = true;

        /// <summary>The target the compass points at now, or null.</summary>
        public static CompassTarget Current(Vector3 from)
        {
            CompassTarget best = null;
            var bestDistance = float.MaxValue;
            foreach (var t in All)
            {
                if (t == null || t.Done || !t.isActiveAndEnabled) continue;
                var d = (t.transform.position - from).sqrMagnitude;
                if (best == null || t.order < best.order || (t.order == best.order && d < bestDistance))
                {
                    best = t; bestDistance = d;
                }
            }
            return best;
        }
    }
}
