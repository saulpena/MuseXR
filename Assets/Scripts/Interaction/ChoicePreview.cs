using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Which of a choice's options the visitor has heard the companions on. Pure: no Unity.
    /// </summary>
    public sealed class OptionsHeard
    {
        readonly List<string> _ids = new List<string>();
        readonly List<string> _labels = new List<string>();
        readonly HashSet<string> _heard = new HashSet<string>();

        public OptionsHeard(IEnumerable<(string id, string label)> options)
        {
            foreach (var (id, label) in options) { _ids.Add(id); _labels.Add(label); }
        }

        public int Count => _ids.Count;
        public bool AllHeard => _heard.Count >= _ids.Count;
        public bool IsHeard(string id) => _heard.Contains(id);

        /// <summary>True when this hearing completed the set (the moment to unlock the choice).</summary>
        public bool MarkHeard(string id)
        {
            if (!_ids.Contains(id) || AllHeard) return false;
            _heard.Add(id);
            return AllHeard;
        }

        /// <summary>The card's lines: a tick or an open circle per option, in her order.</summary>
        public IEnumerable<string> Checklist(string howToHear)
        {
            for (var i = 0; i < _ids.Count; i++)
                yield return _heard.Contains(_ids[i]) ? "✓  " + _labels[i] + "  ·  heard" : "○  " + _labels[i] + "  ·  " + howToHear;
        }
    }

    /// <summary>
    /// Saul, 4 Oct 2026: before a chapter asks the visitor to choose, they must have looked at EVERY option
    /// and heard the companions on it, so they know what they are choosing between. The compass says so below its
    /// arrow (<see cref="CompassBrief"/>): "Before you choose", one row per option that ticks as it is heard (a
    /// master speaking on it, from a tap or from walking up), then her instruction for the choice itself. The
    /// chapter gates its grab or its slot on <see cref="Ready"/>.
    ///
    /// Saul, 5 Oct: it used to be its own card following the visitor; it is now part of the compass, which already
    /// points the way - one thing to look at, not two (the card also sat over the Grotto's Stop 2).
    /// </summary>
    public sealed class ChoicePreview : MonoBehaviour
    {
        public const string HearEach = "Before you choose, hear your companions on each";
        public const string ToHear = "point to hear", Heard = "heard";

        public OptionsHeard Options { get; private set; }
        public bool Ready => Options == null || Options.AllHeard;
        public event Action Unlocked;

        readonly Dictionary<InsightTarget, string> _targets = new Dictionary<InsightTarget, string>();
        readonly List<string> _ids = new List<string>();
        string _kicker, _then;
        bool _closed;

        /// <summary>
        /// The brief for <paramref name="options"/>; once every one is heard it reads <paramref name="then"/>.
        /// <paramref name="at"/> and <paramref name="facing"/> are where its card used to stand; kept for the callers.
        /// </summary>
        public static ChoicePreview Make(Transform parent, Vector3 at, Vector3 facing, string kicker,
                                         IList<(InsightTarget target, string label)> options, string then)
        {
            var go = new GameObject("Choice Preview · " + kicker);
            go.transform.SetParent(parent, true);
            go.transform.position = at;
            var p = go.AddComponent<ChoicePreview>();
            var list = new List<(string, string)>();
            foreach (var (t, label) in options)
            {
                if (t == null) continue;
                t.askReply = false;   // the reason is asked once the choice is made, not per option
                t.onApproach = true;  // a choice piece: walking up to it is enough to hear the companions
                p._targets[t] = label;   // a target's id may be empty (the crane, the turtle): the label is the key
                list.Add((label, label));
                p._ids.Add(label);
            }
            p.Options = new OptionsHeard(list);
            p._kicker = kicker; p._then = then;
            p.Post();
            return p;
        }

        void OnEnable() { MasterInsights.Spoke += OnSpoke; if (Options != null && !_closed) Post(); }
        void OnDisable() { MasterInsights.Spoke -= OnSpoke; CompassBrief.Hide(this); }

        void OnSpoke(InsightTarget t)
        {
            if (t == null || !_targets.TryGetValue(t, out var id)) return;
            if (Options.MarkHeard(id)) { Post(); Unlocked?.Invoke(); Debug.Log("[Choice] every option heard: " + _kicker); }
            else Post();
        }

        /// <summary>The choice is made: the brief has done its job.</summary>
        public void Close()
        {
            if (this == null) return;
            _closed = true;
            CompassBrief.Hide(this);
            Destroy(gameObject);
        }

        /// <summary>The visitor reached for the choice too early: the compass pulses.</summary>
        public void Nudge() => CompassBrief.Nudge(this);

        void Post()
        {
            if (_closed) return;
            var rows = new List<CompassBrief.Row>();
            foreach (var id in _ids)
            {
                var heard = Options.IsHeard(id);
                rows.Add(new CompassBrief.Row { Label = id, Done = heard, State = heard ? Heard : ToHear });
            }
            CompassBrief.Show(this, _kicker, Ready ? _then : HearEach, rows);
        }
    }
}
