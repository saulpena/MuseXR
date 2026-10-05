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
        [Tooltip("Stand it at a fixed point of the chapter (its frame's space), turned to the visitor, and keep it up - " +
                 "the Palace's above the throne statue. Off: ahead of the visitor's gaze on arrival, fading after a few seconds.")]
        public bool anchored;
        [Tooltip("The fixed point, in the chapter frame's space (the Palace: just above the statue's head).")]
        public Vector3 anchorPoint;
        [Tooltip("Size on top of the reading size from the arrival point (the Grotto's, as Saul set it: 1.317).")]
        public float anchorScale = 1f;

        // Further, higher and up for the whole chapter (Saul, 5 Oct: Stop 1 and Stop 2 showed for a few seconds 3 m
        // ahead and were easy to miss): the room's title, sized for reading from where the visitor arrives.
        public const float ShowSeconds = 8f, FadeSeconds = 1.2f, Ahead = 6f, Above = 1.5f;

        /// <summary>Her script's wording: "Stop 3 · question", or the question alone.</summary>
        public static string Line(string stop, string question) =>
            string.IsNullOrWhiteSpace(stop) ? question : stop.Trim() + "  ·  " + question;

        bool _shown;
        float _arrivedAt = -1f;

        /// <summary>Shown and faded away (what else waits for it, as Your world's memento does).</summary>
        // It no longer fades away, so "done" is "up long enough to have been read" (Your world's memento waits for it).
        public bool Done => _shown && _t >= ShowSeconds;
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
                if (d.magnitude > 60f) return;   // Monet's spawn is 30.5 m from its origin: at 30 its question never showed
                // A second after arriving: the chapter builds its pieces (Monet's Great Wave) a frame or two after it
                // wakes, and a title placed before they exist cannot see them to stand clear of them.
                if (_arrivedAt < 0f) { _arrivedAt = Time.time; return; }
                if (Time.time - _arrivedAt < 1f) return;
                Show(eye);
                return;
            }
            if (_group == null) return;
            _t += Time.deltaTime;
            // It stays, as the room's title, for the whole chapter (Saul, 5 Oct) - anchored or not.
            _group.alpha = Mathf.Clamp01(_t / 0.6f);
        }

        /// <summary>
        /// Ahead and high, with a clear line from the eye: 6 m ahead it once stood behind Monet's Great Wave, half hidden.
        /// Raised until nothing solid is in the way (up to 3 m more); failing that, brought in short of what is.
        /// </summary>
        static Vector3 Clear(Vector3 eye, Vector3 fwd)
        {
            // Its whole width, not just its centre: at 6 m the panel is ~4.8 m wide, and the Wave cut through its side.
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            // Raised first, then nearer: the first place from which its centre and both edges are in plain view.
            for (var ahead = Ahead; ahead >= 3f; ahead -= 1f)
            {
                var half = PanelScale.MetresPerPixel(ahead) * WidthPx * 0.5f;
                for (var lift = 0f; lift <= 3f; lift += 0.5f)
                {
                    var at = eye + fwd * ahead + Vector3.up * (Above + lift);
                    if (Seen(eye, at) && Seen(eye, at - right * half) && Seen(eye, at + right * half)) return at;
                }
            }
            return eye + fwd * Ahead + Vector3.up * Above;
        }

        static bool Seen(Vector3 eye, Vector3 point) => !Physics.Linecast(eye, point, out _, ~0, QueryTriggerInteraction.Ignore);

        const float WidthPx = 640f;

        void Show(Transform eye)
        {
            _shown = true;
            var fwd = eye.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            var anchor = new GameObject("Chapter Question").transform;
            anchor.SetParent(transform, true);
            var at = Clear(eye.position, fwd);
            var distance = Mathf.Max(3f, Vector3.Distance(eye.position, at));
            if (anchored)
            {
                at = transform.root.TransformPoint(anchorPoint);
                var toIt = at - eye.position; toIt.y = 0f;
                if (toIt.sqrMagnitude > 1e-4f) fwd = toIt.normalized;
                distance = Mathf.Max(Ahead, Vector3.Distance(eye.position, at));   // sized to read from where the visitor arrives
            }
            anchor.SetPositionAndRotation(at, Quaternion.LookRotation(fwd, Vector3.up));   // +Z away from the viewer reads
            if (anchored) anchor.localScale = Vector3.one * Mathf.Max(0.1f, anchorScale);
            // Every title turns to the visitor, wherever they walk: one left facing its arrival read backwards from the far
            // side of the garden ("What is worth stopping for?", mirrored over the time ring, 5 Oct).
            TurnToVisitor.Attach(anchor.gameObject);
            var c = MuseUi.Canvas(anchor, "Question", distance, WidthPx);
            _group = c.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            // Darker, so it reads against any world behind it (Saul, 5 Oct).
            var glass = MuseUi.Card(c, new Color32(6, 5, 8, 236), MuseTheme.PanelRadius, new Color32(238, 233, 223, 46), 1f, padX: 26f, padY: 18f, gap: 6f, name: "Glass");
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
