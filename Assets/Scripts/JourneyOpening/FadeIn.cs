using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MuseXR.Journey
{
    /// <summary>
    /// Brings something into the world gently instead of popping it in (Saul, 4 Oct: the Gate's paintings
    /// appear once the companions are chosen). URP Lit / Unlit materials fade their alpha through a
    /// transparent copy and get their originals back at the end; text fades; anything drawn by another
    /// shader (the glTF statue) rises a little into place instead.
    /// </summary>
    public sealed class FadeIn : MonoBehaviour
    {
        public float seconds = 1.4f;

        readonly List<(Renderer r, Material[] originals, Material[] fades, float[] alpha)> _faded = new List<(Renderer, Material[], Material[], float[])>();
        readonly List<(TMP_Text t, float alpha)> _texts = new List<(TMP_Text, float)>();
        readonly List<(Transform t, Vector3 pos, Vector3 scale)> _risen = new List<(Transform, Vector3, Vector3)>();
        float _t;

        /// <summary>Show <paramref name="go"/> and fade it in.</summary>
        public static void Reveal(GameObject go, float seconds = 1.4f)
        {
            if (go == null) return;
            go.SetActive(true);
            var f = go.GetComponent<FadeIn>();
            if (f == null) f = go.AddComponent<FadeIn>();
            f.seconds = seconds;
        }

        void Start()
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                var originals = r.sharedMaterials;
                var fades = new Material[originals.Length];
                var alpha = new float[originals.Length];
                var all = true;
                for (var i = 0; i < originals.Length; i++)
                {
                    var m = originals[i];
                    if (m == null || m.shader == null || !m.shader.name.StartsWith("Universal Render Pipeline/") || !m.HasProperty("_BaseColor")) { all = false; break; }
                    var f = new Material(m);
                    f.SetFloat("_Surface", 1f); f.SetFloat("_Blend", 0f);
                    f.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    f.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    f.SetFloat("_ZWrite", 0f); f.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");   // the keyword, not just the floats
                    f.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    alpha[i] = m.GetColor("_BaseColor").a;
                    fades[i] = f;
                }
                if (!all) { _risen.Add((r.transform, r.transform.localPosition, r.transform.localScale)); continue; }
                r.sharedMaterials = fades;
                _faded.Add((r, originals, fades, alpha));
            }
            foreach (var t in GetComponentsInChildren<TMP_Text>()) _texts.Add((t, t.alpha));
            Apply(0f);
        }

        void Update()
        {
            _t += Time.deltaTime / Mathf.Max(0.05f, seconds);
            var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));
            Apply(k);
            if (_t < 1f) return;
            foreach (var f in _faded) if (f.r != null) { f.r.sharedMaterials = f.originals; foreach (var m in f.fades) Destroy(m); }
            Destroy(this);
        }

        void Apply(float k)
        {
            foreach (var f in _faded)
                for (var i = 0; i < f.fades.Length; i++)
                {
                    var c = f.fades[i].GetColor("_BaseColor"); c.a = f.alpha[i] * k; f.fades[i].SetColor("_BaseColor", c);
                }
            foreach (var t in _texts) if (t.t != null) t.t.alpha = t.alpha * k;
            foreach (var r in _risen)
                if (r.t != null)
                {
                    r.t.localPosition = r.pos - Vector3.up * (0.25f * (1f - k));
                    r.t.localScale = r.scale * Mathf.Lerp(0.9f, 1f, k);
                }
        }
    }
}
