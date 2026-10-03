using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MuseXR.UI
{
    /// <summary>
    /// Builds her interface components out of uGUI, in her pixel units, laid out by layout groups so
    /// every panel sizes to its content the way her HTML does. A panel is a world-space canvas scaled
    /// by <see cref="PanelScale"/> for the distance it is read from - never head-locked.
    /// </summary>
    public static class MuseUi
    {
        public enum Face { Serif, SerifMedium, SerifItalic, Sans, SansSemi, SansBold, Mono }

        // ---- canvas -------------------------------------------------------------------------------

        /// <summary>A world-space canvas for one panel, scaled so her 14px body text is >= 1 degree
        /// from <paramref name="viewingDistance"/> metres. +Z of <paramref name="parent"/> = away from the viewer.</summary>
        public static RectTransform Canvas(Transform parent, string name, float viewingDistance, float widthPx)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<UnityEngine.Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;   // crisp TMP in world space
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(widthPx, 10f);
            rt.localScale = Vector3.one * PanelScale.MetresPerPixel(viewingDistance);
            AddRaycaster(go);
            var fit = go.AddComponent<VerticalLayoutGroup>();
            fit.childControlWidth = true; fit.childControlHeight = true; fit.childForceExpandHeight = false;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        static void AddRaycaster(GameObject go)
        {
            // XRI's world-space UI raycaster, if present, so a controller ray can press the pills.
            var t = Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            if (t != null) go.AddComponent(t); else go.AddComponent<GraphicRaycaster>();
        }

        // ---- containers ---------------------------------------------------------------------------

        /// <summary>Her glass panel (.gp): milk glass, white edge, 22px radius, soft shadow.</summary>
        public static RectTransform Glass(Transform parent, float widthPx, float padX = 20f, float padY = 18f, float gap = 6f)
        {
            var rt = Box(parent, "Glass", MuseTheme.Glass, MuseTheme.PanelRadius, widthPx, padX, padY, gap);
            Sheen(rt, MuseTheme.PanelRadius);
            Edge(rt, MuseTheme.GlassEdge, MuseTheme.PanelRadius, 1f);
            Shadow(rt, MuseTheme.PanelRadius, 34f, new Vector2(0f, -18f));
            return rt;
        }

        /// <summary>A solid card (her .opt, .answer, .memento): fill, optional edge.</summary>
        public static RectTransform Card(Transform parent, Color fill, float radius, Color? edge = null, float edgePx = 1f,
                                         float widthPx = -1f, float padX = 12f, float padY = 10f, float gap = 4f, string name = "Card")
        {
            var rt = Box(parent, name, fill, radius, widthPx, padX, padY, gap);
            if (edge.HasValue) Edge(rt, edge.Value, radius, edgePx);
            return rt;
        }

        static RectTransform Box(Transform parent, string name, Color fill, float radius, float widthPx, float padX, float padY, float gap)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiSprites.Rounded(radius); img.type = Image.Type.Sliced; img.color = fill;
            img.pixelsPerUnitMultiplier = 1f;
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(Mathf.RoundToInt(padX), Mathf.RoundToInt(padX), Mathf.RoundToInt(padY), Mathf.RoundToInt(padY));
            v.spacing = gap; v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            if (widthPx > 0f) go.AddComponent<LayoutElement>().preferredWidth = widthPx;
            return (RectTransform)go.transform;
        }

        static void Edge(RectTransform box, Color colour, float radius, float px)
        {
            var go = new GameObject("Edge", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(box, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            Stretch((RectTransform)go.transform, 0f);
            var img = go.GetComponent<Image>();
            img.sprite = UiSprites.Outline(radius, px); img.type = Image.Type.Sliced; img.color = colour;
            img.raycastTarget = false;
        }

        /// <summary>
        /// Her glass is a 22px backdrop blur with saturate(1.4). A real blur behind every panel is too
        /// dear over splats in a headset, so the frost is faked: a soft white light from the top-left
        /// fading to a warm tint at the right, as her mockups read.
        /// </summary>
        static void Sheen(RectTransform box, float radius)
        {
            var go = new GameObject("Sheen", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(box, false);
            go.transform.SetAsFirstSibling();
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            Stretch((RectTransform)go.transform, 0f);
            var img = go.GetComponent<Image>();
            img.sprite = UiSprites.Sheen(radius); img.type = Image.Type.Simple; img.color = Color.white;
            img.raycastTarget = false;
        }

        static void Shadow(RectTransform box, float radius, float blur, Vector2 offset)
        {
            var go = new GameObject("Shadow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(box, false);
            go.transform.SetAsFirstSibling();
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            Stretch(rt, -blur);
            rt.anchoredPosition = offset;
            var img = go.GetComponent<Image>();
            img.sprite = UiSprites.Shadow(radius, blur); img.type = Image.Type.Sliced; img.color = MuseTheme.Shadow;
            img.raycastTarget = false;
            // Behind the panel: a canvas-level child would draw over its parent, so push it back.
            var c = go.AddComponent<UnityEngine.Canvas>(); c.overrideSorting = true; c.sortingOrder = 5;
        }

        public static RectTransform Row(Transform parent, float gap = 8f, TextAnchor align = TextAnchor.MiddleLeft, string name = "Row")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = gap; h.childAlignment = align;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            return (RectTransform)go.transform;
        }

        public static RectTransform Column(Transform parent, float gap = 2f, string name = "Column")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.spacing = gap; v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return (RectTransform)go.transform;
        }

        /// <summary>Vertical breathing room (her margins).</summary>
        public static void Space(Transform parent, float px)
        {
            var go = new GameObject("Space", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = px;
        }

        /// <summary>A 1px rule (her border-top: 1px solid var(--line)).</summary>
        public static void Rule(Transform parent, Color colour)
        {
            var go = new GameObject("Rule", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = colour;
            var le = go.GetComponent<LayoutElement>(); le.minHeight = 1f; le.preferredHeight = 1f;
        }

        // ---- text ---------------------------------------------------------------------------------

        /// <summary>Her sizes are CSS px; TMP sets the same fonts ~13% larger at the same number, which
        /// made every panel wrap earlier than her render (a blind diff against ui.png, all five
        /// panels). Layout px are unchanged; only type is brought to her size.</summary>
        public const float PxToTmp = 0.87f;

        public static TextMeshProUGUI Text(Transform parent, string text, Face face, float px, Color colour,
                                           float letterSpacingEm = 0f, bool upper = false, float lineHeight = 1.4f, string name = "Text")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            var f = MuseFonts.Get();
            if (f != null)
            {
                var fa = face switch
                {
                    Face.Serif => f.serif, Face.SerifMedium => f.serifMedium != null ? f.serifMedium : f.serif,
                    Face.SerifItalic => f.serifItalic, Face.SansSemi => f.sansSemi,
                    Face.SansBold => f.sansBold, Face.Mono => f.mono, _ => f.sans,
                };
                if (fa != null) t.font = fa;
            }
            t.text = text;
            t.fontSize = px * PxToTmp;
            t.color = colour;
            t.characterSpacing = letterSpacingEm * 100f;
            if (upper) t.fontStyle |= FontStyles.UpperCase;
            t.lineSpacing = (lineHeight - 1.2f) * 100f;   // TMP's default line is ~1.2 em
            t.enableWordWrapping = true;
            t.raycastTarget = false;
            t.margin = Vector4.zero;
            return t;
        }

        /// <summary>Her small tracked caps label (.gp .k / .eyebrow).</summary>
        public static TextMeshProUGUI Kicker(Transform parent, string text, Color? colour = null, bool upper = true) =>
            Text(parent, text, Face.SansSemi, MuseTheme.KickerPx, colour ?? MuseTheme.Ink3, upper ? 0.12f : 0.04f, upper: upper, name: "Kicker");

        /// <summary>Her serif heading (.gp h4).</summary>
        public static TextMeshProUGUI Title(Transform parent, string text, float px = MuseTheme.TitlePx) =>
            Text(parent, text, Face.Serif, px, MuseTheme.Ink, lineHeight: 1.15f, name: "Title");

        public static TextMeshProUGUI Body(Transform parent, string text, Color? colour = null, float px = MuseTheme.BodyPx) =>
            Text(parent, text, Face.Sans, px, colour ?? MuseTheme.Ink2, name: "Body");

        // ---- small parts --------------------------------------------------------------------------

        /// <summary>Her pill button (.btn / .btn.pri) with its controller-letter badge (.gl).</summary>
        public static Button Pill(Transform parent, string glyph, string label, bool primary, UnityAction onClick = null)
        {
            var row = Row(parent, 8f, TextAnchor.MiddleCenter, "Pill " + glyph);
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(10, 14, 7, 7);
            var img = row.gameObject.AddComponent<Image>();
            img.sprite = UiSprites.Rounded(16f); img.type = Image.Type.Sliced;
            img.color = primary ? MuseTheme.Ink : MuseTheme.Paper;
            if (!primary) Edge(row, MuseTheme.Line, 16f, 1f);
            Glyph(row, glyph, primary);
            Text(row, label, Face.SansSemi, MuseTheme.ButtonPx, primary ? Color.white : MuseTheme.Ink, name: "Label").enableWordWrapping = false;
            row.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0f;   // a pill hugs its label
            var b = row.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        /// <summary>The controller letter in its little rounded square - never colour alone.</summary>
        public static RectTransform Glyph(Transform parent, string glyph, bool onDark)
        {
            var box = Card(parent, onDark ? new Color(1f, 1f, 1f, 0.18f) : MuseTheme.GlyphBack, MuseTheme.GlyphRadius,
                           widthPx: -1f, padX: 4f, padY: 2f, gap: 0f, name: "Glyph");
            // flexibleWidth 0: a layout group with childForceExpand reports itself stretchable, and the
            // badge then soaked up all of its pill's spare width (354px in a stretched Redo pill).
            var le = box.gameObject.AddComponent<LayoutElement>(); le.minWidth = 20f; le.minHeight = 20f; le.flexibleWidth = 0f;
            var t = Text(box, glyph, Face.Mono, MuseTheme.GlyphPx, onDark ? Color.white : MuseTheme.Ink2, name: "Letter");
            t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false;
            return box;
        }

        /// <summary>Her avatar disc (.av) with the master's initial; a speaking ring when <paramref name="speaking"/>.</summary>
        public static RectTransform Avatar(Transform parent, string masterId, bool speaking = false, float px = MuseTheme.AvatarPx)
        {
            var go = new GameObject("Avatar " + masterId, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>(); le.minWidth = le.preferredWidth = px; le.minHeight = le.preferredHeight = px;
            var rt = (RectTransform)go.transform;
            var colour = MuseTheme.Master(masterId);
            if (speaking)
            {
                Disc(rt, colour, px + 10f);
                Disc(rt, Color.white, px + 6f);
            }
            Disc(rt, colour, px);
            var t = Text(rt, MuseTheme.Initial(masterId), Face.SansBold, 13f, Color.white, name: "Initial");
            t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false;
            var trt = t.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            return rt;
        }

        static void Disc(RectTransform parent, Color colour, float size)
        {
            var go = new GameObject("Disc", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>(); img.sprite = UiSprites.Disc(); img.color = colour; img.raycastTarget = false;
        }

        /// <summary>Her small tag (.tag): the purple AI label by default.</summary>
        public static RectTransform Tag(Transform parent, string text, Color? back = null, Color? ink = null)
        {
            var box = Card(parent, back ?? MuseTheme.AiTagBack, 5f, widthPx: -1f, padX: 7f, padY: 2f, gap: 0f, name: "Tag");
            box.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0f;   // a tag hugs its word
            var t = Text(box, text, Face.SansSemi, 10.5f, ink ?? MuseTheme.AiTagInk, 0.06f, name: "TagText");
            t.enableWordWrapping = false;
            return box;
        }

        /// <summary>A ring at an exact size, for her slot states (.slot .ring).</summary>
        public static Image Ring(Transform parent, Sprite sprite, Color colour, float sizePx, string name = "Ring")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>(); le.minWidth = le.preferredWidth = sizePx; le.minHeight = le.preferredHeight = sizePx;
            var img = go.GetComponent<Image>(); img.sprite = sprite; img.color = colour; img.raycastTarget = false; img.preserveAspect = true;
            return img;
        }

        public static void Stretch(RectTransform rt, float outset)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(outset, outset); rt.offsetMax = new Vector2(-outset, -outset);
        }
    }
}
