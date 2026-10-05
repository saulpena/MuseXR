using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her satchel: "the ring on your left wrist" that holds the small copies you replicate of each
    /// chapter's hero work (MUSE-VR-design, hero works; one copy per chapter, each keeps its source).
    /// Her doc names only the ring; Saul's design for viewing it (4 Oct): a gold ring round the left
    /// wrist with a small button on top. Tap the button - touch it with the right hand, or point at it
    /// and pull the trigger - and every copy appears orbiting the wrist; tap again to put them away.
    /// Nothing can be taken out yet (not asked for, and her doc does not mention it).
    ///
    /// At a desk there is no left hand: the ring hangs at the lower left of the view, the mouse clicks
    /// its button, and I toggles it.
    /// </summary>
    public sealed class Satchel : MonoBehaviour
    {
        public const float RingRadius = 0.045f, ButtonSize = 0.026f, OrbitRadius = 0.16f, ItemSize = 0.07f;
        public const float OrbitDegreesPerSecond = 22f, SpinDegreesPerSecond = 40f, TouchRange = 0.035f;

        public readonly struct Item
        {
            public readonly GameObject Copy; public readonly string Label, Chapter, ReplicaName;
            public Item(GameObject copy, string label, string chapter, string replicaName)
            { Copy = copy; Label = label; Chapter = chapter; ReplicaName = replicaName; }
        }

        public IReadOnlyList<Item> Items => _items;
        public bool Open { get; private set; }
        public event System.Action<Item> Added;

        readonly List<Item> _items = new List<Item>();
        Transform _wrist, _ring, _button, _orbit;
        TMPro.TextMeshPro _count;
        Material _gold;
        float _orbitAngle, _touchCooldown;
        bool _desk;

        static Satchel _instance;

        public static Satchel Get()
        {
            if (_instance != null) return _instance;
            var cam = Camera.main;
            if (cam == null) return null;
            var origin = cam.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
            var host = new GameObject("Satchel");
            if (origin != null) host.transform.SetParent(origin.transform, false);
            _instance = host.AddComponent<Satchel>();
            _instance.Build();
            return _instance;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        /// <summary>True when a copy from <paramref name="chapter"/> is already in the satchel (one per chapter).</summary>
        public bool Has(string chapter)
        {
            foreach (var i in _items) if (i.Chapter == chapter) return true;
            return false;
        }

        /// <summary>Put a small copy of <paramref name="source"/> in the satchel: the same model, scaled down.</summary>
        public bool Add(GameObject source, string label, string chapter) => Add(source, label, chapter, null, label);

        /// <summary>Put a small copy in the satchel, flying it from <paramref name="from"/> to the wrist when given.</summary>
        public bool Add(GameObject source, string label, string chapter, Vector3? from, string replicaName)
        {
            if (source == null || Has(chapter)) return false;
            var copy = Instantiate(source);
            copy.name = "Satchel · " + label;
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var b in copy.GetComponentsInChildren<MonoBehaviour>(true)) if (!(b is TMPro.TMP_Text)) Destroy(b);
            foreach (var g in copy.GetComponentsInChildren<Transform>(true)) if (g.name.StartsWith("Replicate")) Destroy(g.gameObject);
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.materials;   // per-copy instances
                foreach (var m in mats) if (m != null && m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
                r.materials = mats;
            }
            copy.transform.SetParent(_orbit, false);
            Fit(copy.transform, ItemSize);
            var item = new Item(copy, label, chapter, replicaName);
            _items.Add(item);
            Layout();
            if (from.HasValue) StartCoroutine(Fly(copy.transform, from.Value));
            else { copy.SetActive(Open); Arrived(); }
            Added?.Invoke(item);
            return true;
        }

        /// <summary>The copy lifts off the work and shrinks into the wrist over FlySeconds, then the button pulses.</summary>
        System.Collections.IEnumerator Fly(Transform copy, Vector3 from)
        {
            var home = copy.localPosition; var small = copy.localScale;
            copy.SetParent(null, true);
            copy.gameObject.SetActive(true);
            var start = from; var big = small * 4f;
            for (float t = 0f; t < FlySeconds; t += Time.deltaTime)
            {
                var k = Mathf.SmoothStep(0f, 1f, t / FlySeconds);
                var to = _wrist.position;
                copy.position = Vector3.Lerp(start, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.25f;   // a little arc
                copy.localScale = Vector3.Lerp(big, small, k);
                var cam = Camera.main;
                if (cam != null) copy.rotation = Quaternion.LookRotation(copy.position - cam.transform.position, Vector3.up);
                yield return null;
            }
            copy.SetParent(_orbit, false);
            copy.localPosition = home; copy.localScale = small;
            copy.gameObject.SetActive(Open);
            Arrived();
        }

        void Arrived()
        {
            _count.text = _items.Count.ToString();
            StartCoroutine(Pulse());
        }

        System.Collections.IEnumerator Pulse()
        {
            var baseScale = new Vector3(ButtonSize, 0.004f, ButtonSize);
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                var s = 1f + 0.45f * Mathf.Sin(t / 0.5f * Mathf.PI);
                _button.localScale = new Vector3(baseScale.x * s, baseScale.y, baseScale.z * s);
                yield return null;
            }
            _button.localScale = baseScale * 1f;
        }

        public const float FlySeconds = 0.7f;

        public void Toggle()
        {
            Open = !Open;
            foreach (var i in _items) if (i.Copy != null) i.Copy.SetActive(Open);
            _button.localScale = Vector3.one * (Open ? 0.9f : 1f);
            foreach (var p in Pointer.All) if (p.Source != null && p.Source.Hand == Hand.Left) p.Source.Buzz(0.25f, 0.05f);
        }

        void Build()
        {
            _gold = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _gold.SetColor("_BaseColor", new Color(0.83f, 0.66f, 0.32f));
            _gold.SetFloat("_Metallic", 0.85f); _gold.SetFloat("_Smoothness", 0.7f);

            _wrist = new GameObject("Wrist").transform;
            _wrist.SetParent(transform, false);
            _ring = new GameObject("Ring").transform;
            _ring.SetParent(_wrist, false);
            _ring.gameObject.AddComponent<MeshFilter>().sharedMesh = Torus(RingRadius, 0.006f);
            _ring.gameObject.AddComponent<MeshRenderer>().sharedMaterial = _gold;

            // The button sits on top of the wrist: a small gold disc with the count on it.
            var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            b.name = "Satchel Button";
            Destroy(b.GetComponent<Collider>());
            _button = b.transform;
            _button.SetParent(_wrist, false);
            _button.localPosition = new Vector3(0f, RingRadius + 0.004f, 0f);
            _button.localScale = new Vector3(ButtonSize, 0.004f, ButtonSize);
            b.GetComponent<Renderer>().sharedMaterial = _gold;
            var face = new GameObject("Face").transform;
            face.SetParent(_wrist, false);
            face.localPosition = _button.localPosition + Vector3.up * 0.0045f;
            face.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _count = face.gameObject.AddComponent<TMPro.TextMeshPro>();
            _count.text = "0"; _count.fontSize = 0.12f; _count.alignment = TMPro.TextAlignmentOptions.Center;
            _count.color = new Color(0.16f, 0.12f, 0.09f);
            _count.rectTransform.sizeDelta = new Vector2(0.03f, 0.02f);
            var hit = new GameObject("Button Target").transform;   // a generous target for the ray and the mouse
            hit.SetParent(_wrist, false);
            hit.localPosition = _button.localPosition;
            var box = hit.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(0.045f, 0.03f, 0.045f);
            var p = Pointable.Make(hit.gameObject, "satchel");
            p.Selected += (_, __) => Toggle();

            _orbit = new GameObject("Orbit").transform;
            _orbit.SetParent(_wrist, false);
        }

        void LateUpdate()
        {
            Place();
            _orbitAngle += OrbitDegreesPerSecond * Time.deltaTime;
            _orbit.localRotation = Quaternion.Euler(0f, _orbitAngle, 0f);
            foreach (var i in _items)
                if (i.Copy != null) i.Copy.transform.Rotate(Vector3.up, SpinDegreesPerSecond * Time.deltaTime, Space.Self);

            // Touch: the right hand's controller comes within TouchRange of the button.
            _touchCooldown -= Time.deltaTime;
            foreach (var p in Pointer.All)
            {
                if (p.Source == null || p.Source.Hand != Hand.Right || p.Source.Aim == null || _desk) continue;
                if (_touchCooldown <= 0f && Vector3.Distance(p.Source.Aim.position, _button.position) < TouchRange)
                {
                    Toggle(); _touchCooldown = 0.8f; p.Source.Buzz(0.3f, 0.04f);
                }
            }
            if (_desk && Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame) Toggle();
        }

        /// <summary>On the left controller, just behind the hand; at a desk, at the lower left of the view.</summary>
        void Place()
        {
            Transform left = null;
            foreach (var p in Pointer.All) if (p.Source != null && p.Source.Hand == Hand.Left && p.Source.Aim != null) left = p.Source.Aim;
            _desk = left == null || !UnityEngine.XR.XRSettings.isDeviceActive;
            var cam = Camera.main;
            if (!_desk)
            {
                // Saul, 4 Oct: a giant gold ring through his head. Until the left controller is tracked its pose sits
                // at the rig origin - the head - so the wrist only shows on a tracked hand clear of the face.
                var tracked = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand)
                    .TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out var t0) && t0;
                var clear = cam == null || Vector3.Distance(left.position, cam.transform.position) > 0.25f;
                Show(tracked && clear);
                // The ring round the wrist: its axis along the forearm (the controller's forward).
                _wrist.SetPositionAndRotation(left.position - left.forward * 0.07f, left.rotation * Quaternion.Euler(90f, 0f, 0f));
                return;
            }
            Show(Open);   // at a desk there is no wrist to look at: it shows only while the satchel is open (I)
            if (cam == null) return;
            var t = cam.transform;
            // Lower left, clear of the waist panel's compass in the middle (they overlapped at 0.2 m left).
            _wrist.SetPositionAndRotation(t.position + t.forward * 0.45f - t.right * 0.34f - t.up * 0.16f,
                                          Quaternion.LookRotation(t.forward, t.up) * Quaternion.Euler(-35f, 0f, 0f));
        }

        void Show(bool on)
        {
            if (_wrist != null && _wrist.gameObject.activeSelf != on) _wrist.gameObject.SetActive(on);
        }

        void Layout()
        {
            for (var k = 0; k < _items.Count; k++)
            {
                var a = k * Mathf.PI * 2f / Mathf.Max(1, _items.Count);
                _items[k].Copy.transform.localPosition = new Vector3(Mathf.Cos(a) * OrbitRadius, 0.03f, Mathf.Sin(a) * OrbitRadius);
            }
        }

        static void Fit(Transform t, float size)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) { t.localScale = Vector3.one * size; return; }
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var big = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (big > 1e-5f) t.localScale *= size / big;
            t.localRotation = Quaternion.identity;
        }

        static Mesh Torus(float radius, float tube)
        {
            const int n = 48, m = 10;
            var v = new Vector3[n * m]; var tris = new List<int>();
            for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
            {
                float a = i * Mathf.PI * 2f / n, b = j * Mathf.PI * 2f / m;
                var c = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * m + j] = c * (radius + Mathf.Cos(b) * tube) + Vector3.up * Mathf.Sin(b) * tube;
                int i2 = (i + 1) % n, j2 = (j + 1) % m;
                tris.AddRange(new[] { i * m + j, i2 * m + j, i * m + j2, i2 * m + j, i2 * m + j2, i * m + j2 });
            }
            var mesh = new Mesh { vertices = v, triangles = tris.ToArray(), name = "Satchel ring" };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
