using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her "the distant Buddha silhouette is rimmed", as a line of soft gold sparks along the figure's own outline
    /// (Saul, 5 Oct: "a halo is not a silhouette rim" ... "place particles along the outline"). The outline is traced once
    /// from a render of the figure's mesh alone, seen from where the visitor stands to choose (Resources/Rims/*.json, in
    /// the chapter frame's space). At 65 m it barely shifts as the visitor walks the terrace. Drawn after the splat world
    /// like the soft halo it replaces (a solid shell drawn before it blocked the world behind, measured), so nothing of
    /// the splat renderer is touched. The sparks share one additive material: whoever fades it (GrottoChapter, black to
    /// gold) fades the whole rim.
    /// </summary>
    public sealed class SparkleRim : MonoBehaviour
    {
        /// <summary>A spark's size, metres at the figure's distance (about 0.8 degrees at 65 m), and how much it twinkles.</summary>
        public const float SparkSize = 2.2f, Twinkle = 0.3f, TwinkleSpeed = 2.2f;
        /// <summary>The sparks' gold, drawn (not added): additive gold vanished against the bright sky (measured, 5 Oct).</summary>
        public static readonly Color Gold = new Color(1f, 0.78f, 0.28f);

        ParticleSystem _ps;
        Material _material;
        float _shown;
        ParticleSystem.Particle[] _sparks;
        float[] _phase;

        /// <summary>The rim's renderer (black: invisible, additive), its sparks on the outline in <paramref name="resource"/>.</summary>
        public static Renderer Make(Transform frame, Transform parent, string name, string resource)
        {
            var points = Load(resource);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.maxParticles = Mathf.Max(1, points.Count);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1e6f; main.startSpeed = 0f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            // Particles/Unlit takes each spark's own colour; alpha-blended so the gold reads on sky and stone alike.
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name + " (sparks)" };
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetTexture("_BaseMap", Spark());
            m.SetColor("_BaseColor", Color.black);
            r.sharedMaterial = m;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.minParticleSize = 0f; r.maxParticleSize = 1f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;

            var rim = go.AddComponent<SparkleRim>();
            rim._ps = ps; rim._material = m;
            rim._sparks = new ParticleSystem.Particle[points.Count];
            rim._phase = new float[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                rim._sparks[i].position = frame != null ? frame.TransformPoint(points[i]) : points[i];
                rim._sparks[i].startSize = SparkSize;
                rim._sparks[i].startColor = new Color(Gold.r, Gold.g, Gold.b, 0f);   // hidden until the chapter fades it up
                rim._sparks[i].remainingLifetime = 1e6f; rim._sparks[i].startLifetime = 1e6f;
                rim._phase[i] = (i * 0.61803f) % 1f * Mathf.PI * 2f;
            }
            ps.SetParticles(rim._sparks, rim._sparks.Length);
            if (points.Count == 0) Debug.LogWarning("[Rim] no outline in Resources/" + resource);
            return r;
        }

        static List<Vector3> Load(string resource)
        {
            var list = new List<Vector3>();
            var asset = Resources.Load<TextAsset>(resource);
            if (asset == null) return list;
            // The file's "points" are [[x,y,z], ...]: read the numbers in order (JsonUtility has no nested arrays).
            var text = asset.text;
            var i = text.IndexOf("\"points\"", System.StringComparison.Ordinal);
            if (i < 0) return list;
            var nums = new List<float>();
            var sb = new System.Text.StringBuilder();
            for (var k = text.IndexOf('[', i); k < text.Length; k++)
            {
                var ch = text[k];
                if (char.IsDigit(ch) || ch == '-' || ch == '.' || ch == 'e' || ch == 'E') sb.Append(ch);
                else
                {
                    if (sb.Length > 0) { nums.Add(float.Parse(sb.ToString(), System.Globalization.CultureInfo.InvariantCulture)); sb.Clear(); }
                    if (ch == '}') break;
                }
            }
            for (var n = 0; n + 2 < nums.Count; n += 3) list.Add(new Vector3(nums[n], nums[n + 1], nums[n + 2]));
            return list;
        }

        void LateUpdate()
        {
            if (_ps == null || _sparks == null || _sparks.Length == 0) return;
            // The chapter fades the rim by its material colour (black to gold): read how far, carry it as the sparks'
            // opacity, and keep the material itself white so each spark's own gold shows.
            if (_material != null)
            {
                var c = _material.GetColor("_BaseColor");
                if (c != Color.white) { _shown = Mathf.Clamp01(c.maxColorComponent); _material.SetColor("_BaseColor", Color.white); }
            }
            var t = Time.time * TwinkleSpeed;
            for (var i = 0; i < _sparks.Length; i++)
            {
                var tw = Mathf.Sin(t + _phase[i]);
                _sparks[i].startSize = SparkSize * (1f - Twinkle * 0.5f + Twinkle * 0.5f * tw);
                _sparks[i].startColor = new Color(Gold.r, Gold.g, Gold.b, _shown * (0.8f + 0.2f * tw));
            }
            _ps.SetParticles(_sparks, _sparks.Length);
        }

        /// <summary>A soft round spark: bright core, falling off to nothing at the edge.</summary>
        static Texture2D Spark()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "spark" };
            var px = new Color[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude / (n / 2f);
                    var a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a); a = Mathf.Pow(a, 1.6f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);   // white: the spark takes its gold from its own colour
                }
            tex.SetPixels(px); tex.Apply(false, true);
            return tex;
        }
    }
}
