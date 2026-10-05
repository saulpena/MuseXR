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
        /// <summary>Metres added to the approach distance: walking up counts from its edge, not its pivot.</summary>
        public float reach;
        /// <summary>The masters speak when the visitor walks up to it, not only when it is clicked. Only for a
        /// chapter's choice pieces (the crane and turtle, the lamp's sockets): Saul, 4 Oct - they need not
        /// talk about absolutely everything.</summary>
        public bool onApproach;
        [Tooltip("Offer her reply chips (\"What is this painting to you?\") after the insight. Off for a choice's options, whose reason comes later.")]
        public bool askReply = true;

        public static readonly List<InsightTarget> All = new List<InsightTarget>();

        /// <summary>Mark <paramref name="go"/> as something the masters talk about. Click and approach both work.</summary>
        public static InsightTarget Add(GameObject go, string title, string artist = null, string id = null, bool pointToSpeak = true)
        {
            var t = go.GetComponent<InsightTarget>();
            if (t == null) t = go.AddComponent<InsightTarget>();   // not ??: Unity's fake null defeats it
            t.id = string.IsNullOrEmpty(id) ? go.name : id;
            t.title = title; t.artist = artist;
            // Clickable: a Pointable on the object (or reuse one already there). A grabbable work
            // passes pointToSpeak false and calls Speak from its tap instead: a Pointable answers the
            // trigger going DOWN, so a hold meant to pick the work up would also have spoken.
            if (pointToSpeak)
            {
                var p = go.GetComponent<Pointable>();
                if (p == null) p = Pointable.Make(go, t.id);
                p.Selected += (_, __) => MasterInsights.Ensure().Clicked(t);
            }
            MasterInsights.Ensure();   // walking close needs the watcher running before any click
            return t;
        }

        /// <summary>A click on it: the next master gives an insight (when the companions are free).</summary>
        public void Speak() => MasterInsights.Ensure().Clicked(this);

        /// <summary>
        /// A work the visitor can take in hand: a tap has a master speak about it (and counts it as
        /// seen), a hold takes it off the wall, a second hand scales it (MusePico.Grab.Grabbable).
        /// </summary>
        public static InsightTarget AddGrabbable(GameObject go, string title, string artist = null, string id = null, System.Action alsoOnTap = null)
        {
            var t = Add(go, title, artist, id, pointToSpeak: false);
            var col = go.GetComponent<Collider>();
            if (col == null) { var b = go.AddComponent<BoxCollider>(); b.size = new Vector3(1f, 1f, 0.02f); col = b; }
            MusePico.Grab.Grabbable.Make(go, () => { alsoOnTap?.Invoke(); t.Speak(); }, MusePico.Grab.GrabReach.AtRayEnd, col);
            // The pointer reaches it too (Saul, 5 Oct: no hover and no click on the paintings): its name and corners while
            // the ray is on it, and the trigger has a master speak. The grab's own tap is the same click, so it is not doubled.
            var p = go.GetComponent<Pointable>();
            if (p == null) p = Pointable.Make(go, t.id);
            p.Label = title;
            p.Selected += (_, __) => { alsoOnTap?.Invoke(); t.Speak(); };
            return t;
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);
    }
}
