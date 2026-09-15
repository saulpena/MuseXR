using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusePico.Gallery
{
    /// <summary>
    /// One exhibit: the model, what to say about it, and where it came from.
    ///
    /// The provenance block is not decoration. muse-infinity's THIRD_PARTY_NOTICES.md commits the
    /// project to recording, for every Tripo model, "its generation account, creation date,
    /// ownership, applicable terms, and any source inputs" — and its ASSET_PIPELINE.md requires
    /// every model derived from a historical portrait to stay labelled an AI interpretation. A
    /// note in a markdown file drifts from the asset it describes; a field on the asset does not.
    /// </summary>
    [Serializable]
    public class GalleryEntry
    {
        [Tooltip("Stable id. Matches muse-infinity's character ids where the exhibit is one of theirs.")]
        public string id;

        [Tooltip("Shown on the plinth caption.")]
        public string displayName;

        [Tooltip("Second caption line — one sentence, what the visitor is looking at.")]
        [TextArea(2, 4)]
        public string caption;

        [Tooltip("The model. A .glb imported by glTFast, or any prefab.")]
        public GameObject model;

        [Header("Provenance")]
        [Tooltip("Tripo model version, e.g. v3.1-20260211. Empty for assets that are not Tripo output.")]
        public string tripoModelVersion;

        [Tooltip("How it was generated: text_to_model, image_to_model, multiview_to_model.")]
        public string tripoTaskType;

        [Tooltip("Tripo task id, so the generation can be traced back or re-downloaded.")]
        public string tripoTaskId;

        [Tooltip("The prompt, or the source images, that produced it.")]
        [TextArea(2, 4)]
        public string sourceInputs;

        [Tooltip("Rights note. For a likeness this must say it is an AI interpretation.")]
        [TextArea(2, 4)]
        public string rights;

        [Tooltip("Date generated, ISO 8601.")]
        public string generatedOn;

        [Header("Display")]
        [Tooltip("Metres the tallest axis should occupy. 0 uses the gallery default.")]
        public float targetHeight;

        [Tooltip("Extra yaw applied after the model is faced toward the viewer.")]
        public float yawOffset;

        public bool IsTripoGenerated => !string.IsNullOrEmpty(tripoModelVersion) || !string.IsNullOrEmpty(tripoTaskId);
    }

    /// <summary>
    /// The exhibition list. A ScriptableObject rather than a hard-coded array so the Tripo editor
    /// window can append a freshly generated model to it without touching a scene or a script.
    /// </summary>
    [CreateAssetMenu(fileName = "TripoGalleryCatalog", menuName = "MusePico/Tripo Gallery Catalog")]
    public class GalleryCatalog : ScriptableObject
    {
        [TextArea(2, 5)]
        public string description = "Tripo-generated exhibits.";

        [Header("Ring")]
        [Tooltip("Metres from the viewer to each pedestal.")]
        public float radius = 3.5f;

        [Tooltip("Total sweep of the arc. 170 puts the outermost pieces at the viewer's shoulders.")]
        public float arcDegrees = 170f;

        [Tooltip("Default display height in metres. 1.8 reads as life-size.")]
        public float defaultTargetHeight = 1.8f;

        [Tooltip("Height of the pedestal top the models stand on.")]
        public float pedestalHeight = 0.9f;

        public List<GalleryEntry> entries = new List<GalleryEntry>();

        /// <summary>Entries with a model assigned — the ones a builder can actually place.</summary>
        public IEnumerable<GalleryEntry> Placeable()
        {
            foreach (var entry in entries)
                if (entry != null && entry.model != null)
                    yield return entry;
        }

        public float HeightFor(GalleryEntry entry) =>
            entry != null && entry.targetHeight > 0f ? entry.targetHeight : defaultTargetHeight;
    }
}
