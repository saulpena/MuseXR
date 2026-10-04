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

    /// <summary>
    /// The masters' insights in a scene: watches the visitor's head against every <see cref="InsightTarget"/>
    /// and, on a click or a close approach, has the next master say her opening line about it through
    /// the scene's companions (and so the waist panel). Never interrupts a chapter's own turns: while
    /// the companions are speaking, approaches wait and clicks are ignored.
    /// </summary>
    public sealed class MasterInsights : MonoBehaviour
    {
        Insights _rule;
        CompanionGroup _group;

        public static MasterInsights Ensure()
        {
            var m = FindAnyObjectByType<MasterInsights>();
            return m != null ? m : new GameObject("Master Insights").AddComponent<MasterInsights>();
        }

        CompanionGroup Group()
        {
            if (_group == null) _group = FindAnyObjectByType<CompanionGroup>();
            if (_group == null) _group = BuildGroup();
            if (_group != null && _rule == null) _rule = new Insights(_group.Ids);
            return _group;
        }

        public void Clicked(InsightTarget t)
        {
            if (Group() == null || t == null) return;
            if (_group.Busy) return;
            _rule.Clicked(t.id);
            Speak(t);
        }

        void Update()
        {
            if (Group() == null || _group.Busy) return;
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            foreach (var t in InsightTarget.All)
            {
                if (t == null) continue;
                var to = t.transform.position - head.position; var flat = new Vector3(to.x, 0f, to.z);
                var g = head.forward; g.y = 0f;
                var off = g.sqrMagnitude > 1e-6f && flat.sqrMagnitude > 1e-6f ? Vector3.Angle(g, flat) : 180f;
                if (_rule.Approached(t.id, flat.magnitude, off)) { Speak(t); return; }
            }
        }

        /// <summary>
        /// A scene with the masters' figures (the layout's "Mark *") but no companions yet: gather them,
        /// as the Palace does - a crowd beside the visitor, with the waist panel for their lines.
        /// </summary>
        CompanionGroup BuildGroup()
        {
            var figures = new Dictionary<string, Transform>();
            var order = new List<string>();
            foreach (var id in Masters.Row)
                foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (t.name == "Mark " + id) { figures[id] = t; order.Add(id); break; }
            if (order.Count == 0) return null;
            var go = new GameObject("Companions");
            go.transform.SetParent(transform, false);
            var g = go.AddComponent<CompanionGroup>();
            g.FollowVisitor = false;
            g.Crowd = true;   // Saul, 3 Oct: always a crowd beside the visitor
            g.Head = Camera.main != null ? Camera.main.transform : null;
            g.Set(order, figures);
            var subtitles = System.Type.GetType("MuseXR.UI.SubtitleRig, MuseXR.UI.Interaction");
            if (subtitles != null) go.AddComponent(subtitles);
            return g;
        }

        void Speak(InsightTarget t)
        {
            var speaker = _rule.NextSpeaker();
            if (speaker == null) return;
            _group.Say(speaker, Insights.Opening(speaker, t.title, t.artist));
            // Heard about counts as seen: the compass moves on (it kept pointing at a work a master had
            // just spoken about when the insight came from walking up rather than a click).
            var compass = t.GetComponent<CompassTarget>();
            if (compass != null) compass.MarkDone();
            Debug.Log("[Insight] " + Masters.Name(speaker) + " on " + t.title);
        }
    }
}
