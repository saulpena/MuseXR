using System.Collections;
using MuseXR.UI;
using TMPro;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her way into Your world (MUSE-VR-design, storyboard E and Monet step 8): once the answer is kept at
    /// the roundtable, "a small world rises from the centre of the table with your pieces already inside.
    /// Point at its little door and confirm Enter" - then only a fade (0.3 s), "landing on solid ground at
    /// the corridor start". No flythrough.
    ///
    /// The crossing itself is the Monet chapter's own exit gate (ChapterExit, linked to Your world by a
    /// ChapterLink in GateWorld): Enter opens it under the fade and carries the visitor through it, so
    /// everything Your world does on arrival happens exactly as on a walk through a gate.
    /// </summary>
    public sealed class YourWorldMiniature : MonoBehaviour, IConfirmable
    {
        public const float RiseSeconds = 2.2f, FadeSeconds = 0.3f, Radius = 0.42f, Above = 1.1f, Size = 1.2f, InFront = 1.1f;   // chest height and a little larger: at table height it vanished in the splat floaters around the rotunda
        const string YourWorldKey = "fantasy-realm-of-shimmering-spheres";

        public static YourWorldMiniature Current { get; private set; }

        Vector3 _top;
        bool _entering;

        /// <summary>Raise the miniature at the table (its centre, on the floor). Once per walk.</summary>
        public static YourWorldMiniature Rise(Transform table)
        {
            if (Current != null || table == null) return Current;
            var go = new GameObject("Your World Miniature");
            go.transform.SetParent(table, false);
            Current = go.AddComponent<YourWorldMiniature>();
            // Between the visitor and the table, not over it: the companions stand at the table and the
            // rotunda sits in splat floaters, and over the table both hid it (blind review, 4 Oct 2026).
            var eye = Camera.main != null ? Camera.main.transform.position : table.position + Vector3.back * 2f;
            var toTable = table.position - eye; toTable.y = 0f;
            var near = toTable.magnitude > InFront + 0.6f ? eye + toTable.normalized * InFront : table.position;
            Current._top = new Vector3(near.x, table.position.y + Above, near.z);
            return Current;
        }

        void Start() { Build(); StartCoroutine(Rising()); }

        void Build()
        {
            // Her pink, sphere-filled world in miniature (1:20): a disc, a few soft spheres, the visitor's
            // own pieces from the satchel, and a little arched door facing the visitor.
            var pink = Lit(new Color32(0xf2, 0xb8, 0xc6, 0xff)); var cream = Lit(new Color32(0xf6, 0xe8, 0xd8, 0xff));
            var disc = Part(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0f), new Vector3(Radius * 2f, 0.03f, Radius * 2f), pink, "Ground");
            var rnd = new System.Random(7);
            for (var i = 0; i < 9; i++)
            {
                var a = i * 0.7f; var r = 0.12f + 0.25f * (float)rnd.NextDouble(); var s = 0.04f + 0.06f * (float)rnd.NextDouble();
                Part(PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * r, 0.03f + s * 0.5f, Mathf.Sin(a) * r), Vector3.one * s, i % 2 == 0 ? pink : cream, "Sphere");
            }
            var satchel = FindAnyObjectByType<Satchel>();
            if (satchel != null)
                for (var i = 0; i < satchel.Items.Count; i++)
                {
                    var item = satchel.Items[i];
                    if (item.Copy == null) continue;
                    var copy = Instantiate(item.Copy, transform);
                    copy.SetActive(true);
                    foreach (var b in copy.GetComponentsInChildren<Behaviour>(true)) if (!(b is Light)) b.enabled = false;
                    var x = (i % 2 == 0 ? -1f : 1f) * 0.2f; var z = -0.15f + 0.1f * i;
                    copy.transform.localPosition = new Vector3(x, 0.03f, z);
                    copy.transform.localRotation = Quaternion.Euler(0f, x < 0f ? 90f : -90f, 0f);
                    copy.transform.localScale = item.Copy.transform.lossyScale * 1.2f;   // ~15 cm: 3 m at 1:20
                }
            // The door, toward the visitor.
            var eye = Camera.main != null ? Camera.main.transform.position : transform.position + Vector3.back;
            var toEye = eye - _top; toEye.y = 0f; toEye = toEye.sqrMagnitude > 1e-4f ? toEye.normalized : Vector3.back;
            transform.rotation = Quaternion.LookRotation(-toEye, Vector3.up);   // the door is at local -Z
            var gold = Lit(new Color32(0xc9, 0xaa, 0x72, 0xff), 0.6f);
            var door = new GameObject("Door").transform; door.SetParent(transform, false); door.localPosition = new Vector3(0f, 0.03f, -Radius * 0.85f);
            PartUnder(door, PrimitiveType.Cube, new Vector3(-0.07f, 0.09f, 0f), new Vector3(0.025f, 0.18f, 0.025f), gold);
            PartUnder(door, PrimitiveType.Cube, new Vector3(0.07f, 0.09f, 0f), new Vector3(0.025f, 0.18f, 0.025f), gold);
            PartUnder(door, PrimitiveType.Cube, new Vector3(0f, 0.19f, 0f), new Vector3(0.165f, 0.025f, 0.025f), gold);
            var glow = PartUnder(door, PrimitiveType.Quad, new Vector3(0f, 0.09f, 0.002f), new Vector3(0.115f, 0.17f, 1f), Unlit(new Color32(0xff, 0xe9, 0xc4, 0xff)));
            glow.localRotation = Quaternion.identity;
            var box = door.gameObject.AddComponent<BoxCollider>(); box.center = new Vector3(0f, 0.1f, 0f); box.size = new Vector3(0.3f, 0.3f, 0.12f);
            var p = Pointable.Make(door.gameObject, "enter your world");
            p.Selected += (_, __) => Confirm();

            // Her words over it.
            var rec = JourneyMemory.Record;
            var anchor = new GameObject("Label").transform; anchor.SetParent(transform, false);
            anchor.localPosition = new Vector3(0f, 0.42f, 0f);   // x Size 1.4: just above eye height
            anchor.rotation = Quaternion.LookRotation(-toEye, Vector3.up);
            var c = MuseUi.Canvas(anchor, "Miniature", 1.8f, 300f);
            var card = MuseUi.Card(c, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Gold, 1f, padX: 12f, padY: 9f, gap: 3f, name: "Card");
            card.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var t = MuseUi.Text(card, "Your world" + (string.IsNullOrWhiteSpace(rec.WorldTitle) ? "" : "  ·  " + rec.WorldTitle.Trim()), MuseUi.Face.Serif, 15f, MuseTheme.Ink, name: "Title");
            t.alignment = TextAlignmentOptions.Center;
            var h = MuseUi.Text(card, "Point at its door and press A to enter", MuseUi.Face.Sans, 10f, MuseTheme.Ink3, name: "Hint");
            h.alignment = TextAlignmentOptions.Center;
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;   // shown as it rises
        }

        IEnumerator Rising()
        {
            var start = _top - Vector3.up * 0.6f;
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;
            for (float t = 0f; t < RiseSeconds; t += Time.deltaTime)
            {
                var k = Mathf.SmoothStep(0f, 1f, t / RiseSeconds);
                transform.position = Vector3.Lerp(start, _top, k);
                transform.localScale = Vector3.one * Mathf.Lerp(0.2f, Size, k);
                yield return null;
            }
            transform.position = _top; transform.localScale = Vector3.one * Size;
            if (ConfirmInput.Focus == null) ConfirmInput.Take(this);
        }

        /// <summary>A, or the trigger on its door: enter.</summary>
        public bool Confirm()
        {
            if (_entering) return false;
            _entering = true;
            ConfirmInput.Drop(this);
            // Off the table first: the table is in the Monet frame, which ChapterLink destroys on arrival, and
            // with it this coroutine - leaving the fade black on the visitor's eye for good.
            transform.SetParent(null, true);
            StartCoroutine(Enter());
            return true;
        }

        public bool Redo() => false;

        IEnumerator Enter()
        {
            ChapterExit exit = null;
            foreach (var e in FindObjectsByType<ChapterExit>(FindObjectsSortMode.None))
                if (e.gate != null && e.gate.nextWorldKey != null && e.gate.nextWorldKey.StartsWith(YourWorldKey)) exit = e;
            if (exit == null) { Debug.LogWarning("[YourWorld] no exit to Your world in this scene"); _entering = false; yield break; }
            var fade = Fade.Over(Camera.main != null ? Camera.main.transform : null, this);
            yield return fade.To(1f, FadeSeconds);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;   // it stays behind in the Monet frame
            exit.Complete();
            var gate = exit.gate;
            for (float t = 0f; t < 5f && (gate.Door == null || gate.Door.Phase != MuseXR.Worlds.PortalPhase.Open); t += Time.deltaTime) yield return null;
            // Through the gate plane in one step: the door sees the crossing, the link does the rest.
            var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            var head = origin != null && origin.Camera != null ? origin.Camera.transform : null;
            if (origin != null && head != null)
            {
                var body = origin.GetComponent<CharacterController>();
                if (body != null) body.enabled = false;
                var local = gate.transform.InverseTransformPoint(head.position);
                origin.MoveCameraToWorldLocation(gate.transform.TransformPoint(new Vector3(0f, local.y, -0.6f)));
                Physics.SyncTransforms();
                yield return null;
                origin.MoveCameraToWorldLocation(gate.transform.TransformPoint(new Vector3(0f, local.y, MuseXR.Worlds.MoonGate.ArrivalPastDoor + 0.4f)));
                Physics.SyncTransforms();
                if (body != null) body.enabled = true;
            }
            for (float t = 0f; t < 6f && gate != null && !(gate.Door != null && gate.Door.IsDone); t += Time.deltaTime) yield return null;
            yield return null; yield return null;
            yield return fade.To(0f, FadeSeconds);
            Destroy(fade.gameObject);
            if (Current == this) Current = null;
            if (this != null) Destroy(gameObject);
        }

        Transform Part(PrimitiveType type, Vector3 local, Vector3 scale, Material m, string name) => PartUnder(transform, type, local, scale, m, name);

        static Transform PartUnder(Transform parent, PrimitiveType type, Vector3 local, Vector3 scale, Material m, string name = "Part")
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        static Material Lit(Color c, float smooth = 0.3f)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
            // A faint glow of its own: the Monet garden is dim, and lit only by the scene it read as a dark clump.
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 0.12f);   // 0.45 blew the pink out to white
            return m;
        }

        static Material Unlit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            return m;
        }

        /// <summary>Her only transition: a 0.3 s fade, a black quad just in front of the eye.</summary>
        sealed class Fade : MonoBehaviour
        {
            Material _m;
            MonoBehaviour _owner;

            public static Fade Over(Transform eye, MonoBehaviour owner)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Fade";
                Destroy(q.GetComponent<Collider>());
                if (eye != null) q.transform.SetParent(eye, false);
                q.transform.localPosition = new Vector3(0f, 0f, 0.12f);
                q.transform.localRotation = Quaternion.identity;
                q.transform.localScale = new Vector3(1f, 1f, 1f);
                var f = q.AddComponent<Fade>();
                f._owner = owner;
                f._m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                f._m.SetFloat("_Surface", 1f); f._m.SetFloat("_Blend", 0f);
                f._m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                f._m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                f._m.SetFloat("_ZWrite", 0f); f._m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                f._m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");   // CLAUDE.md: the blend floats alone do not do it
                f._m.renderQueue = 4000;
                f._m.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
                q.GetComponent<Renderer>().sharedMaterial = f._m;
                return f;
            }

            // Whoever raised it is gone mid-transition: never leave the visitor in the black.
            void Update() { if (_owner == null) Destroy(gameObject); }

            public IEnumerator To(float alpha, float seconds)
            {
                var from = _m.GetColor("_BaseColor").a;
                for (float t = 0f; t < seconds; t += Time.deltaTime)
                {
                    _m.SetColor("_BaseColor", new Color(0f, 0f, 0f, Mathf.Lerp(from, alpha, t / seconds)));
                    yield return null;
                }
                _m.SetColor("_BaseColor", new Color(0f, 0f, 0f, alpha));
            }
        }
    }
}
