using UnityEngine;

namespace MusePico.Journey
{
    /// <summary>
    /// Stage 08, the world rewriting itself: particles all round the visitor in the style of the
    /// answer they gave Socrates. Hers (app.js choose): invention is "fracture", emotion is
    /// "turbulence", perception is "mist", and her canvas particles take that mode for the twelve
    /// seconds of the transformation. Here they are a ParticleSystem centred on the head, so they
    /// surround the visitor in a headset rather than sitting on a flat screen.
    ///
    ///   * mist: soft, slow, pale blue-white motes drifting upward.
    ///   * turbulence: warm motes swirling on a strong noise field.
    ///   * fracture: thin violet and cyan shards bursting outward from the visitor.
    /// </summary>
    public sealed class TransformationParticles : MonoBehaviour
    {
        ParticleSystem _system;
        ParticleSystem.Particle[] _buffer;
        string _choice;
        Transform _frame;
        float _start, _seconds;
        bool _converging;

        /// <summary>
        /// Her finalParticleTarget: for the first 36% of the beat the particles stay in their storm;
        /// after it they ease into the shape of the answer, in front of the visitor in
        /// <paramref name="frame"/> — a horizon band, a vortex, or a facade grid of windows.
        /// </summary>
        public void ConvergeTo(string choice, Transform frame, float seconds)
        {
            _choice = choice;
            _frame = frame;
            _start = Time.time;
            _seconds = seconds;
        }

        void LateUpdate()
        {
            if (_frame == null || _system == null) return;
            var progress = Mathf.Clamp01((Time.time - _start) / Mathf.Max(0.01f, _seconds));
            if (progress <= .36f) return;
            if (!_converging)
            {
                _converging = true;
                var emission = _system.emission; emission.enabled = false;   // the storm's last breath
                // A stretched particle that comes to rest has no length and vanishes: the fracture
                // shards settling into the facade grid were invisible until they became dots.
                GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
            }
            var blend = Mathf.Min(1f, (progress - .36f) / .64f);
            var eased = blend * blend * (3f - 2f * blend);
            if (_buffer == null || _buffer.Length < _system.main.maxParticles) _buffer = new ParticleSystem.Particle[_system.main.maxParticles];
            var n = _system.GetParticles(_buffer);
            var k = 1f - Mathf.Exp(-Time.deltaTime * (1.5f + 6f * eased));
            for (var i = 0; i < n; i++)
            {
                var seed = _buffer[i].randomSeed;
                var sx = (seed % 997u) / 997f;
                var sy = (seed / 997u % 991u) / 991f;
                MusePico.Dialogue.MemoryWorld.FinalTarget(_choice, i, n, sx, sy, Time.time, out var x, out var y, out var z);
                var target = _frame.TransformPoint(new Vector3(x, y, z));
                _buffer[i].position = Vector3.Lerp(_buffer[i].position, target, k * eased);
                _buffer[i].velocity *= 1f - eased;
                // Held at mid-life, where the fade is at its peak: lengthening the remaining life past
                // the start life put them before their own fade-in, fully transparent (1 Oct 2026).
                _buffer[i].startLifetime = 8f;
                _buffer[i].remainingLifetime = 4f;
            }
            _system.SetParticles(_buffer, n);
        }

        /// <summary>Her particleMode for a stage-07 answer id.</summary>
        public static string ModeFor(string choiceId) =>
            choiceId == "invention" ? "fracture" : choiceId == "emotion" ? "turbulence" : "mist";

        public static TransformationParticles Create(string mode, Transform head)
        {
            var go = new GameObject("Transformation Particles (" + mode + ")");
            var tp = go.AddComponent<TransformationParticles>();
            tp.Build(mode, head);
            return tp;
        }

        /// <summary>Stop emitting and go once the last particle has faded.</summary>
        public void Release()
        {
            _frame = null;   // stop holding them: they fade out over their remaining life
            if (_system != null) _system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(gameObject, 5f);
        }

        void Build(string mode, Transform head)
        {
            if (head != null) transform.SetParent(head, false);   // all round the visitor, wherever they look
            _system = gameObject.AddComponent<ParticleSystem>();
            _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _system.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4000;
            var emission = _system.emission;
            var shape = _system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            var noise = _system.noise;
            var colour = _system.colorOverLifetime;
            colour.enabled = true;
            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.material = Material(mode);

            switch (mode)
            {
                case "fracture":
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.2f, 0.95f), new Color(0.1f, 0.75f, 0.95f));
                    emission.rateOverTime = 420f;
                    // Through the space round the visitor in every direction. Fired straight out from
                    // the head they ran exactly along the line of sight, and a stretched particle seen
                    // end-on is a dot: 750 shards alive and none visible (1 Oct 2026).
                    shape.radius = 5f;
                    shape.radiusThickness = 1f;
                    shape.randomDirectionAmount = 1f;
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.velocityScale = 0.12f;
                    renderer.lengthScale = 2f;
                    colour.color = Fade(new Color(1f, 1f, 1f), 0.9f);
                    break;

                case "turbulence":
                    main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.1f), new Color(0.85f, 0.15f, 0.25f));
                    emission.rateOverTime = 500f;
                    shape.radius = 4f;
                    noise.enabled = true;
                    noise.strength = 2.2f;
                    noise.frequency = 0.35f;
                    noise.scrollSpeed = 0.6f;
                    noise.octaveCount = 2;
                    colour.color = Fade(Color.white, 0.85f);
                    break;

                default: // mist
                    main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.92f, 1f, 0.35f), new Color(1f, 1f, 1f, 0.2f));
                    main.gravityModifier = -0.02f;        // a slow rise
                    emission.rateOverTime = 160f;
                    shape.radius = 5f;
                    noise.enabled = true;
                    noise.strength = 0.3f;
                    noise.frequency = 0.2f;
                    colour.color = Fade(Color.white, 0.6f);
                    break;
            }
            _system.Play();
        }

        /// <summary>Fade in over the first fifth of a life, out over the last third.</summary>
        static ParticleSystem.MinMaxGradient Fade(Color c, float peak)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, 0.2f),
                              new GradientAlphaKey(peak, 0.66f), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        /// <summary>A soft round additive sprite, made here so nothing has to be imported.</summary>
        static Material Material(string mode)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var dx = (x + 0.5f) / n * 2f - 1f;
                    var dy = (y + 0.5f) / n * 2f - 1f;
                    var a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = mode == "fracture" ? Mathf.Pow(a, 0.6f) : a * a;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();

            // Particles/Unlit ships only because Resources/Materials/ParticlesUnlit.mat uses it: from code alone, Shader.Find
            // found nothing in a build and the throw stopped the Grotto's setup cold (no rim, no keep, no arch - Quest, 5 Oct).
            var shader = (Resources.Load<Material>("Materials/ParticlesUnlit") is Material pmat && pmat != null ? pmat.shader : (Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit")));
            var m = new Material(shader);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1f);                                   // transparent
            // Alpha-blended, not additive: added light vanished against a sunlit world (the live run,
            // 1 Oct 2026, showed fracture as almost nothing over the conservatory).
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            return m;
        }
    }
}
