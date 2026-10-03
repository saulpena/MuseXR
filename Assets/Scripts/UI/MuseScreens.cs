using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MuseXR.UI
{
    /// <summary>Her 4.1 artwork card's fields, straight from sceneCollections.js.</summary>
    public sealed class ArtworkInfo
    {
        public string Title = "", Artist = "", Date = "", Source = "", Rights = "", SourceUrl = "";
        /// <summary>Her rule: works dated "AI interpretive study" carry the purple AI tag.</summary>
        public bool AiStudy;
    }

    /// <summary>One line of her 4.2 companion dialogue.</summary>
    public sealed class DialogueLine
    {
        public string SpeakerId = "", SpeakerName = "", Line = "";
        public string NextSpeakerId, NextSpeakerName;
        public IReadOnlyList<string> Replies = new string[0];   // her artworkChoices, up to three
    }

    /// <summary>One roundtable turn (4.4): a line that must cite a real record.</summary>
    public sealed class RoundtableTurn { public string SpeakerId = "", SpeakerName = "", Line = "", BasedOn = ""; }

    /// <summary>Her memento card (4.5).</summary>
    public sealed class Memento
    {
        public string WorldTitle = "", Question = "", Answer = "";
        public IReadOnlyList<MementoChoice> Choices = new MementoChoice[0];
        public IReadOnlyList<string> Companions = new string[0];
        public Texture2D Header;
        public bool Sample;
    }

    /// <summary>One line of the memento: her chapter letter (A-D) and what was kept there.</summary>
    public sealed class MementoChoice
    {
        public string Letter, Text;
        public MementoChoice(string letter, string text) { Letter = letter; Text = text; }
    }

    public enum SlotVisual { Empty, Aligned, Placed }

    /// <summary>
    /// Skylar's five interface screens (her plan, section 04), built from <see cref="MuseUi"/>.
    /// Each returns the canvas so the caller places it in the world (never head-locked).
    /// Wording is hers, from the mockups.
    /// </summary>
    public static class MuseScreens
    {
        /// <summary>The disclaimer her dialogue carries on every line (DIALOGUE_DISCLAIMER, kept verbatim).</summary>
        public const string Disclaimer = "AI interpretation grounded in documented themes — not an authentic quotation";

        // ---- 4.1 Artwork info ---------------------------------------------------------------------

        public static RectTransform ArtworkCard(Transform parent, ArtworkInfo a, float viewingDistance,
                                                UnityAction hearCompanions = null, UnityAction close = null)
        {
            var canvas = MuseUi.Canvas(parent, "Artwork Card", viewingDistance, 440f);
            var p = MuseUi.Glass(canvas, 440f);
            var head = MuseUi.Row(p, 8f);
            MuseUi.Kicker(head, "Artwork card · appears on point");
            if (a.AiStudy) MuseUi.Tag(head, "AI");
            MuseUi.Title(p, a.Title);
            MuseUi.Body(p, string.IsNullOrEmpty(a.Date) ? a.Artist : a.Artist + " · " + a.Date, MuseTheme.Ink2, 14f);
            MuseUi.Space(p, 2f);
            MuseUi.Text(p, a.Source + " · " + a.Rights, MuseUi.Face.Sans, 12f, MuseTheme.Ink3, name: "Source");
            if (!string.IsNullOrEmpty(a.SourceUrl)) MuseUi.Text(p, a.SourceUrl, MuseUi.Face.Mono, 12f, MuseTheme.Ink3, name: "Url");
            MuseUi.Space(p, 6f);
            var row = MuseUi.Row(p, 8f);
            MuseUi.Pill(row, "A", "Hear companions", true, hearCompanions);
            MuseUi.Pill(row, "B", "Close", false, close);
            return canvas;
        }

        // ---- 4.2 Companion dialogue ---------------------------------------------------------------

        public static RectTransform Dialogue(Transform parent, DialogueLine d, float viewingDistance,
                                             UnityAction next = null, UnityAction<int> reply = null)
        {
            var canvas = MuseUi.Canvas(parent, "Companion Dialogue", viewingDistance, 440f);
            var p = MuseUi.Glass(canvas, 440f, gap: 8f);

            var who = MuseUi.Row(p, 10f);
            MuseUi.Avatar(who, d.SpeakerId, speaking: true);
            var names = MuseUi.Column(who, 0f);
            MuseUi.Kicker(names, "Speaking", MuseTheme.Master(d.SpeakerId));
            MuseUi.Text(names, d.SpeakerName, MuseUi.Face.SansBold, 14f, MuseTheme.Ink, name: "Name");

            MuseUi.Text(p, "“" + d.Line + "”", MuseUi.Face.SerifMedium, MuseTheme.QuotePx, MuseTheme.Ink, lineHeight: 1.3f, name: "Quote");
            MuseUi.Text(p, Disclaimer, MuseUi.Face.SansSemi, 10f, MuseTheme.Ink3, 0.04f, upper: true, name: "Disclaimer");

            var nav = MuseUi.Row(p, 6f);
            if (!string.IsNullOrEmpty(d.NextSpeakerId))
            {
                MuseUi.Text(nav, "Next", MuseUi.Face.Sans, 12f, MuseTheme.Ink3, name: "NextLabel").enableWordWrapping = false;
                MuseUi.Avatar(nav, d.NextSpeakerId, px: 20f);
                MuseUi.Text(nav, d.NextSpeakerName, MuseUi.Face.Sans, 12f, MuseTheme.Ink2, name: "NextName").enableWordWrapping = false;
            }
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(LayoutElement));
            fill.transform.SetParent(nav, false); fill.GetComponent<LayoutElement>().flexibleWidth = 1f;
            MuseUi.Pill(nav, "A", "Next", false, next);

            for (var i = 0; i < d.Replies.Count && i < 3; i++)
            {
                var opt = MuseUi.Card(p, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 12f, padY: 10f, name: "Reply " + (i + 1));
                // Her .opt small: the number in mono, then the reply.
                var line = MuseUi.Row(opt, 8f, TextAnchor.UpperLeft, "ReplyRow");
                var num = MuseUi.Text(line, "0" + (i + 1), MuseUi.Face.Mono, 11f, MuseTheme.Ink3, name: "Number");
                num.enableWordWrapping = false; num.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0f;
                var txt = MuseUi.Text(line, d.Replies[i], MuseUi.Face.Sans, 14f, MuseTheme.Ink, name: "ReplyText");
                txt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var b = opt.gameObject.AddComponent<Button>();
                int idx = i;
                if (reply != null) b.onClick.AddListener(() => reply(idx));
            }
            return canvas;
        }

        // ---- 4.3 Choice prompts and slots -----------------------------------------------------------

        /// <summary>One slot card in her three states: shape, word and hint change together, never colour alone.</summary>
        public static RectTransform Slot(Transform parent, SlotVisual look, float widthPx = 140f)
        {
            var card = MuseUi.Card(parent, new Color(1f, 253f / 255f, 249f / 255f, 0.86f), MuseTheme.CardRadius, MuseTheme.GlassEdge, 1f,
                                   widthPx, 6f, 12f, 4f, "Slot " + look);
            card.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            var ringRow = MuseUi.Row(card, 0f, TextAnchor.MiddleCenter, "RingRow");
            var holder = new GameObject("RingHolder", typeof(RectTransform), typeof(LayoutElement));
            holder.transform.SetParent(ringRow, false);
            var le = holder.GetComponent<LayoutElement>(); le.minWidth = le.preferredWidth = 62f; le.minHeight = le.preferredHeight = 62f;
            var h = (RectTransform)holder.transform;

            string word, title, hint;
            switch (look)
            {
                case SlotVisual.Aligned:
                    Layer(h, UiSprites.Disc(), MuseTheme.RoseSoft, 74f);                 // her 6px halo
                    Layer(h, UiSprites.Disc(), Color.white, 62f);
                    Layer(h, UiSprites.Ring(0.1f), MuseTheme.Rose, 62f);
                    word = "Release"; title = "Aligned"; hint = "Snap preview + light haptic";
                    Label(h, word, MuseTheme.Rose);
                    break;
                case SlotVisual.Placed:
                    Layer(h, UiSprites.Disc(), MuseTheme.GoldSoft, 62f);
                    Layer(h, UiSprites.Ring(0.1f), MuseTheme.Gold, 62f);
                    word = "Saved"; title = "Placed"; hint = "Undo 3s · B";
                    Label(h, word, MuseTheme.GoldInk);
                    break;
                default:
                    Layer(h, UiSprites.Ring(0.04f, 18), MuseTheme.Ink3, 62f);
                    word = "Empty"; title = "Waiting"; hint = "Hold Grip, place here";
                    Label(h, word, MuseTheme.Ink3);
                    break;
            }
            var t = MuseUi.Text(card, title, MuseUi.Face.SansSemi, 12.5f, look == SlotVisual.Aligned ? MuseTheme.Rose : MuseTheme.Ink, name: "State");
            t.alignment = TextAlignmentOptions.Center;
            var s = MuseUi.Text(card, hint, MuseUi.Face.Sans, 11.5f, MuseTheme.Ink3, name: "Hint");
            s.alignment = TextAlignmentOptions.Center;
            return card;
        }

        static void Layer(RectTransform parent, Sprite sprite, Color colour, float size)
        {
            var go = new GameObject("Layer", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>(); img.sprite = sprite; img.color = colour; img.raycastTarget = false;
        }

        static void Label(RectTransform parent, string word, Color colour)
        {
            var t = MuseUi.Text(parent, word, MuseUi.Face.SansSemi, 11f, colour, name: "Word");
            t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false;
            var rt = t.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>Her three slot states side by side, as in her 4.3 mockup.</summary>
        public static RectTransform SlotStates(Transform parent, float viewingDistance, SlotVisual highlight = SlotVisual.Empty)
        {
            var canvas = MuseUi.Canvas(parent, "Slot States", viewingDistance, 450f);
            var row = MuseUi.Row(canvas, 10f, TextAnchor.UpperCenter);
            Slot(row, SlotVisual.Empty); Slot(row, SlotVisual.Aligned); Slot(row, SlotVisual.Placed);
            return canvas;
        }

        /// <summary>Her confirm strip: "Keep this moment? Dusk · Water Lilies", A Confirm, B Redo.</summary>
        public static RectTransform ConfirmStrip(Transform parent, string detail, float viewingDistance,
                                                 UnityAction confirm = null, UnityAction redo = null, float undoFraction = -1f)
        {
            var canvas = MuseUi.Canvas(parent, "Confirm Strip", viewingDistance, 450f);
            var p = MuseUi.Glass(canvas, 450f, 16f, 14f, 10f);
            var top = MuseUi.Row(p, 8f);
            var dot = new GameObject("Dot", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            dot.transform.SetParent(top, false);
            var dle = dot.GetComponent<LayoutElement>(); dle.minWidth = dle.preferredWidth = 8f; dle.minHeight = dle.preferredHeight = 8f;
            var di = dot.GetComponent<Image>(); di.sprite = UiSprites.Disc(); di.color = MuseTheme.Gold;
            var q = MuseUi.Text(top, "Keep this moment?", MuseUi.Face.SansSemi, 13.5f, MuseTheme.Ink, name: "Question");
            q.enableWordWrapping = false;
            MuseUi.Text(top, detail, MuseUi.Face.Sans, 13.5f, MuseTheme.Ink2, name: "Detail").gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            
            MuseUi.Pill(top, "A", "Confirm", true, confirm);
            var bottom = MuseUi.Row(p, 8f);
            MuseUi.Pill(bottom, "B", "Redo", false, redo);
            if (undoFraction >= 0f)
                MuseUi.Text(bottom, "Undo " + Mathf.CeilToInt(3f * undoFraction) + "s", MuseUi.Face.Sans, 11.5f, MuseTheme.Ink3, name: "Undo");
            return canvas;
        }

        // ---- 4.4 Closing roundtable ----------------------------------------------------------------

        public static RectTransform Roundtable(Transform parent, IReadOnlyList<RoundtableTurn> turns, string draft, float viewingDistance,
                                               UnityAction keep = null, UnityAction rewrite = null, UnityAction sayOwn = null,
                                               string rewriteVia = "Socrates", bool localFallback = false)
        {
            var canvas = MuseUi.Canvas(parent, "Roundtable", viewingDistance, 450f);
            var col = MuseUi.Column(canvas, 10f);
            foreach (var t in turns)
            {
                var p = MuseUi.Glass(col, 450f, 14f, 12f, 3f);
                var row = MuseUi.Row(p, 10f, TextAnchor.UpperLeft);
                MuseUi.Avatar(row, t.SpeakerId);
                var c = MuseUi.Column(row, 2f);
                var nameRow = MuseUi.Row(c, 6f);
                MuseUi.Text(nameRow, t.SpeakerName, MuseUi.Face.SansBold, 13.5f, MuseTheme.Ink, name: "Name").enableWordWrapping = false;
                MuseUi.Tag(nameRow, "AI");
                MuseUi.Text(c, "“" + t.Line + "”", MuseUi.Face.Sans, 13.5f, MuseTheme.Ink, name: "Line");
                MuseUi.Text(c, "Based on: " + t.BasedOn, MuseUi.Face.Sans, 11f, MuseTheme.Ink3, name: "BasedOn");
            }
            var a = MuseUi.Card(col, new Color(1f, 253f / 255f, 249f / 255f, 0.94f), 20f, MuseTheme.Gold, 1.5f, padX: 18f, padY: 16f, gap: 6f, name: "Answer");
            MuseUi.Kicker(a, localFallback ? "Your answer · draft · local fallback" : "Your answer · draft", MuseTheme.Gold);
            MuseUi.Text(a, draft, MuseUi.Face.SerifMedium, MuseTheme.QuotePx, MuseTheme.Ink, lineHeight: 1.35f, name: "Draft");
            var row2 = MuseUi.Row(a, 8f);
            MuseUi.Pill(row2, "A", "Keep", true, keep);
            MuseUi.Pill(row2, "X", "Rewrite via " + rewriteVia, false, rewrite);
            MuseUi.Pill(row2, "Y", "Say my own", false, sayOwn);
            return canvas;
        }

        // ---- 4.5 Personal ending -------------------------------------------------------------------

        public static RectTransform MementoCard(Transform parent, Memento m, float viewingDistance,
                                                UnityAction save = null, UnityAction startAgain = null)
        {
            var canvas = MuseUi.Canvas(parent, "Memento", viewingDistance, 300f);
            var card = MuseUi.Card(canvas, MuseTheme.MementoPaper, 16f, padX: 0f, padY: 0f, gap: 0f, widthPx: 300f, name: "Memento");
            if (m.Header != null)
            {
                var img = new GameObject("Header", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement));
                img.transform.SetParent(card, false);
                img.GetComponent<LayoutElement>().minHeight = 120f;
                var ri = img.GetComponent<RawImage>(); ri.texture = m.Header;
                float aspect = m.Header.width / (float)m.Header.height, want = 300f / 120f;
                ri.uvRect = aspect > want ? new Rect((1f - want / aspect) / 2f, 0f, want / aspect, 1f) : new Rect(0f, (1f - aspect / want) / 2f, 1f, aspect / want);
            }
            var body = MuseUi.Column(card, 4f, "Body");
            body.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(16, 16, 14, 14);
            MuseUi.Kicker(body, "MUSE∞ · Memento" + (m.Sample ? " · sample" : ""), MuseTheme.Gold);
            MuseUi.Title(body, m.WorldTitle, 22f);
            MuseUi.Text(body, "“" + m.Question + "”", MuseUi.Face.SerifItalic, 13.5f, MuseTheme.Ink2, name: "Question");
            MuseUi.Space(body, 4f);
            foreach (var c in m.Choices)
                MuseUi.Text(body, "<color=#26221d>" + c.Letter + "</color>   " + c.Text, MuseUi.Face.Sans, 12.5f, MuseTheme.Ink2, name: "Choice " + c.Letter);
            MuseUi.Space(body, 6f); MuseUi.Rule(body, MuseTheme.Line); MuseUi.Space(body, 6f);
            MuseUi.Text(body, "“" + m.Answer + "”", MuseUi.Face.SerifMedium, 17f, MuseTheme.Ink, lineHeight: 1.3f, name: "Answer");
            MuseUi.Space(body, 4f);
            MuseUi.Text(body, "With " + string.Join(" · ", m.Companions) + " (AI interpretations)", MuseUi.Face.Sans, 11f, MuseTheme.Ink3, name: "Credits");
            // In the headset only Save and Start again (her 4.5).
            if (save != null || startAgain != null)
            {
                MuseUi.Space(body, 6f);
                var row = MuseUi.Row(body, 8f);
                MuseUi.Pill(row, "A", "Save", true, save);
                MuseUi.Pill(row, "B", "Start again", false, startAgain);
            }
            return canvas;
        }
    }
}
