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
        public const float FollowScale = 0.3f, FollowAhead = 2.6f, FollowAbove = -1.0f;   // FollowVisitor's drop: negative is above

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
            // It follows the visitor high up, as the Gate's instruction card does (2.6 m ahead, 1 m above the eye),
            // small (Saul, 5 Oct: "follow the user high up like the instruction panel in the very first world",
            // 0.3 scale "since it will follow us, it does not need to be this big").
            go.transform.localScale = Vector3.one * FollowScale;
            FollowVisitor.Attach(go, FollowAhead, FollowAbove);
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

        readonly List<(TextMeshProUGUI mark, TextMeshProUGUI state, UnityEngine.UI.Image edge)> _rows = new List<(TextMeshProUGUI, TextMeshProUGUI, UnityEngine.UI.Image)>();
        readonly List<string> _ids = new List<string>();

        /// <summary>
        /// Her light panel, as the choice panels are (Saul, 5 Oct: Skylar disliked the dark cards; one style, fixed
        /// text sizes): a white card, a kicker, the instruction in her serif, one row per option with its number -
        /// a gold tick once heard - and what to do on the right.
        /// </summary>
        void Build()
        {
            var c = MuseUi.Canvas(transform, "Choice", 2.6f, 320f);
            _group = c.gameObject.AddComponent<CanvasGroup>();
            var card = MuseUi.Card(c, MuseTheme.Paper, MuseTheme.PanelRadius, MuseTheme.Line, 1f, padX: 16f, padY: 14f, gap: 8f, name: "Card");
            MuseUi.Text(card, _kicker.ToUpperInvariant(), MuseUi.Face.Sans, ChoicePanel.KickerPx, MuseTheme.Ink3, 0.14f, name: "Kicker");
            _title = MuseUi.Text(card, "", MuseUi.Face.Serif, ChoicePanel.PromptPx, MuseTheme.Ink, name: "Title");
            _title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = ChoicePanel.PromptPx * 1.5f;
            var labels = new List<string>(); foreach (var kv in _targets) labels.Add(kv.Value);
            for (var i = 0; i < labels.Count; i++)
            {
                var row = MuseUi.Card(card, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 10f, padY: 7f, gap: 0f, name: "Option " + i);
                var line = MuseUi.Row(row, 8f, TextAnchor.MiddleLeft, "Row");
                var mark = MuseUi.Text(line, (i + 1).ToString("00"), MuseUi.Face.Mono, ChoicePanel.NumberPx, MuseTheme.Ink3, name: "Number");
                mark.enableWordWrapping = false;
                var ml = mark.gameObject.AddComponent<UnityEngine.UI.LayoutElement>(); ml.flexibleWidth = 0f; ml.minWidth = ml.preferredWidth = 16f;
                var l = MuseUi.Text(line, labels[i], MuseUi.Face.Sans, ChoicePanel.OptionPx, MuseTheme.Ink, name: "Label");
                l.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
                var st = MuseUi.Text(line, "", MuseUi.Face.Sans, ChoicePanel.FooterPx, MuseTheme.Ink3, name: "State");
                st.enableWordWrapping = false; st.alignment = TextAlignmentOptions.Right;
                var sl = st.gameObject.AddComponent<UnityEngine.UI.LayoutElement>(); sl.flexibleWidth = 0f; sl.minWidth = sl.preferredWidth = 62f;   // fixed: it ran past the row's edge
                var edge = row.Find("Edge");
                _rows.Add((mark, st, edge != null ? edge.GetComponent<UnityEngine.UI.Image>() : null));
                _ids.Add(labels[i]);
            }
            Refresh();
        }

        void Refresh()
        {
            if (_title == null) return;
            _title.text = Ready ? _then : "Before you choose, hear your companions on each";
            for (var i = 0; i < _rows.Count; i++)
            {
                var heard = Options.IsHeard(_ids[i]);
                var (mark, state, edge) = _rows[i];
                mark.text = heard ? "✓" : (i + 1).ToString("00");
                mark.color = heard ? MuseTheme.GoldInk : MuseTheme.Ink3;
                state.text = heard ? "heard" : "point to hear";
                state.color = heard ? MuseTheme.GoldInk : MuseTheme.Ink3;
                if (edge != null) edge.color = heard ? MuseTheme.Gold : MuseTheme.Line;
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
                transform.localScale = Vector3.one * FollowScale * (1f + 0.06f * Mathf.Sin(_nudge * 18f) * Mathf.Clamp01(_nudge));
                if (_nudge <= 0f) transform.localScale = Vector3.one * FollowScale;
            }
        }
    }
}
