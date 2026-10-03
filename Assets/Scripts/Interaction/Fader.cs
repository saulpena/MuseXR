using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Fades a figure out and switches it off: the uninvited masters once the visitor has chosen
    /// (decided by Saul, 3 Oct 2026: "fade out"). Each renderer gets its own transparent copy of its
    /// materials (URP Lit and Unlit both read _BaseColor's alpha once _Surface is transparent and the
    /// keyword is on - the keyword is the half that is easy to forget), so shared materials are never
    /// touched. Works on a standee board or a rigged master alike.
    /// </summary>
    public sealed class Fader : MonoBehaviour
    {
        public const float DefaultSeconds = 0.6f;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        float _seconds, _t;
        readonly List<(Material m, Color c)> _mats = new List<(Material, Color)>();

        public event Action<Fader> Gone;

        public static Fader FadeOut(GameObject go, float seconds = DefaultSeconds)
        {
            var f = go.GetComponent<Fader>();
            if (f == null) f = go.AddComponent<Fader>();
            f._seconds = Mathf.Max(0.01f, seconds);
            f._t = 0f;
            f._mats.Clear();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is TMPro.TMP_SubMeshUI) continue;
                var mats = r.materials;   // instances: the shared materials stay opaque
                foreach (var m in mats)
                {
                    if (m == null || !m.HasProperty(BaseColor)) continue;
                    MakeTransparent(m);
                    f._mats.Add((m, m.GetColor(BaseColor)));
                }
                r.materials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (var p in go.GetComponentsInChildren<Pointable>()) p.Interactive = false;
            return f;
        }

        static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        void Update()
        {
            _t += Time.deltaTime;
            var a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / _seconds));
            foreach (var (m, c) in _mats) m.SetColor(BaseColor, new Color(c.r, c.g, c.b, c.a * a));
            if (_t < _seconds) return;
            enabled = false;
            gameObject.SetActive(false);
            Gone?.Invoke(this);
        }
    }
}
