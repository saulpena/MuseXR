using System.Collections.Generic;
using MuseXR.Slots;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Something a master can speak about: a painting or an interactable object. Clicking it (the
    /// trigger ray) or walking close to it has a master give an insight (<see cref="Insights"/>).
    /// <see cref="Add"/> is all a scene needs; <see cref="MasterInsights"/> does the rest.
    /// </summary>
    public sealed class InsightTarget : MonoBehaviour
    {
        public string id;
        public string title;
        public string artist;

        public static readonly List<InsightTarget> All = new List<InsightTarget>();

        /// <summary>Mark <paramref name="go"/> as something the masters talk about. Click and approach both work.</summary>
        public static InsightTarget Add(GameObject go, string title, string artist = null, string id = null)
        {
            var t = go.GetComponent<InsightTarget>();
            if (t == null) t = go.AddComponent<InsightTarget>();   // not ??: Unity's fake null defeats it
            t.id = string.IsNullOrEmpty(id) ? go.name : id;
            t.title = title; t.artist = artist;
            // Clickable: a Pointable on the object (or reuse one already there).
            var p = go.GetComponent<Pointable>();
            if (p == null) p = Pointable.Make(go, t.id);
            p.Selected += (_, __) => MasterInsights.Ensure().Clicked(t);
            MasterInsights.Ensure();   // walking close needs the watcher running before any click
            return t;
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);
    }
}
