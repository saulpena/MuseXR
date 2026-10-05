using System;
using System.Collections.Generic;
using MuseXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MuseXR.Interaction
{
    /// <summary>
    /// A multiple choice in her web's style (the masters' reply panel, her .opt chips): a white card with a hairline
    /// edge, a small kicker, the prompt in her serif, then one numbered chip per option and a footer line. Every text
    /// has one fixed size - nothing shrinks to fit - and choosing changes only colour, never size or position, so the
    /// panel never jumps or overlaps itself (Saul, 5 Oct: Skylar disliked the bare quad chips; "the font of different
    /// sizes looks unprofessional"; "the text where it shows the option I selected overlaps").
    ///
    /// The panel hangs off this object (+Z away from the viewer). Each chip gets a hit box sized from its own laid-out
    /// corners, so the pointer's ray chooses it, with her hover.
    /// </summary>
    public sealed class ChoicePanel : MonoBehaviour
    {
        public const float WidthPx = 300f, PromptPx = 14f, OptionPx = 12f, NumberPx = 9f, KickerPx = 8.5f, FooterPx = 8.5f;

        readonly List<Pointable> _options = new List<Pointable>();
        readonly List<Image> _fills = new List<Image>();
        readonly List<Image> _edges = new List<Image>();
        readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
        readonly List<TextMeshProUGUI> _numbers = new List<TextMeshProUGUI>();
        TextMeshProUGUI _footer, _prompt, _actionLabel;
        Image _actionFill;
        Pointable _action;
        readonly List<string> _words = new List<string>();
        RectTransform _canvas;

        /// <summary>The options' hit boxes, in order (what the pointer selects).</summary>
        public IReadOnlyList<Pointable> Options => _options;
        public int Chosen { get; private set; } = -1;

        public static ChoicePanel Make(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<ChoicePanel>();
        }

        /// <summary>Build it with these words, every option unchosen. <paramref name="onPick"/> gets the option's index.</summary>
        public void Build(string kicker, string prompt, IReadOnlyList<string> options, string footer, float viewingDistance,
                          Action<int, Pointer> onPick, string action = null, Action onAction = null)
        {
            Clear();
            _canvas = MuseUi.Canvas(transform, "Choice", viewingDistance, WidthPx);
            var card = MuseUi.Card(_canvas, MuseTheme.Paper, MuseTheme.PanelRadius, MuseTheme.Line, 1f, padX: 16f, padY: 14f, gap: 9f, name: "Card");
            if (!string.IsNullOrWhiteSpace(kicker))
                MuseUi.Text(card, kicker.ToUpperInvariant(), MuseUi.Face.Sans, KickerPx, MuseTheme.Ink3, 0.14f, name: "Kicker").alignment = TextAlignmentOptions.Center;
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                // Its own reserved height: laid out before the serif's metrics settled, the first chip sat over it.
                var p = MuseUi.Text(card, prompt, MuseUi.Face.Serif, PromptPx, MuseTheme.Ink, name: "Prompt");
                _prompt = p; p.alignment = TextAlignmentOptions.Center;
                var pl = p.gameObject.AddComponent<LayoutElement>(); pl.minHeight = PromptPx * 1.5f;
            }
            for (var i = 0; i < options.Count; i++)
            {
                // Centred words, no number column (Saul, 5 Oct: "the text is not centered").
                var chip = MuseUi.Card(card, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Gold, 1f, padX: 12f, padY: 8f, gap: 0f, name: "Option " + i);
                var l = MuseUi.Text(chip, options[i], MuseUi.Face.Sans, OptionPx, MuseTheme.Ink, name: "Label");
                l.enableAutoSizing = false; l.alignment = TextAlignmentOptions.Center;
                _fills.Add(chip.GetComponent<Image>());
                var edge = chip.Find("Edge"); _edges.Add(edge != null ? edge.GetComponent<Image>() : null);
                _labels.Add(l); _numbers.Add(null); _words.Add(options[i]);
            }
            RectTransform actionChip = null;
            if (!string.IsNullOrEmpty(action))
            {
                // A real button for the last step, built now and dimmed until there is a choice, so the panel
                // never changes size (Saul, 5 Oct: "I can't click on the confirm button to keep its moment").
                actionChip = MuseUi.Card(card, MuseTheme.GlyphBack, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 12f, padY: 8f, gap: 0f, name: "Action");
                _actionLabel = MuseUi.Text(actionChip, action, MuseUi.Face.Sans, OptionPx, MuseTheme.Ink3, name: "Label");
                _actionLabel.alignment = TextAlignmentOptions.Center; _actionLabel.fontStyle = FontStyles.Bold;
                _actionFill = actionChip.GetComponent<Image>();
            }
            _footer = MuseUi.Text(card, footer ?? "", MuseUi.Face.Sans, FooterPx, MuseTheme.Ink3, 0.06f, name: "Footer");
            _footer.alignment = TextAlignmentOptions.Center;

            // Hit boxes from the laid-out chips, as the masters' reply panel does.
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            for (var i = 0; i < _fills.Count; i++)
            {
                _fills[i].rectTransform.GetWorldCorners(corners);
                var hit = new GameObject("Hit " + i).transform;
                hit.SetParent(transform, false);
                var lo = transform.InverseTransformPoint(corners[0]); var hi = transform.InverseTransformPoint(corners[2]);
                var box = hit.gameObject.AddComponent<BoxCollider>();
                box.center = (lo + hi) * 0.5f; box.size = new Vector3(Mathf.Abs(hi.x - lo.x), Mathf.Abs(hi.y - lo.y), 0.02f);
                var p = Pointable.Make(hit.gameObject, name + " " + i);
                HoverTint.Bind(p, _fills[i]);
                var index = i;
                p.Selected += (_, pointer) => onPick?.Invoke(index, pointer);
                _options.Add(p);
            }
            if (actionChip != null)
            {
                actionChip.GetWorldCorners(corners);
                var hit = new GameObject("Hit action").transform;
                hit.SetParent(transform, false);
                var lo = transform.InverseTransformPoint(corners[0]); var hi = transform.InverseTransformPoint(corners[2]);
                var box = hit.gameObject.AddComponent<BoxCollider>();
                box.center = (lo + hi) * 0.5f; box.size = new Vector3(Mathf.Abs(hi.x - lo.x), Mathf.Abs(hi.y - lo.y), 0.02f);
                _action = Pointable.Make(hit.gameObject, name + " action");
                HoverTint.Bind(_action, _actionFill);
                _action.Selected += (_, __) => onAction?.Invoke();
                _action.Interactive = false;
            }
            Chosen = -1;
        }

        /// <summary>
        /// Mark option <paramref name="index"/> chosen (-1: none): its chip fills with her soft gold and its number
        /// becomes a tick; the others quieten. Colour only - no size, weight or position changes.
        /// </summary>
        public void Mark(int index, string footer = null)
        {
            Chosen = index;
            for (var i = 0; i < _fills.Count; i++)
            {
                var on = i == index;
                var quiet = index >= 0 && !on;
                if (_fills[i] != null) _fills[i].color = on ? MuseTheme.GoldSoft : MuseTheme.Paper;
                if (_edges[i] != null) _edges[i].color = on ? MuseTheme.Gold : quiet ? MuseTheme.Line : MuseTheme.Gold;
                _labels[i].color = on ? MuseTheme.GoldInk : quiet ? MuseTheme.Ink3 : MuseTheme.Ink;
                _labels[i].text = on ? "✓  " + _words[i] : _words[i];
            }
            if (_action != null)
            {
                var ready = index >= 0;
                _action.Interactive = ready;
                if (_actionFill != null) _actionFill.color = ready ? MuseTheme.Gold : MuseTheme.GlyphBack;
                if (_actionLabel != null) _actionLabel.color = ready ? Color.white : MuseTheme.Ink3;
            }
            if (footer != null && _footer != null) _footer.text = footer;
        }

        public void SetFooter(string footer) { if (_footer != null) _footer.text = footer ?? ""; }
        public void SetPrompt(string prompt) { if (_prompt != null) _prompt.text = prompt ?? ""; }

        /// <summary>An option already looked at (Monet's paintings heard): a gold dot for its number. Colour only.</summary>
        public void MarkHeard(int index)
        {
            if (index < 0 || index >= _labels.Count || index == Chosen) return;
            _labels[index].text = "•  " + _words[index];   // heard: a gold dot before its words
            if (_edges[index] != null) _edges[index].color = MuseTheme.GoldInk;
        }

        void Clear()
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            _options.Clear(); _fills.Clear(); _edges.Clear(); _labels.Clear(); _numbers.Clear(); _words.Clear();
            _action = null; _actionFill = null; _actionLabel = null;
            _footer = null; _prompt = null; _canvas = null; Chosen = -1;
        }
    }
}
