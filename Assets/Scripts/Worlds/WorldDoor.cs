using System;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MuseXR.Worlds
{
    /// <summary>
    /// A door to one exhibition world: a gold frame standing on the floor, the scene's own thumbnail
    /// in the opening, and her chapter and title above it. Pointing the controller ray at it and
    /// pulling the trigger opens it; so does walking through it (<see cref="WorldDoorsRunner"/>
    /// watches for that).
    ///
    /// Door space: origin at the centre of the threshold on the floor, +Z pointing away from the
    /// visitor. That is also the orientation a Quad and a TextMeshPro need to be readable from the
    /// visitor's side (their +Z must point AWAY from the viewer), so every visual child sits at
    /// identity rotation.
    /// </summary>
    public sealed class WorldDoor : MonoBehaviour
    {
        public const float Width = 1.2f;
        public const float Height = 2.0f;
        const float Bar = 0.08f;
        /// <summary>Outside width of the frame, and of the name plate above it.</summary>
        public const float OuterWidth = Width + Bar * 2f;

        static readonly Color Gold = new Color(0.79f, 0.67f, 0.45f);     // her #C9AA72
        static readonly Color GoldLit = new Color(1.00f, 0.88f, 0.62f);
        static readonly Color PanelRest = new Color(0.82f, 0.82f, 0.82f);

        /// <summary>The scene this door opens.</summary>
        public ExhibitionScene Scene { get; private set; }

        /// <summary>Raised when the visitor picks the door with the ray.</summary>
        public event Action<WorldDoor> Picked;

        /// <summary>A door that has only just appeared ignores a press for this long, so the trigger
        /// pull that opened the previous door cannot land on one that appeared under the ray.</summary>
        const float FreshGuard = 0.6f;

        Material _frame, _panel, _plate;
        float _builtAt;

        public static WorldDoor Build(Transform parent, DoorPose pose, ExhibitionScene scene, Texture thumbnail)
        {
            var go = new GameObject("Door " + scene.chapter + " " + scene.title);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            var door = go.AddComponent<WorldDoor>();
            door.Construct(scene, thumbnail);
            return door;
        }

        void Construct(ExhibitionScene scene, Texture thumbnail)
        {
            Scene = scene;
            _builtAt = Time.unscaledTime;
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");

            _frame = new Material(unlit) { name = "Door Frame" };
            _frame.SetColor("_BaseColor", Gold);

            // Left, right, lintel, threshold.
            Bar_("Left",  new Vector3(-(Width + Bar) / 2f, Height / 2f, 0f), new Vector3(Bar, Height + Bar * 2f, Bar));
            Bar_("Right", new Vector3( (Width + Bar) / 2f, Height / 2f, 0f), new Vector3(Bar, Height + Bar * 2f, Bar));
            Bar_("Lintel", new Vector3(0f, Height + Bar / 2f, 0f), new Vector3(Width, Bar, Bar));
            Bar_("Sill",  new Vector3(0f, Bar / 4f, 0f), new Vector3(Width, Bar / 2f, Bar));

            // The opening shows her thumbnail, cropped to the door's portrait shape from the centre
            // of the 16:9 image rather than squashed into it.
            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "Opening";
            Destroy(panel.GetComponent<Collider>());
            panel.transform.SetParent(transform, false);
            panel.transform.localPosition = new Vector3(0f, Height / 2f, 0f);
            panel.transform.localScale = new Vector3(Width, Height, 1f);
            _panel = new Material(unlit) { name = "Door Opening" };
            _panel.SetColor("_BaseColor", PanelRest);
            if (thumbnail != null)
            {
                _panel.SetTexture("_BaseMap", thumbnail);
                float imageAspect = thumbnail.width / (float)Mathf.Max(1, thumbnail.height);
                float u = Mathf.Clamp01((Width / Height) / imageAspect);
                _panel.SetTextureScale("_BaseMap", new Vector2(u, 1f));
                _panel.SetTextureOffset("_BaseMap", new Vector2((1f - u) / 2f, 0f));
            }
            panel.GetComponent<MeshRenderer>().sharedMaterial = _panel;

            BuildLabel(scene, unlit);

            // A trigger, so walking into the doorway is not blocked by it; handed to the interactable
            // explicitly, because XRBaseInteractable drops trigger colliders it collects itself.
            var box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, Height / 2f, 0f);
            box.size = new Vector3(Width + Bar * 2f, Height + Bar * 2f, 0.15f);

            gameObject.SetActive(false);
            var interactable = gameObject.AddComponent<XRSimpleInteractable>();
            interactable.colliders.Clear();
            interactable.colliders.Add(box);
            gameObject.SetActive(true);

            interactable.hoverEntered.AddListener(_ => Highlight(true));
            interactable.hoverExited.AddListener(_ => Highlight(false));
            interactable.selectEntered.AddListener(args =>
            {
                if (Time.unscaledTime - _builtAt < FreshGuard) return;
                Debug.Log("[WorldDoors] picked " + scene.chapter + " " + scene.title + " by " +
                          (args.interactorObject != null ? args.interactorObject.transform.name : "?"));
                Pick();
            });
        }

        /// <summary>Open the door, as the ray does. Also the entry point for tests and the Editor.</summary>
        public void Pick() => Picked?.Invoke(this);

        void Bar_(string name, Vector3 centre, Vector3 size)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = name;
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(transform, false);
            bar.transform.localPosition = centre;
            bar.transform.localScale = size;
            bar.GetComponent<MeshRenderer>().sharedMaterial = _frame;
        }

        void BuildLabel(ExhibitionScene scene, Shader unlit)
        {
            var label = new GameObject("Label");
            label.transform.SetParent(transform, false);
            label.transform.localPosition = new Vector3(0f, Height + 0.38f, 0f);

            // A dark plate behind the words, so they read over a bright sky as well as a dark room.
            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "Plate";
            Destroy(plate.GetComponent<Collider>());
            plate.transform.SetParent(label.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            plate.transform.localScale = new Vector3(OuterWidth + 0.14f, 0.58f, 1f);
            var mat = _plate = new Material(unlit) { name = "Door Plate" };
            mat.SetColor("_BaseColor", new Color(0.04f, 0.035f, 0.03f, 0.78f));
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 5;
            plate.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var text = new GameObject("Text").AddComponent<TextMeshPro>();
            text.transform.SetParent(label.transform, false);
            text.rectTransform.sizeDelta = new Vector2(OuterWidth + 0.06f, 0.54f);
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.fontSize = 1.3f;
            text.color = Color.white;
            text.text = "<size=55%><color=#C9AA72><cspace=0.18em>" + scene.chapter + "</cspace></color></size>\n" +
                        scene.title;
        }

        void Highlight(bool on)
        {
            if (_frame != null) _frame.SetColor("_BaseColor", on ? GoldLit : Gold);
            if (_panel != null) _panel.SetColor("_BaseColor", on ? Color.white : PanelRest);
        }

        void OnDestroy()
        {
            if (_frame != null) Destroy(_frame);
            if (_panel != null) Destroy(_panel);
            if (_plate != null) Destroy(_plate);
        }
    }
}
