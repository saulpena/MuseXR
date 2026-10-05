using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her "the distant Buddha silhouette is rimmed", as a line of gold sparks along the figure's own outline (Saul,
    /// 5 Oct: "a halo is not a silhouette rim" ... "place particles along the outline" ... "denser, larger, more golden,
    /// magical"). The outline is traced once from a render of the figure's mesh alone, from where the visitor stands to
    /// choose (Resources/Rims/*.json, chapter-frame space, with the figure's centre and the point it was seen from).
    /// The traced ring is turned about the figure's upright axis to face the visitor wherever they stand: from a terrace
    /// a few metres across, a 65 m figure is only ever seen within a few degrees of the traced view, so the turned
    /// outline stays on its edge without tracing every angle. Two layers - small bright cores and a wide soft glow -
    /// twinkling out of step. Drawn after the splat world like the halo it replaces (a solid shell drawn before it
    /// blocked the world behind, measured), so nothing of the splat renderer is touched. The chapter fades the rim by
    /// the material colour (black to gold); that level is carried as the sparks' opacity.
    /// </summary>
    public sealed class SparkleRim : MonoBehaviour
    {
        /// <summary>Core and glow sizes, metres at the figure's distance; how much they twinkle.</summary>
        public const float CoreSize = 2.6f, GlowSize = 7f, Twinkle = 0.45f, TwinkleSpeed = 2.6f;
        public static readonly Color Gold = new Color(1f, 0.8f, 0.3f), GlowGold = new Color(1f, 0.62f, 0.16f);
        public const float GlowAlpha = 0.3f;

        ParticleSystem _core, _glow;
        ParticleSystem.Particle[] _cores, _glows;
        Vector3[] _local;
        float[] _phase;
        Transform _frame;
        Vector3 _centre, _seenFrom;
        bool _turn;
        Material _material;
        float _shown;

        /// <summary>The rim's renderer (its material black: hidden), its sparks on the outline in <paramref name="resource"/>.</summary>
        public static Renderer Make(Transform frame, Transform parent, string name, string resource)
        {
            var asset = Resources.Load<TextAsset>(resource);
            var text = asset != null ? asset.text : "";
            var points = Vectors(text, "points");
            var centre = Vectors(text, "centre");
            var from = Vectors(text, "seenFrom");
            if (points.Count == 0) Debug.LogWarning("[Rim] no outline in Resources/" + resource);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            // Particles/Unlit ships only because Resources/Materials/ParticlesUnlit.mat uses it: from code alone, Shader.Find
            // found nothing in a build and the throw stopped the Grotto's setup cold (no rim, no keep, no arch - Quest, 5 Oct).
            var m = new Material((Resources.Load<Material>("Materials/ParticlesUnlit") is Material pmat && pmat != null ? pmat.shader : (Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit")))) { name = name + " (sparks)" };
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);   // drawn gold, not added: additive vanished on the sky
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetTexture("_BaseMap", Spark());
            m.SetColor("_BaseColor", Color.black);

            var rim = go.AddComponent<SparkleRim>();
            rim._frame = frame; rim._material = m;
            rim._centre = centre.Count > 0 ? centre[0] : Vector3.zero;
            rim._seenFrom = from.Count > 0 ? from[0] : Vector3.zero;
            rim._turn = centre.Count > 0 && from.Count > 0;
            rim._local = points.ToArray();
            rim._phase = new float[points.Count];
            for (var i = 0; i < points.Count; i++) rim._phase[i] = Random.value * Mathf.PI * 2f;
            // The glow first, so the bright cores draw over it.
            var glowGo = new GameObject(name + " glow"); glowGo.transform.SetParent(go.transform, false);
            rim._glow = Layer(glowGo, m, points.Count, 0); rim._glows = new ParticleSystem.Particle[points.Count];
            rim._core = Layer(go, m, points.Count, 1); rim._cores = new ParticleSystem.Particle[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                rim._cores[i].remainingLifetime = rim._cores[i].startLifetime = 1e6f;
                rim._glows[i].remainingLifetime = rim._glows[i].startLifetime = 1e6f;
            }
            return go.GetComponent<ParticleSystemRenderer>();
        }

        static ParticleSystem Layer(GameObject go, Material m, int count, int order)
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.maxParticles = Mathf.Max(1, count);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1e6f; main.startSpeed = 0f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.minParticleSize = 0f; r.maxParticleSize = 1f;
            r.sortingFudge = order == 0 ? 10f : 0f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        void LateUpdate()
        {
            if (_core == null || _local == null || _local.Length == 0) return;
            // The chapter's fade level, read off the material it sets; the material itself stays white.
            if (_material != null)
            {
                var c = _material.GetColor("_BaseColor");
                if (c != Color.white) { _shown = Mathf.Clamp01(c.maxColorComponent); _material.SetColor("_BaseColor", Color.white); }
            }
            if (_shown <= 0f) { if (_core.particleCount > 0) { _core.Clear(); _glow.Clear(); } return; }

            // Turn the traced outline about the figure's upright axis to face where the visitor stands now.
            var turn = Quaternion.identity;
            if (_turn && Camera.main != null)
            {
                var cam = Camera.main.transform.position;
                var eye = _frame != null ? _frame.InverseTransformPoint(cam) : cam;
                var a = _seenFrom - _centre; a.y = 0f;
                var b = eye - _centre; b.y = 0f;
                if (a.sqrMagnitude > 1e-4f && b.sqrMagnitude > 1e-4f) turn = Quaternion.FromToRotation(a.normalized, b.normalized);
            }
            var t = Time.time * TwinkleSpeed;
            for (var i = 0; i < _local.Length; i++)
            {
                var local = _centre + turn * (_local[i] - _centre);
                var world = _frame != null ? _frame.TransformPoint(local) : local;
                var tw = Mathf.Sin(t + _phase[i]);
                var tw2 = Mathf.Sin(t * 0.37f + _phase[i] * 1.7f);
                _cores[i].position = world;
                _cores[i].startSize = CoreSize * (1f - Twinkle * 0.5f + Twinkle * 0.5f * tw);
                _cores[i].startColor = new Color(Gold.r, Gold.g, Gold.b, _shown * (0.75f + 0.25f * tw));
                _glows[i].position = world;
                _glows[i].startSize = GlowSize * (0.85f + 0.15f * tw2);
                _glows[i].startColor = new Color(GlowGold.r, GlowGold.g, GlowGold.b, _shown * GlowAlpha * (0.7f + 0.3f * tw2));
            }
            _glow.SetParticles(_glows, _glows.Length);
            _core.SetParticles(_cores, _cores.Length);
        }

        /// <summary>The [x,y,z] triples under <paramref name="key"/> in the outline file (JsonUtility has no nested arrays).</summary>
        static List<Vector3> Vectors(string text, string key)
        {
            var list = new List<Vector3>();
            var i = text.IndexOf("\"" + key + "\"", System.StringComparison.Ordinal);
            if (i < 0) return list;
            var start = text.IndexOf('[', i);
            if (start < 0) return list;
            var depth = 0; var nums = new List<float>(); var sb = new System.Text.StringBuilder();
            for (var k = start; k < text.Length; k++)
            {
                var ch = text[k];
                if (ch == '[') depth++;
                if (char.IsDigit(ch) || ch == '-' || ch == '.' || ch == 'e' || ch == 'E') { sb.Append(ch); continue; }
                if (sb.Length > 0) { nums.Add(float.Parse(sb.ToString(), System.Globalization.CultureInfo.InvariantCulture)); sb.Clear(); }
                if (ch == ']' && --depth == 0) break;
            }
            for (var n = 0; n + 2 < nums.Count; n += 3) list.Add(new Vector3(nums[n], nums[n + 1], nums[n + 2]));
            return list;
        }

        /// <summary>A soft round spark: white (it takes its colour from each spark), bright core falling off to nothing.</summary>
        static Texture2D Spark()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "spark" };
            var px = new Color[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude / (n / 2f);
                    var a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a); a = Mathf.Pow(a, 1.4f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply(false, true);
            return tex;
        }
    }
}
