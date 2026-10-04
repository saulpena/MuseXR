using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her one-tap replicate (MUSE-VR-design, hero works): aim at a chapter's hero work and hold the
    /// trigger for one second; a band of gold light sweeps down it and a small copy goes into the
    /// satchel on the left wrist. One copy per chapter; the copy keeps its label.
    /// </summary>
    public sealed class Replicable : MonoBehaviour
    {
        public const float HoldSeconds = 1f, SweepSeconds = 0.8f;

        public string label;
        public string chapter;
        /// <summary>What goes into the satchel; this object when null.</summary>
        public GameObject copySource;

        Pointable _pointable;
        float _held;
        bool _sweeping;
        GameObject _glow;
        Transform _holdRing;
        Mesh _arc;
        float _arcShown = -1f;

        /// <summary>The replica's caption, as her script words it ("palm-sized framed Mona Lisa").</summary>
        public string replicaName;

        public static Replicable Make(GameObject go, string label, string chapter, GameObject copySource = null)
        {
            var r = go.GetComponent<Replicable>();
            if (r == null) r = go.AddComponent<Replicable>();
            r.label = label; r.chapter = chapter; r.copySource = copySource;
            return r;
        }

        void Awake()
        {
            _pointable = GetComponent<Pointable>();
            if (_pointable == null) _pointable = Pointable.Make(gameObject, "replicate " + label);
        }

        void Start()
        {
            _glow = MakeGlow();
            _pointable.Hovering += _ => { if (_glow != null && !Done) _glow.SetActive(true); };
            _pointable.Unhovered += _ => { if (_glow != null) _glow.SetActive(false); };
            MakeHoldRing();
        }

        bool Done { get { var s = Satchel.Get(); return s != null && s.Has(chapter); } }

        /// <summary>Her hover cue: a soft gold glow just behind the work, a little larger than it.</summary>
        GameObject MakeGlow()
        {
            var b = LocalBounds();
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Replicate glow"; Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            // Just behind the work's own face but in front of whatever it hangs on: behind the frame's back
            // edge the white wall hid it completely (Editor capture, 4 Oct).
            q.transform.localPosition = new Vector3(b.center.x, b.center.y, Mathf.Min(b.max.z, 0.012f));
            q.transform.localScale = new Vector3(b.size.x + 0.16f, b.size.y + 0.16f, 1f);   // an 8 cm rim round the frame
            var rim = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            rim.SetColor("_BaseColor", new Color(1f, 0.82f, 0.38f));   // opaque: added gold on a white wall stayed white
            q.GetComponent<Renderer>().sharedMaterial = rim;
            q.SetActive(false);
            return q.gameObject;
        }

        /// <summary>A gold ring that fills while the trigger is held, at the ray's hit point, over a faint track.</summary>
        void MakeHoldRing()
        {
            _holdRing = new GameObject("Replicate hold").transform;
            var track = new GameObject("Track").transform;
            track.SetParent(_holdRing, false);
            track.gameObject.AddComponent<MeshFilter>().sharedMesh = Arc(1f, 0.08f, 0.1f);
            track.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Flat(new Color(1f, 1f, 1f, 0.9f));
            var fill = new GameObject("Fill").transform;
            fill.SetParent(_holdRing, false);
            fill.localPosition = new Vector3(0f, 0f, -0.002f);
            _arc = new Mesh { name = "Replicate hold arc" };
            fill.gameObject.AddComponent<MeshFilter>().sharedMesh = _arc;
            fill.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Flat(new Color(1f, 0.78f, 0.3f));
            _holdRing.gameObject.SetActive(false);
        }

        static Material Flat(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Cull", 0f);
            return m;
        }

        /// <summary>The ring from 12 o'clock clockwise through <paramref name="fraction"/> of a turn (XY plane).</summary>
        static Mesh Arc(float fraction, float radius, float outer, Mesh into = null)
        {
            var m = into ?? new Mesh();
            m.Clear();
            int n = Mathf.Max(2, Mathf.CeilToInt(64 * Mathf.Clamp01(fraction)) + 1);
            var v = new Vector3[n * 2]; var t = new int[(n - 1) * 6];
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI / 2f - Mathf.Clamp01(fraction) * Mathf.PI * 2f * i / (n - 1);
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v[i * 2] = d * radius; v[i * 2 + 1] = d * outer;
                if (i < n - 1) { int k = i * 2, j = i * 6; t[j] = k; t[j + 1] = k + 1; t[j + 2] = k + 2; t[j + 3] = k + 1; t[j + 4] = k + 3; t[j + 5] = k + 2; }
            }
            m.vertices = v; m.triangles = t; m.RecalculateBounds();
            return m;
        }

        void Update()
        {
            if (_sweeping) return;
            var satchel = Satchel.Get();
            if (satchel == null || satchel.Has(chapter)) return;
            Pointer holding = null;
            foreach (var p in Pointer.All)
                if (p.Source != null && p.Source.Trigger && ReferenceEquals(p.Hovered, _pointable)) holding = p;
            _held = holding != null ? _held + Time.deltaTime : 0f;
            if (holding == null) _arcShown = -1f;
            if (_holdRing != null)
            {
                _holdRing.gameObject.SetActive(holding != null);
                if (holding != null)
                {
                    var eye = Camera.main != null ? Camera.main.transform.position : holding.Source.Aim.position;
                    var at = holding.HitPoint + (eye - holding.HitPoint).normalized * 0.02f;
                    _holdRing.SetPositionAndRotation(at, Quaternion.LookRotation(at - eye, Vector3.up));
                    _holdRing.localScale = Vector3.one * Mathf.Max(1f, Vector3.Distance(eye, at) / 2f);   // ~20 cm across at 2 m
                    var f = Mathf.Clamp01(_held / HoldSeconds);
                    if (Mathf.Abs(f - _arcShown) > 0.01f) { Arc(f, 0.08f, 0.1f, _arc); _arcShown = f; }
                }
            }
            if (holding != null && Mathf.Repeat(_held, 0.25f) < Time.deltaTime) holding.Source.Buzz(0.08f, 0.02f);   // a ticking build-up
            if (_held >= HoldSeconds)
            {
                _held = 0f;
                if (_holdRing != null) _holdRing.gameObject.SetActive(false);
                if (_glow != null) _glow.SetActive(false);
                StartCoroutine(Sweep(holding));
            }
        }

        System.Collections.IEnumerator Sweep(Pointer by)
        {
            _sweeping = true;
            var rs = GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(transform.position, Vector3.one);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            // The band: a thin glowing gold slab the width of the work, from its top to its bottom.
            var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
            band.name = "Replicate sweep";
            Destroy(band.GetComponent<Collider>());
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", new Color(1f, 0.82f, 0.4f, 0.75f));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            band.GetComponent<Renderer>().sharedMaterial = m;
            band.transform.localScale = new Vector3(b.size.x * 1.05f, Mathf.Max(0.02f, b.size.y * 0.03f), b.size.z * 1.05f + 0.02f);
            for (float t = 0f; t < SweepSeconds; t += Time.deltaTime)
            {
                band.transform.position = new Vector3(b.center.x, Mathf.Lerp(b.max.y, b.min.y, t / SweepSeconds), b.center.z);
                yield return null;
            }
            Destroy(band);
            var satchel = Satchel.Get();
            if (satchel != null) satchel.Add(copySource != null ? copySource : gameObject, label, chapter, b.center,
                                              string.IsNullOrEmpty(replicaName) ? label : replicaName);
            by?.Source?.Buzz(0.5f, 0.12f);
            _sweeping = false;
        }

        Bounds LocalBounds()
        {
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var b = new Bounds(transform.InverseTransformPoint(rs[0].bounds.center), Vector3.zero);
            foreach (var r in rs)
            {
                var rb = r.bounds;
                for (var i = 0; i < 8; i++)
                    b.Encapsulate(transform.InverseTransformPoint(rb.center + Vector3.Scale(rb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            }
            return b;
        }

        static Material Additive(Color c, Texture2D tex)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        static Texture2D _feather;

        /// <summary>A soft-edged rectangle: bright inside, fading to nothing at every edge.</summary>
        static Texture2D Feather()
        {
            if (_feather != null) return _feather;
            const int n = 64;
            _feather = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = Mathf.Min(x, n - 1 - x) / (n * 0.18f), v = Mathf.Min(y, n - 1 - y) / (n * 0.18f);
                var a = (byte)(Mathf.Clamp01(Mathf.Min(u, v)) * 255f);
                px[y * n + x] = new Color32(a, a, a, a);
            }
            _feather.SetPixels32(px); _feather.Apply();
            return _feather;
        }
    }
}
