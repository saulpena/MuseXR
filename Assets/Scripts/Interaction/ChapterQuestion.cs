using MuseXR.UI;
using TMPro;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her "each chapter shows its question at entry" (MUSE-VR-design acceptance: the player knows why
    /// the question led here): on arriving, "Stop 3 · What can my feeling become as expression?" stands
    /// ahead of the visitor, above eye level, for a few seconds, then fades. Chained into GateWorld a
    /// chapter can be awake while its frame still stands behind a gate, so it waits until the frame is at
    /// the origin and the visitor is in it.
    /// </summary>
    public sealed class ChapterQuestion : MonoBehaviour
    {
        [Tooltip("Her stop label, e.g. 'Stop 3'. Empty for Your world.")]
        public string stop;
        [Tooltip("Her chapter question, e.g. 'What can my feeling become as expression?'")]
        public string question;

        public const float ShowSeconds = 8f, FadeSeconds = 1.2f, Ahead = 3.2f, Above = 0.55f;

        /// <summary>Her script's wording: "Stop 3 · question", or the question alone.</summary>
        public static string Line(string stop, string question) =>
            string.IsNullOrWhiteSpace(stop) ? question : stop.Trim() + "  ·  " + question;

        bool _shown;
        CanvasGroup _group;
        float _t;

        void Update()
        {
            if (!_shown)
            {
                if (transform.root.position.sqrMagnitude > 0.01f) return;   // still behind a gate
                var eye = Camera.main != null ? Camera.main.transform : null;
                if (eye == null) return;
                var d = eye.position - transform.root.position; d.y = 0f;
                if (d.magnitude > 30f) return;
                Show(eye);
                return;
            }
            if (_group == null) return;
            _t += Time.deltaTime;
            _group.alpha = _t < ShowSeconds ? Mathf.Clamp01(_t / 0.6f) : 1f - Mathf.Clamp01((_t - ShowSeconds) / FadeSeconds);
            if (_t > ShowSeconds + FadeSeconds) { Destroy(_group.transform.parent.gameObject); _group = null; }
        }

        void Show(Transform eye)
        {
            _shown = true;
            var fwd = eye.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            var anchor = new GameObject("Chapter Question").transform;
            anchor.SetParent(transform, true);
            var at = eye.position + fwd * Ahead + Vector3.up * Above;
            anchor.SetPositionAndRotation(at, Quaternion.LookRotation(fwd, Vector3.up));   // +Z away from the viewer reads
            var c = MuseUi.Canvas(anchor, "Question", Ahead, 640f);
            _group = c.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            var glass = MuseUi.Card(c, new Color32(8, 6, 10, 196), 0f, new Color32(238, 233, 223, 46), 1f, padX: 26f, padY: 18f, gap: 6f, name: "Glass");
            glass.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            if (!string.IsNullOrWhiteSpace(stop))
            {
                var k = MuseUi.Text(glass, stop.Trim().ToUpperInvariant(), MuseUi.Face.Sans, 15f, new Color32(158, 135, 170, 255), 0.28f, true, name: "Stop");
                k.alignment = TextAlignmentOptions.Center;
            }
            var q = MuseUi.Text(glass, question, MuseUi.Face.Serif, 42f, new Color32(238, 233, 223, 255), lineHeight: 1.1f, name: "Question");
            q.alignment = TextAlignmentOptions.Center;
            var fonts = MuseFonts.Get(); if (fonts != null && fonts.display != null) q.font = fonts.display;
            Debug.Log("[Chapter] " + Line(stop, question));
        }
    }
}
