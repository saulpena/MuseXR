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
        /// <summary>The round table's top above its foot (the Monet rotunda's table, 0.76 m).</summary>
        public const float TableTop = 0.76f;
        public const float RiseSeconds = 2.2f, FadeSeconds = 0.3f, Radius = 0.42f, Above = 1.1f, Size = 1.2f, InFront = 1.1f;   // chest height and a little larger: at table height it vanished in the splat floaters around the rotunda
        const string YourWorldKey = "fantasy-realm-of-shimmering-spheres";

        public static YourWorldMiniature Current { get; private set; }

        /// <summary>Her regret path: B at the miniature - it sinks back into the table and the answer is open again.</summary>
        public static event System.Action Regretted;

        Vector3 _top;
        bool _entering;

        /// <summary>Raise the miniature at the table (its centre, on the floor). Once per walk.</summary>
        public static YourWorldMiniature Rise(Transform table)
        {
            if (Current != null || table == null) return Current;
            var go = new GameObject("Your World Miniature");
            go.transform.SetParent(table, false);
            Current = go.AddComponent<YourWorldMiniature>();
            // On the table, resting on its top (Saul, 5 Oct: it floated in front of the table, "not lined up with
            // it"). Her "a small world rises from the centre of the table".
            Current._top = table.position + Vector3.up * TableTop;
            return Current;
        }

        float _modelTop;

        void Start() { Build(); StartCoroutine(Rising()); }

        void Build()
        {
            // Her pink, sphere-filled world in miniature (1:20): the generated diorama - a pink cushion, pearl spheres
            // and a little gold arch (Saul, 4 Oct: no shapes made in code) - with the visitor's own pieces on it.
            // Built at scale 1, in the miniature's own units, before it rises to its Size.
            var scaleWas = transform.localScale; var rotWas = transform.rotation;
            transform.localScale = Vector3.one; transform.rotation = Quaternion.identity;
            // Its arch is at its back (-Z): turned so the arch stands on the visitor's side, where the door is.
            var model = PropModels.Spawn("miniature", transform, transform.position, transform.rotation, new Vector3(Radius * 2f, 0f, Radius * 2f));
            var mesh = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
            var probe = mesh != null ? mesh.gameObject.AddComponent<MeshCollider>() : null;   // on the mesh's own node, so it sits where the mesh draws
            Physics.SyncTransforms();
            // The cushion's top under a point of the miniature (local x, z): where a piece stands.
            float Surface(float x, float z)
            {
                var from = transform.TransformPoint(new Vector3(x, 1f, z));
                return probe != null && probe.Raycast(new Ray(from, Vector3.down), out var hit, 2f) ? transform.InverseTransformPoint(hit.point).y : 0.03f;
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
                    copy.transform.localPosition = new Vector3(x, Surface(x, z), z);
                    copy.transform.localRotation = Quaternion.Euler(0f, x < 0f ? 90f : -90f, 0f);
                    copy.transform.localScale = item.Copy.transform.lossyScale * 1.2f;   // ~15 cm: 3 m at 1:20
                }
            if (model != null) _modelTop = transform.InverseTransformPoint(PropModels.Bounds(model).max).y;
            if (probe != null) Destroy(probe);
            transform.localScale = scaleWas; transform.rotation = rotWas;
            // The door - the model's arch - toward the visitor.
            var eye = Camera.main != null ? Camera.main.transform.position : transform.position + Vector3.back;
            var toEye = eye - _top; toEye.y = 0f; toEye = toEye.sqrMagnitude > 1e-4f ? toEye.normalized : Vector3.back;
            transform.rotation = Quaternion.LookRotation(-toEye, Vector3.up);   // the arch is at local -Z
            var door = new GameObject("Door").transform; door.SetParent(transform, false); door.localPosition = new Vector3(0f, 0.03f, -Radius * 0.85f);
            var box = door.gameObject.AddComponent<BoxCollider>(); box.center = new Vector3(0f, 0.2f, 0f); box.size = new Vector3(0.4f, 0.5f, 0.2f);
            var p = Pointable.Make(door.gameObject, "enter your world");
            p.Selected += (_, __) => Confirm();

            // Her words over it.
            var rec = JourneyMemory.Record;
            var anchor = new GameObject("Label").transform; anchor.SetParent(transform, false);
            // Clear above the model's arch (0.64 high in these units; at 0.42 the label cut across it, 5 Oct).
            // Well clear of the arch: seen from standing height, close, the arch still crossed it at +0.14 (5 Oct).
            anchor.localPosition = new Vector3(0f, Mathf.Max(0.5f, _modelTop + 0.32f), 0f);
            anchor.rotation = Quaternion.LookRotation(-toEye, Vector3.up);
            var c = MuseUi.Canvas(anchor, "Miniature", 1.8f, 300f);
            var card = MuseUi.Card(c, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Gold, 1f, padX: 12f, padY: 9f, gap: 3f, name: "Card");
            card.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var t = MuseUi.Text(card, "Your world" + (string.IsNullOrWhiteSpace(rec.WorldTitle) ? "" : "  ·  " + rec.WorldTitle.Trim()), MuseUi.Face.Serif, 15f, MuseTheme.Ink, name: "Title");
            t.alignment = TextAlignmentOptions.Center;
            var h = MuseUi.Text(card, "Point at its door and press A to enter  ·  B back to your answer", MuseUi.Face.Sans, 10f, MuseTheme.Ink3, name: "Hint");
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

        /// <summary>
        /// B: not yet. The miniature sinks back into the table and the answer opens again with its draft and rewrite
        /// (her "regret path: at the miniature stage you can go back and edit"). Never once the visitor is entering.
        /// </summary>
        public bool Redo()
        {
            if (_entering || _sinking) return false;
            _sinking = true;
            ConfirmInput.Drop(this);
            StopAllCoroutines();
            StartCoroutine(Sinking());
            return true;
        }

        bool _sinking;

        IEnumerator Sinking()
        {
            var from = transform.position; var scale = transform.localScale.x;
            var to = _top - Vector3.up * 0.6f;
            const float seconds = 1.0f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                var k = Mathf.SmoothStep(0f, 1f, t / seconds);
                transform.position = Vector3.Lerp(from, to, k);
                transform.localScale = Vector3.one * Mathf.Lerp(scale, 0.2f, k);
                yield return null;
            }
            if (Current == this) Current = null;
            Debug.Log("[Miniature] back to the answer");
            Regretted?.Invoke();
            Destroy(gameObject);
        }

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
