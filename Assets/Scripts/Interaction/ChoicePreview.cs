using System;
using System.Collections.Generic;
using MuseXR.UI;
using TMPro;
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
    /// and heard the companions on it, so they know what they are choosing between. A card stands by the
    /// options: "Before you choose · hear your companions on each", one line per option that ticks as it is
    /// heard (a master speaking on it, from a tap or from walking up), then her instruction for the choice
    /// itself. The chapter gates its grab or its slot on <see cref="Ready"/>.
    /// </summary>
    public sealed class ChoicePreview : MonoBehaviour
    {
        public const string HowToHear = "point at it and pull the trigger";
        public const float ShowDistance = 9f;

        public OptionsHeard Options { get; private set; }
        public bool Ready => Options == null || Options.AllHeard;
        public event Action Unlocked;

        readonly Dictionary<InsightTarget, string> _targets = new Dictionary<InsightTarget, string>();
        string _kicker, _then;
        TextMeshProUGUI _title, _list;
        CanvasGroup _group;
        float _nudge;

        /// <summary>
        /// A card at <paramref name="at"/> facing <paramref name="facing"/> (the way the visitor looks at it),
        /// listing <paramref name="options"/>; once every one is heard it reads <paramref name="then"/>.
        /// </summary>
        public static ChoicePreview Make(Transform parent, Vector3 at, Vector3 facing, string kicker,
                                         IList<(InsightTarget target, string label)> options, string then)
        {
            var go = new GameObject("Choice Preview · " + kicker);
            go.transform.SetParent(parent, true);
            facing.y = 0f;
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.forward, Vector3.up));   // +Z away from the viewer reads
            var p = go.AddComponent<ChoicePreview>();
            var list = new List<(string, string)>();
            foreach (var (t, label) in options)
            {
                if (t == null) continue;
                t.askReply = false;
                t.onApproach = true;   // a choice piece: walking up to it is enough to hear the companions   // the reason is asked once the choice is made, not per option
                p._targets[t] = label;   // a target's id may be empty (the crane, the turtle): the label is the key
                list.Add((label, label));
            }
            p.Options = new OptionsHeard(list);
            p._kicker = kicker; p._then = then;
            p.Build();
            return p;
        }

        void OnEnable() => MasterInsights.Spoke += OnSpoke;
        void OnDisable() => MasterInsights.Spoke -= OnSpoke;

        void OnSpoke(InsightTarget t)
        {
            if (t == null || !_targets.TryGetValue(t, out var id)) return;
            if (Options.MarkHeard(id)) { Refresh(); Unlocked?.Invoke(); Debug.Log("[Choice] every option heard: " + _kicker); }
            else Refresh();
        }

        /// <summary>The choice is made: the card has done its job.</summary>
        public void Close() { if (this != null) { _closed = true; Appear.Out(gameObject, 0.35f, destroy: true); } }
        bool _closed;

        /// <summary>The visitor reached for the choice too early: the card pulses and says why.</summary>
        public void Nudge() => _nudge = 1.2f;

        void Build()
        {
            var c = MuseUi.Canvas(transform, "Choice", 3f, 420f);
            _group = c.gameObject.AddComponent<CanvasGroup>();
            var glass = MuseUi.Card(c, new Color32(8, 6, 10, 200), MuseTheme.OptionRadius, new Color32(238, 233, 223, 60), 1f, padX: 18f, padY: 14f, gap: 6f, name: "Glass");
            var k = MuseUi.Text(glass, _kicker.ToUpperInvariant(), MuseUi.Face.Sans, 11f, new Color32(201, 170, 114, 255), 0.22f, true, name: "Kicker");
            _title = MuseUi.Text(glass, "", MuseUi.Face.Serif, 21f, new Color32(238, 233, 223, 255), lineHeight: 1.1f, name: "Title");
            var fonts = MuseFonts.Get(); if (fonts != null && fonts.display != null) _title.font = fonts.display;
            _list = MuseUi.Text(glass, "", MuseUi.Face.Sans, 13f, new Color32(214, 206, 192, 255), lineHeight: 1.35f, name: "Options");
            Refresh();
        }

        void Refresh()
        {
            if (_title == null) return;
            if (Ready)
            {
                _title.text = _then;
                _list.text = string.Join("\n", Options.Checklist(HowToHear));
            }
            else
            {
                _title.text = "Before you choose, hear your companions on each";
                _list.text = string.Join("\n", Options.Checklist(HowToHear));
            }
        }

        void Update()
        {
            if (_closed) return;   // fading out: its own distance fade must not fight it
            if (_group == null) return;
            var eye = Camera.main != null ? Camera.main.transform : null;
            float near = 1f;
            if (eye != null)
            {
                var d = transform.position - eye.position; d.y = 0f;
                near = d.magnitude < ShowDistance ? 1f : 0f;
            }
            _group.alpha = Mathf.MoveTowards(_group.alpha, near, Time.deltaTime * 2f);
            if (_nudge > 0f)
            {
                _nudge -= Time.deltaTime;
                transform.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(_nudge * 18f) * Mathf.Clamp01(_nudge));
                if (_nudge <= 0f) transform.localScale = Vector3.one;
            }
        }
    }
}
