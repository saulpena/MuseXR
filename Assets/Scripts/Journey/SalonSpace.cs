using System.Collections.Generic;
using MusePico.Dialogue;
using UnityEngine;

namespace MusePico.Journey
{
    /// <summary>
    /// Her point-cloud "impossible manor" (<see cref="MemoryWorld"/>), made by the visitor's answer.
    ///
    /// Nothing of it exists before Socrates' question is answered (Saul, 1 Oct 2026). On the answer
    /// it bursts out as particles all round the visitor, in the colour of the answer (hers: mist,
    /// fracture, turbulence), swirls for the first third of the twelve-second transformation, then
    /// gathers into the manor standing where hers stands, its facade 15.5 m ahead, coming 4.5 m
    /// closer as her camera pushes in and shaking with her stageDrift. It stays, settled, through
    /// the manifesto. The world around it is a splat (the salon stages run in the Buddha Hall).
    /// </summary>
    public sealed class SalonSpace : MonoBehaviour
    {
        const float PointScale = 0.05f;      // her size is in pixels; this makes it metres at her depth

        ParticleSystem _cloud;
        ParticleSystem.Particle[] _particles;
        List<MemoryPoint> _points;
        Vector3[] _rest, _start;
        Color32[] _family, _storm;
        float _transformationStart = -1f, _transformationSeconds = 12f;
        bool _settled;

        /// <summary>The frame the manor stands in: origin on the visitor's floor, +Z the way they faced.</summary>
        public Transform Frame => transform;

        public static SalonSpace Open(Transform head, float floorY)
        {
            var go = new GameObject("Manor (her memory architecture)");
            var s = go.AddComponent<SalonSpace>();
            s.Build(head, floorY);
            return s;
        }

        void Build(Transform head, float floorY)
        {
            var yaw = head != null ? head.eulerAngles.y : 0f;
            var at = head != null ? head.position : Vector3.zero;
            transform.SetPositionAndRotation(new Vector3(at.x, floorY, at.z), Quaternion.Euler(0f, yaw, 0f));

            _points = MemoryWorld.Create();
            _cloud = gameObject.AddComponent<ParticleSystem>();
            _cloud.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _cloud.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = _points.Count + 16;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = _cloud.emission; emission.enabled = false;
            GetComponent<ParticleSystemRenderer>().material = Dot();

            var n = _points.Count;
            _particles = new ParticleSystem.Particle[n];
            _rest = new Vector3[n];
            _start = new Vector3[n];
            _family = new Color32[n];
            _storm = new Color32[n];
            var eye = head != null ? head.position : transform.position + Vector3.up * 1.5f;
            for (var i = 0; i < n; i++)
            {
                var p = _points[i];
                // Her floor is y -2.5; ours is the visitor's floor.
                _rest[i] = transform.TransformPoint(new Vector3(p.X, p.Y + 2.5f, p.Z));
                // The burst: a shell all round the visitor, 1.5-6 m out.
                var dir = new Vector3(MemoryWorld.Random(i, 21) - .5f, MemoryWorld.Random(i, 22) - .5f, MemoryWorld.Random(i, 23) - .5f);
                if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
                _start[i] = eye + dir.normalized * (1.5f + MemoryWorld.Random(i, 24) * 4.5f);
                var rgb = MemoryWorld.Palette.TryGetValue(p.Family, out var c) ? c : MemoryWorld.Palette["dust"];
                var familyAlpha = p.Family == "dust" ? .45f : p.Family == "mist" ? .5f : p.Family == "garden" ? .8f : 1f;
                _family[i] = new Color32((byte)rgb[0], (byte)rgb[1], (byte)rgb[2], (byte)(255 * familyAlpha));
                _particles[i].startSize = Mathf.Max(0.02f, p.Size * PointScale);
                _particles[i].startLifetime = _particles[i].remainingLifetime = 1e6f;
            }
            // Not drawn until the answer.
            _cloud.SetParticles(_particles, 0);
        }

        /// <summary>Her palette for each answer's storm (app.js drawParticle).</summary>
        static Color32 StormColour(string choice) =>
            choice == "invention" ? new Color32(196, 77, 96, 230)       // fracture
            : choice == "emotion" ? new Color32(109, 139, 211, 230)     // turbulence
            : new Color32(125, 190, 182, 230);                          // mist

        /// <summary>The answer is given: burst out round the visitor and gather into the manor.</summary>
        public void BeginTransformation(string choice, float seconds)
        {
            _transformationStart = Time.time;
            _transformationSeconds = seconds;
            _settled = false;
            var storm = StormColour(choice);
            for (var i = 0; i < _storm.Length; i++) _storm[i] = storm;
        }

        /// <summary>The ending: the manor stands, settled where the transformation left it.</summary>
        public void Settle()
        {
            _transformationStart = -1f;
            _settled = true;
            var close = transform.forward * -4.5f;
            for (var i = 0; i < _particles.Length; i++)
            {
                _particles[i].position = _rest[i] + close;
                _particles[i].startColor = _family[i];
            }
            _cloud.SetParticles(_particles, _particles.Length);
        }

        void LateUpdate()
        {
            if (_settled || _transformationStart < 0f || _cloud == null) return;
            var progress = Mathf.Clamp01((Time.time - _transformationStart) / _transformationSeconds);
            var t = Time.time;

            // Her timeline: chaos throughout, peaking mid-way; the gathering from 36% on.
            var chaos = Mathf.Sin(progress * Mathf.PI) * 0.9f;
            var blend = progress > .36f ? Mathf.Min(1f, (progress - .36f) / .64f) : 0f;
            var eased = blend * blend * (3f - 2f * blend);
            var approach = transform.forward * (-4.5f * Mathf.SmoothStep(0f, 1f, progress));
            var drift = Mathf.Sin(progress * Mathf.PI) * 1.25f * (1f - eased * 0.7f);
            var right = transform.right;
            var up = transform.up;

            for (var i = 0; i < _particles.Length; i++)
            {
                var p = _points[i];
                var swirl = new Vector3(Mathf.Sin(p.Seed * 2f + t * 8f), Mathf.Cos(p.Seed + t * 6f) * .45f, Mathf.Sin(p.Seed * 3f + t * 5f)) * chaos;
                var fracture = drift * Mathf.Sin(p.Seed + t * 7f) * (p.Family == "stone" ? .65f : .24f);
                var home = _rest[i] + approach + right * fracture + up * (fracture * .18f);
                _particles[i].position = Vector3.Lerp(_start[i] + swirl, home, eased);
                _particles[i].startColor = Color32.Lerp(_storm[i], _family[i], eased);
            }
            _cloud.SetParticles(_particles, _particles.Length);
            if (progress >= 1f) Settle();
        }

        public void Close() => Destroy(gameObject);

        static Material Dot()
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var dx = (x + .5f) / n * 2f - 1f;
                    var dy = (y + .5f) / n * 2f - 1f;
                    var a = Mathf.Clamp01(1.4f - 1.4f * Mathf.Sqrt(dx * dx + dy * dy));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1f);
            // Alpha-blended: added light would vanish against a lit splat hall.
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
