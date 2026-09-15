using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MusePico.Gallery
{
    /// <summary>
    /// Builds the exhibit ring from a <see cref="GalleryCatalog"/>, in the Editor and at runtime.
    ///
    /// <c>[ExecuteAlways]</c> is the point: the gallery is visible in the Scene view the moment a
    /// model is added to the catalogue, without entering Play Mode. That matters here more than
    /// usual, because Play Mode in this project cannot render the splat worlds on a DX11 Editor
    /// and a device build is a ~3 minute round trip — an exhibition you can only see after a
    /// build is an exhibition nobody will iterate on.
    ///
    /// Everything it spawns is marked <see cref="HideFlags.DontSave"/>, so the scene file keeps
    /// one small object and a reference to the catalogue rather than a baked copy of the
    /// hierarchy. Change the catalogue, and the scene is correct without being re-saved.
    /// </summary>
    [ExecuteAlways]
    public class GalleryRing : MonoBehaviour
    {
        [Tooltip("The exhibition list. Changing it rebuilds the ring.")]
        public GalleryCatalog catalog;

        [Header("Pedestals")]
        public bool showPedestals = true;
        public float pedestalRadius = 0.35f;
        public Material pedestalMaterial;

        [Header("Captions")]
        [Tooltip("Optional. Needs TMP Essential Resources imported; without it the exhibits still stand, unlabelled.")]
        public TMP_FontAsset captionFont;

        [Tooltip("Height of the label plate above the floor, as a fraction of the pedestal height.")]
        [Range(0.1f, 0.9f)] public float captionHeightFraction = 0.55f;

        public float captionWidth = 0.62f;

        // TMP's world-space fontSize is neither metres nor points — MEASURED on this scene,
        // fontSize 0.6 renders about 16 mm of cap height. Serialized so the plate can be tuned in
        // the Inspector without a domain reload.
        [Tooltip("Body text size. The name is drawn at titleScalePercent of this.")]
        public float bodyFontSize = 0.6f;

        [Range(120f, 400f)] public float titleScalePercent = 220f;

        public float plateHeight = 0.24f;

        [Header("Turntable")]
        public bool rotateExhibits = true;
        public float degreesPerSecond = 8f;

        readonly List<GameObject> _spawned = new List<GameObject>();

        void OnEnable() => Rebuild();

        void OnDisable() => Clear();

        void OnValidate()
        {
            // OnValidate runs during serialisation, where DestroyImmediate is illegal. Defer.
            if (!isActiveAndEnabled) return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) Rebuild(); };
#else
            Rebuild();
#endif
        }

        /// <summary>Tears the ring down and lays it out again from the catalogue.</summary>
        public void Rebuild()
        {
            Clear();
            if (catalog == null) return;

            var placeable = new List<GalleryEntry>();
            foreach (var entry in catalog.Placeable()) placeable.Add(entry);

            for (var i = 0; i < placeable.Count; i++)
            {
                var placement = GalleryLayout.Arc(i, placeable.Count, catalog.radius, catalog.arcDegrees);
                Build(placeable[i], placement);
            }
        }

        void Build(GalleryEntry entry, GalleryPlacement placement)
        {
            var root = New("Exhibit — " + Label(entry), transform);
            root.transform.localPosition = placement.Position;
            root.transform.localRotation = placement.Rotation;

            if (showPedestals) BuildPedestal(root.transform);

            var pivot = New("Pivot", root.transform);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.Euler(0f, entry.yawOffset, 0f);

            var model = Instantiate(entry.model, pivot.transform);
            model.name = "Model";
            Mark(model);

            // Scale and lift from the model's OWN bounds. Tripo meshes carry no authored pivot,
            // so the transform they arrive with says nothing about where their feet are.
            var bounds = LocalRendererBounds(model);
            var fit = GalleryLayout.Fit(bounds, catalog.HeightFor(entry), catalog.pedestalHeight);
            model.transform.localScale = Vector3.one * fit.Scale;
            model.transform.localPosition = fit.Offset;

            var plinth = root.AddComponent<GalleryPlinth>();
            plinth.modelPivot = pivot.transform;
            plinth.rotate = rotateExhibits;
            plinth.degreesPerSecond = degreesPerSecond;
            plinth.provenance = Provenance(entry);

            BuildCaption(root.transform, entry, plinth);
        }

        void BuildPedestal(Transform parent)
        {
            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "Pedestal";
            Mark(pedestal);
            pedestal.transform.SetParent(parent, false);
            // A Unity cylinder is 2 units tall, so half the height is the local Y scale.
            pedestal.transform.localScale = new Vector3(pedestalRadius * 2f, catalog.pedestalHeight * 0.5f, pedestalRadius * 2f);
            pedestal.transform.localPosition = new Vector3(0f, catalog.pedestalHeight * 0.5f, 0f);

            if (pedestalMaterial == null) return;
            var renderer = pedestal.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = pedestalMaterial;
        }

        void BuildCaption(Transform parent, GalleryEntry entry, GalleryPlinth plinth)
        {
            if (captionFont == null) return;

            // The plate goes on the near face of the pedestal — LOCAL +Z, since the exhibit root
            // is already turned to face the viewer.
            //
            // The 180 degree flip is the part that is not guessable, and is MEASURED rather than
            // reasoned: with the plate's +Z pointing at the camera, every forward vector checks
            // out and the glyphs still render mirrored. Flipping it reads correctly. Do not
            // "simplify" this away on the strength of the transform maths.
            var caption = New("Caption", parent);
            caption.transform.localPosition =
                new Vector3(0f, catalog.pedestalHeight * captionHeightFraction, pedestalRadius + 0.015f);
            caption.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var text = NewRect("Label", caption.transform).AddComponent<TextMeshPro>();
            text.font = captionFont;
            text.fontSize = bodyFontSize;
            text.alignment = TextAlignmentOptions.Top;
            text.enableWordWrapping = true;
            // Explicit: the default draws a stray ellipsis under a plate whose rect is taller
            // than its text.
            text.overflowMode = TextOverflowModes.Overflow;
            text.color = new Color(0.80f, 0.78f, 0.75f);
            text.rectTransform.sizeDelta = new Vector2(captionWidth, plateHeight);
            text.rectTransform.localPosition = Vector3.zero;

            plinth.label = text;
            plinth.SetLabels(Label(entry), entry.caption, Mathf.RoundToInt(titleScalePercent), "#D8D4CE");
        }

        /// <summary>
        /// Combined renderer bounds expressed in the model root's local space.
        ///
        /// <c>Renderer.bounds</c> is world-space and axis-aligned, so reading it straight would
        /// fold this object's own position and rotation into the result and every exhibit past
        /// the first would be mis-scaled. <c>localBounds</c> transformed into the root is the
        /// measurement that survives being placed on an arc.
        /// </summary>
        static Bounds LocalRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var started = false;
            var result = new Bounds();
            var toRoot = root.transform.worldToLocalMatrix;

            foreach (var renderer in renderers)
            {
                if (renderer is ParticleSystemRenderer) continue;
                var local = renderer.localBounds;
                var matrix = toRoot * renderer.transform.localToWorldMatrix;

                for (var corner = 0; corner < 8; corner++)
                {
                    var point = local.center + Vector3.Scale(local.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                    var inRoot = matrix.MultiplyPoint3x4(point);

                    if (!started) { result = new Bounds(inRoot, Vector3.zero); started = true; }
                    else result.Encapsulate(inRoot);
                }
            }

            return started ? result : new Bounds(Vector3.zero, Vector3.zero);
        }

        static string Label(GalleryEntry entry) =>
            string.IsNullOrEmpty(entry.displayName) ? (entry.id ?? "Untitled") : entry.displayName;

        static string Provenance(GalleryEntry entry)
        {
            if (!entry.IsTripoGenerated) return entry.rights ?? string.Empty;
            var parts = new List<string> { "Tripo " + entry.tripoModelVersion };
            if (!string.IsNullOrEmpty(entry.tripoTaskType)) parts.Add(entry.tripoTaskType);
            if (!string.IsNullOrEmpty(entry.tripoTaskId)) parts.Add("task " + entry.tripoTaskId);
            if (!string.IsNullOrEmpty(entry.generatedOn)) parts.Add(entry.generatedOn);
            var line = string.Join(" · ", parts);
            return string.IsNullOrEmpty(entry.rights) ? line : line + "\n" + entry.rights;
        }

        GameObject New(string name, Transform parent)
        {
            var go = new GameObject(name);
            Mark(go);
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>TMP_Text needs a RectTransform, and a Transform cannot be swapped for one afterwards.</summary>
        GameObject NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Mark(go);
            go.transform.SetParent(parent, false);
            return go;
        }

        void Mark(GameObject go)
        {
            // DontSave keeps the generated hierarchy out of the scene file: the scene stores the
            // catalogue reference, not a frozen copy of what the catalogue contained that day.
            go.hideFlags = HideFlags.DontSave;
            _spawned.Add(go);
        }

        void Clear()
        {
            foreach (var go in _spawned)
            {
                if (go == null) continue;
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
            _spawned.Clear();

            // Belt and braces: a domain reload empties _spawned but leaves the objects behind.
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if ((child.hideFlags & HideFlags.DontSave) == 0) continue;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
        }
    }
}
