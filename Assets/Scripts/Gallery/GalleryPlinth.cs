using TMPro;
using UnityEngine;

namespace MusePico.Gallery
{
    /// <summary>
    /// One pedestal and the exhibit on it.
    ///
    /// The turntable is on the model, not the pedestal, so the caption stays readable while the
    /// piece turns — a sculpture rotating on a plinth reads as a gallery; a rotating label reads
    /// as a bug.
    ///
    /// The caption is a TMP_Text, per the project's UI rule, and is optional: if TMP Essential
    /// Resources are not imported the exhibit still stands and still turns, it just has no label.
    /// </summary>
    public class GalleryPlinth : MonoBehaviour
    {
        [Header("Scene references")]
        [Tooltip("The transform the model was parented under. This is what spins.")]
        public Transform modelPivot;

        /// <summary>
        /// One text block, not two. Stacking a title rect above a body rect means computing where
        /// TMP will actually draw inside each — and getting it wrong renders the first body line
        /// straight through the title, which is what two attempts at the two-rect version did.
        /// Rich-text sizing inside a single block cannot overlap itself.
        /// </summary>
        public TMP_Text label;

        [Header("Turntable")]
        public bool rotate = true;

        [Tooltip("Degrees per second. Slow — fast rotation in a headset is unpleasant to watch.")]
        public float degreesPerSecond = 8f;

        /// <summary>Set by the builder so the scene keeps the rights record with the exhibit.</summary>
        [Header("Provenance (read-only record)")]
        [TextArea(2, 6)] public string provenance;

        float _baseYaw;

        void Awake()
        {
            if (modelPivot != null) _baseYaw = modelPivot.localEulerAngles.y;
        }

        void Update()
        {
            if (!rotate || modelPivot == null) return;
            _baseYaw = Mathf.Repeat(_baseYaw + degreesPerSecond * Time.deltaTime, 360f);
            modelPivot.localRotation = Quaternion.Euler(0f, _baseYaw, 0f);
        }

        public void SetLabels(string title, string caption) =>
            SetLabels(title, caption, 220, "#C8C4BE");

        /// <summary>Composes the plate as one rich-text block: name, then the note beneath it.</summary>
        public void SetLabels(string title, string caption, int titlePercent, string captionColor)
        {
            if (label == null) return;
            label.richText = true;
            label.text = "<size=" + titlePercent + "%>" + (title ?? string.Empty) + "</size>" +
                         (string.IsNullOrEmpty(caption)
                             ? string.Empty
                             : "\n<color=" + captionColor + ">" + caption + "</color>");
        }
    }
}
