using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Nothing pops (Saul, 5 Oct 2026: "having things pop into existence looks very unprofessional"). Every
    /// panel, card, label, prop and figure that comes or goes during the journey eases in or out through this:
    ///
    ///   Appear.In(go)                 switch it on and fade it up
    ///   Appear.Out(go)                fade it down, then switch it off
    ///   Appear.Out(go, destroy: true) fade it down, then destroy it
    ///
    /// What fades: URP Lit / Unlit and glTFast shader-graph materials (through transparent instances, given
    /// back their originals at the end), world-space canvases (a CanvasGroup) and TextMeshPro. Anything else a
    /// renderer draws scales up from slightly smaller instead, so it still eases rather than snapping.
    /// Calling In on something fading Out (or the reverse) turns it round from where it is.
    /// </summary>
    public sealed class Appear : MonoBehaviour
    {
        public const float InSeconds = 0.5f, OutSeconds = 0.35f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int GltfBaseColor = Shader.PropertyToID("baseColorFactor");

        sealed class Faded { public Renderer r; public Material[] originals; public Material[] fades; public int[] prop; public Color[] colour; public bool[] additive; }

        readonly List<Faded> _faded = new List<Faded>();
        readonly List<(CanvasGroup g, float alpha, bool added)> _groups = new List<(CanvasGroup, float, bool)>();
        readonly List<(TMP_Text t, float alpha)> _texts = new List<(TMP_Text, float)>();
        readonly List<(Transform t, Vector3 scale)> _scaled = new List<(Transform, Vector3)>();
        readonly HashSet<Renderer> _seen = new HashSet<Renderer>();
        readonly HashSet<Canvas> _seenCanvas = new HashSet<Canvas>();
        readonly HashSet<TMP_Text> _seenText = new HashSet<TMP_Text>();
        float _k, _target, _seconds;
        bool _destroy, _prepared;

        /// <summary>The fade's current level, 0 hidden to 1 shown.</summary>
        public float Level => _k;
        /// <summary>Fired when an Out has finished (before the object is switched off or destroyed).</summary>
        public event Action<GameObject> Gone;

        public static Appear In(GameObject go, float seconds = InSeconds)
        {
            if (go == null) return null;
            var a = go.GetComponent<Appear>();
            var fresh = a == null;
            if (fresh) a = go.AddComponent<Appear>();
            var wasHidden = !go.activeSelf;
            go.SetActive(true);
            a.Begin(1f, seconds, false, fresh || wasHidden ? 0f : a._k);
            return a;
        }

        public static Appear Out(GameObject go, float seconds = OutSeconds, bool destroy = false)
        {
            if (go == null) return null;
            if (!go.activeInHierarchy) { if (destroy) Destroy(go); else go.SetActive(false); return null; }
            var a = go.GetComponent<Appear>();
            var fresh = a == null;
            if (fresh) a = go.AddComponent<Appear>();
            a.Begin(0f, seconds, destroy, fresh ? 1f : a._k);
            return a;
        }

        /// <summary>
        /// Shown or hidden, eased, for code that states every frame what it wants (a compass that shows while
        /// there is a target): starts a fade only when the wish changes, so calling it each frame is free.
        /// </summary>
        public static void Set(GameObject go, bool shown, float seconds = InSeconds)
        {
            if (go == null) return;
            var a = go.GetComponent<Appear>();
            var now = a != null && a.enabled ? a._target > 0.5f : go.activeSelf;
            if (now == shown) return;
            if (shown) In(go, seconds); else Out(go, seconds);
        }

        void Begin(float target, float seconds, bool destroy, float from)
        {
            _target = target;
            Prepare();
            _target = target; _seconds = Mathf.Max(0.01f, seconds); _destroy = destroy; _k = from;
            Apply(Ease(_k));
            enabled = true;
        }

        static float Ease(float k) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));

        void Prepare()
        {
            if (!_prepared)
            {
                _prepared = true;
                // A card inside a bigger canvas (the waist line card, a chip row): fade it as a group of its own.
                if (transform is RectTransform && GetComponent<Canvas>() == null && GetComponentInParent<Canvas>() != null)
                {
                    var own = GetComponent<CanvasGroup>();
                    var added = own == null;
                    if (added) own = gameObject.AddComponent<CanvasGroup>();
                    _groups.Add((own, own.alpha, added));
                }
            }
            Adopt();
        }

        /// <summary>
        /// The renderers under it not yet faded. Also run while a fade is under way: a chapter switched on builds its
        /// pieces in Start, a frame later, and they join at the current level instead of popping in whole.
        /// Lines and particles are left alone (other code recolours a line's material as it draws).
        /// </summary>
        void Adopt()
        {
            foreach (var c in GetComponentsInChildren<Canvas>(true))
            {
                if (c.renderMode != RenderMode.WorldSpace || !_seenCanvas.Add(c)) continue;
                // A canvas with a CanvasGroup of its own already fades itself (the chapter question, the memento): left to
                // it coming in, so the two never fight; going out it is taken over, since it is leaving anyway.
                if (c.GetComponent<CanvasGroup>() != null && c.gameObject != gameObject && _target > 0.5f) { _seenCanvas.Remove(c); continue; }
                var g = c.GetComponent<CanvasGroup>();
                var added = g == null;
                if (added) g = c.gameObject.AddComponent<CanvasGroup>();
                _groups.Add((g, g.alpha, added));
                if (_prepared && _k < 1f) g.alpha = 0f;   // joins hidden; Apply brings it to the current level
            }
            foreach (var t in GetComponentsInChildren<TMP_Text>(true))
                if (t.GetComponentInParent<Canvas>() == null && _seenText.Add(t)) _texts.Add((t, t.alpha));
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!_seen.Add(r)) continue;
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer || r.GetComponent<TMP_Text>() != null) continue;
                var originals = r.sharedMaterials;
                var fades = new Material[originals.Length];
                var prop = new int[originals.Length];
                var colour = new Color[originals.Length];
                var additive = new bool[originals.Length];
                var ok = originals.Length > 0;
                for (var i = 0; i < originals.Length && ok; i++)
                {
                    var m = originals[i];
                    if (m == null || m.shader == null) { ok = false; break; }
                    var name = m.shader.name;
                    if (name.StartsWith("Universal Render Pipeline/") && m.HasProperty(BaseColor)) prop[i] = BaseColor;
                    else if (name.StartsWith("Shader Graphs/glTF") && m.HasProperty(GltfBaseColor)) prop[i] = GltfBaseColor;
                    else { ok = false; break; }
                    var f = new Material(m) { name = m.name + " (fade)" };
                    // An additive glow (the guide's floor rings) fades by dimming: made alpha-blended it went dark.
                    additive[i] = m.HasProperty("_DstBlend") && Mathf.Approximately(m.GetFloat("_DstBlend"), (float)UnityEngine.Rendering.BlendMode.One);
                    if (!additive[i]) Transparent(f);
                    colour[i] = m.GetColor(prop[i]);
                    fades[i] = f;
                }
                if (!ok)
                {
                    foreach (var f in fades) if (f != null) Destroy(f);
                    _scaled.Add((r.transform, r.transform.localScale));
                    continue;
                }
                _faded.Add(new Faded { r = r, originals = originals, fades = fades, prop = prop, colour = colour, additive = additive });
            }
        }

        /// <summary>URP's transparent surface, the same for Lit, Unlit and glTFast's graphs (the keyword is the half that is easy to forget).</summary>
        static void Transparent(Material m)
        {
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_AlphaDstBlend")) m.SetFloat("_AlphaDstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        void Update()
        {
            if (_target > 0f) Adopt();
            var step = Time.deltaTime / _seconds;
            _k = Mathf.MoveTowards(_k, _target, step);
            Apply(Ease(_k));
            if (!Mathf.Approximately(_k, _target)) return;
            enabled = false;
            if (_target >= 1f) { Restore(); return; }
            Gone?.Invoke(gameObject);
            if (_destroy) Destroy(gameObject);
            else { Restore(); gameObject.SetActive(false); }
        }

        void Apply(float k)
        {
            foreach (var f in _faded)
            {
                if (f.r == null) continue;
                if (f.r.sharedMaterials != f.fades) f.r.sharedMaterials = f.fades;
                for (var i = 0; i < f.fades.Length; i++)
                {
                    var c = f.colour[i];
                    if (f.additive[i]) { c.r *= k; c.g *= k; c.b *= k; } else c.a *= k;
                    f.fades[i].SetColor(f.prop[i], c);
                }
            }
            foreach (var (g, alpha, _) in _groups) if (g != null) g.alpha = alpha * k;
            foreach (var (t, alpha) in _texts) if (t != null) t.alpha = alpha * k;
            foreach (var (t, scale) in _scaled) if (t != null) t.localScale = scale * Mathf.Lerp(0.85f, 1f, k);
        }

        /// <summary>Everything as it was: opaque originals back, alphas and scales restored. The component stays for the next fade.</summary>
        void Restore()
        {
            // Only where our fade copies are still on: anything another script has put on since is left as it is.
            foreach (var f in _faded) if (f.r != null && f.r.sharedMaterials.Length == f.fades.Length && f.r.sharedMaterials[0] == f.fades[0]) f.r.sharedMaterials = f.originals;
            foreach (var (g, alpha, _) in _groups) if (g != null) g.alpha = alpha;
            foreach (var (t, alpha) in _texts) if (t != null) t.alpha = alpha;
            foreach (var (t, scale) in _scaled) if (t != null) t.localScale = scale;
        }

        void OnDestroy()
        {
            foreach (var f in _faded) foreach (var m in f.fades) if (m != null) Destroy(m);
        }
    }
}
