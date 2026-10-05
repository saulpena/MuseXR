using System.Collections.Generic;
using MuseXR.UI;
using TMPro;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// What the ray is on, shown on the thing itself (Saul, 5 Oct: "if I click on something I am not sure what it was
    /// or what caused it"). Gold corner brackets frame anything pointable that has a name (a master, a painting, a
    /// lantern), the name sits above it, and both flash on the trigger so a click always shows what it hit. Faces the
    /// visitor and keeps one apparent size at any distance. Panels and chips have no name, so they get no brackets.
    /// </summary>
    public sealed class HoverCue : MonoBehaviour
    {
        public const float FlashSeconds = 0.3f;
        /// <summary>Cap height of the name, as a fraction of the distance (~1.3 degrees).</summary>
        public const float NameAngularSize = 0.012f;
        static readonly Color Idle = new Color(1f, 0.86f, 0.5f), Flashing = Color.white;

        readonly LineRenderer[] _corners = new LineRenderer[4], _shadows = new LineRenderer[4];
        Material _shadowMat;
        /// <summary>Under every world-space panel (MuseUi canvases sort at 10): over the world, never over the UI
        /// (Saul, 5 Oct, headset: "the hover state draws on top of the UI").</summary>
        public const int SortingOrder = 4;
        readonly List<Collider> _colliders = new List<Collider>();
        TextMeshPro _name;
        Material _mat;
        Component _target;
        float _flash;

        /// <summary>The name to show for <paramref name="p"/>, or null: its own label, else the work it stands for.</summary>
        public static string NameOf(IPointable p)
        {
            if (p is Pointable plain && !string.IsNullOrEmpty(plain.Label)) return plain.Label;
            if (p is Component c)
            {
                var art = c.GetComponentInParent<InsightTarget>();
                if (art != null && !string.IsNullOrEmpty(art.title)) return art.title;
            }
            return null;
        }

        public void Show(IPointable p)
        {
            var name = p != null ? NameOf(p) : null;
            _target = string.IsNullOrEmpty(name) ? null : p as Component;
            if (_target == null) { SetVisible(false); return; }
            Build();
            _colliders.Clear();
            foreach (var col in _target.GetComponentsInChildren<Collider>()) if (col.enabled) _colliders.Add(col);
            _name.text = name;
            _flash = 0f;
            SetVisible(_colliders.Count > 0);
        }

        /// <summary>The trigger landed on it: a short white flash on the brackets and the name.</summary>
        public void Flash() { if (_target != null) _flash = FlashSeconds; }

        void Build()
        {
            if (_name != null) return;
            // Drawn over everything (her pointer outline is never hidden by what stands in front of it): UI/Default
            // takes the GUI depth-test switch, URP's Unlit has none.
            _mat = new Material(Shader.Find("UI/Default"));
            _mat.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            _mat.renderQueue = 4000;
            _mat.color = Idle;
            _shadowMat = new Material(_mat) { color = new Color(0.08f, 0.05f, 0.02f, 0.75f) };
            LineRenderer Line(string n, Material m, int order)
            {
                var go = new GameObject(n);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true; lr.positionCount = 3; lr.sharedMaterial = m;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
                lr.numCapVertices = 2; lr.numCornerVertices = 2;
                lr.sortingOrder = order;
                return lr;
            }
            // A dark edge under each gold corner, so it reads on a pale wall as well as on the dark ground.
            for (var i = 0; i < 4; i++) { _shadows[i] = Line("Hover edge " + i, _shadowMat, SortingOrder); _corners[i] = Line("Hover corner " + i, _mat, SortingOrder + 1); }
            var label = new GameObject("Hover name");
            label.transform.SetParent(transform, false);
            _name = label.AddComponent<TextMeshPro>();
            var fonts = MuseFonts.Get();
            if (fonts != null && fonts.sansSemi != null) _name.font = fonts.sansSemi;
            _name.alignment = TextAlignmentOptions.Bottom;
            _name.enableWordWrapping = false;
            _name.characterSpacing = 4f;
            _name.color = Idle;
            _name.outlineWidth = 0.22f; _name.outlineColor = new Color32(20, 14, 6, 230);
            _name.rectTransform.sizeDelta = new Vector2(8f, 1f);
            _name.rectTransform.pivot = new Vector2(0.5f, 0f);   // its bottom edge sits on the frame's top
            var m = _name.fontMaterial;   // an instance: the over-everything switch must not reach every label
            m.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            m.renderQueue = 4001;
            // A soft dark halo behind the letters: legible on sky, wall or a painting.
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", new Color(0.05f, 0.03f, 0.01f, 0.85f));
            m.SetFloat("_UnderlayDilate", 0.9f); m.SetFloat("_UnderlaySoftness", 0.7f);
            _name.GetComponent<MeshRenderer>().sortingOrder = SortingOrder + 2;
        }

        void SetVisible(bool on)
        {
            if (_name == null) return;
            foreach (var c in _corners) c.enabled = on;
            foreach (var c in _shadows) c.enabled = on;
            _name.gameObject.SetActive(on);
        }

        void LateUpdate()
        {
            if (_name == null || !_name.gameObject.activeSelf) return;
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (_target == null || cam == null) { SetVisible(false); return; }
            var any = false; var b = new Bounds();
            foreach (var col in _colliders)
            {
                if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;
                if (!any) { b = col.bounds; any = true; } else b.Encapsulate(col.bounds);
            }
            if (!any) { SetVisible(false); return; }

            // A rectangle facing the visitor at the front of the bounds: world up, and the camera's flat right.
            var toward = b.center - cam.position;
            var dist = Mathf.Max(0.5f, toward.magnitude);
            var fwd = toward / dist;
            var right = Vector3.Cross(Vector3.up, fwd); right = right.sqrMagnitude > 1e-6f ? right.normalized : cam.right;
            var up = Vector3.Cross(fwd, right).normalized;
            float hx = 0f, hy = 0f, hz = 0f;
            for (var i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var d = corner - b.center;
                hx = Mathf.Max(hx, Mathf.Abs(Vector3.Dot(d, right)));
                hy = Mathf.Max(hy, Mathf.Abs(Vector3.Dot(d, up)));
                hz = Mathf.Max(hz, Mathf.Abs(Vector3.Dot(d, fwd)));
            }
            var pad = 0.015f * dist;
            hx += pad; hy += pad;
            var front = b.center;   // at the object's own depth: pulled toward the eye it read far larger than the object

            _flash = Mathf.Max(0f, _flash - Time.deltaTime);
            var k = _flash / FlashSeconds;
            var colour = Color.Lerp(Idle, Flashing, k);
            _mat.color = colour;
            var width = 0.006f * dist * (1f + k);
            var arm = Mathf.Min(hx, hy) * 0.35f;
            for (var i = 0; i < 4; i++)
            {
                var sx = (i & 1) == 0 ? -1f : 1f; var sy = (i & 2) == 0 ? -1f : 1f;
                var c = front + right * (sx * hx) + up * (sy * hy);
                var lr = _corners[i];
                lr.startWidth = lr.endWidth = width;
                lr.SetPosition(0, c - right * (sx * arm));
                lr.SetPosition(1, c);
                lr.SetPosition(2, c - up * (sy * arm));
                var sh = _shadows[i];
                sh.startWidth = sh.endWidth = width * 2.4f;
                for (var j = 0; j < 3; j++) sh.SetPosition(j, lr.GetPosition(j));
            }

            // The name above the top edge, one apparent size at any distance; +Z away from the visitor reads correctly.
            _name.fontSize = 0.6f * (NameAngularSize * dist / 0.016f);
            _name.color = colour;
            var at = front + up * (hy + 0.012f * dist);
            _name.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - cam.position, up));
        }
    }
}
