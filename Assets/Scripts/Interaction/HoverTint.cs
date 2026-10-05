using UnityEngine;
using UnityEngine.UI;

namespace MuseXR.Interaction
{
    /// <summary>
    /// A button's hover (Saul, 5 Oct: "the UI with buttons should also have hover effects"): while the ray is on its
    /// hit box the chip warms toward gold and grows a touch, and goes back when the ray leaves. If something else
    /// recolours the chip meanwhile (a choice kept), that colour is left alone.
    /// </summary>
    public static class HoverTint
    {
        static readonly Color Gold = new Color(0.86f, 0.68f, 0.32f);
        const float Mix = 0.4f, Grow = 1.04f;

        /// <summary>A uGUI chip (a MuseUi card).</summary>
        public static void Bind(Pointable p, Graphic chip)
        {
            if (p == null || chip == null) return;
            Color before = default, lit = default; Vector3 size = Vector3.one;
            p.Hovering += _ =>
            {
                if (chip == null) return;
                before = chip.color; size = chip.transform.localScale;
                lit = Color.Lerp(before, Gold, Mix); lit.a = Mathf.Max(before.a, 0.95f);
                chip.color = lit; chip.transform.localScale = size * Grow;
            };
            p.Unhovered += _ =>
            {
                if (chip == null) return;
                if (chip.color == lit) chip.color = before;
                chip.transform.localScale = size;
            };
        }

        public static void Bind(Pointable p, RectTransform chip) => Bind(p, chip != null ? chip.GetComponent<Graphic>() : null);

        /// <summary>A chip drawn as a mesh (a quad with an Unlit material).</summary>
        public static void Bind(Pointable p, Renderer chip, Transform grow = null)
        {
            if (p == null || chip == null) return;
            grow = grow != null ? grow : chip.transform;
            Color before = default, lit = default; Vector3 size = Vector3.one;
            p.Hovering += _ =>
            {
                if (chip == null) return;
                var m = chip.material;
                before = m.GetColor("_BaseColor"); size = grow.localScale;
                lit = Color.Lerp(before, Gold, Mix); lit.a = before.a;
                m.SetColor("_BaseColor", lit); grow.localScale = size * Grow;
            };
            p.Unhovered += _ =>
            {
                if (chip == null) return;
                var m = chip.material;
                if (m.GetColor("_BaseColor") == lit) m.SetColor("_BaseColor", before);
                grow.localScale = size;
            };
        }
    }
}
