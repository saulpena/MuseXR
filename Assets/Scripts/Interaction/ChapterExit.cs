using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// A chapter scene's way out, for chapters whose interactions are not built yet (Van Gogh, Monet,
    /// Your world): a walkable floor from the start, and <see cref="Complete"/>, which opens the exit
    /// gate as if the chapter's interaction had just been finished - the frame rises with the next world
    /// already showing in it, and walking through it goes there.
    /// </summary>
    public sealed class ChapterExit : MonoBehaviour
    {
        [Tooltip("The Moon Gate at this chapter's exit, its next world set.")]
        public MuseXR.Worlds.MoonGate gate;

        [Tooltip("The world's collider (Assets/Worlds/Colliders/*-collider.glb): its floor is walkable. Optional.")]
        public GameObject colliderModel;

        [Tooltip("This world's key in WorldCatalog.Small (for the collider's frame).")]
        public string worldKey;

        /// <summary>
        /// Walk on the capture's own collider (raised floors). Off by default: these colliders carry
        /// junk, and in Van Gogh's studio it filled the side door - the walk climbed to 1.9 m in the
        /// doorway and passed over the opening. A flat floor is right for a flat room.
        /// </summary>
        [Tooltip("Walk on the capture's collider (for raised floors). Off: the flat floor only.")]
        public bool walkOnCapture;

        [Tooltip("Open by itself once this chapter's choice is in the journey record: 'vangogh' (the stroke kept), 'monet'. Empty: opened by a script.")]
        public string openWhenChapterDone;
        [Tooltip("Open by itself this many seconds after arriving (0: never). Your world's 'start again' arch.")]
        public float openAfterSeconds;

        MuseXR.Worlds.CaptureProbe _probe;
        float _since;

        // The floor exists before the first physics frame (made later, gravity drops the rig through it).
        void Awake() => Floor(Vector3.zero);

        void Start()
        {
            MuseXR.Worlds.WorldDefinition w = null;
            foreach (var x in MuseXR.Worlds.WorldCatalog.Small) if (x.key == worldKey) w = x;
            _probe = walkOnCapture && colliderModel != null && w != null ? MuseXR.Worlds.CaptureProbe.Open(w, new[] { colliderModel }) : null;
            _probe?.MakeTeleportable();   // a walkable (and teleportable) copy of the capture's floor
        }

        void Update()
        {
            if (gate == null || gate.IsOpen) return;
            // Chained into GateWorld a frame can be awake behind a gate: only count once it stands at the origin.
            if (transform.root.position.sqrMagnitude > 0.01f) return;
            _since += Time.deltaTime;
            bool done = openWhenChapterDone switch
            {
                "vangogh" => JourneyMemory.Record.VanGogh != null,
                "monet" => JourneyMemory.Record.Monet != null,
                _ => false,
            };
            if (done || (openAfterSeconds > 0f && _since >= openAfterSeconds)) Complete();
        }

        /// <summary>The chapter is done: open the exit. The world's layout goes with the old world.</summary>
        public bool Complete()
        {
            if (gate == null || gate.IsOpen) return gate != null;
            var here = FindAnyObjectByType<GaussianSplatting.Runtime.GaussianSplatRenderer>();
            var props = new List<GameObject>();
            // Its chapter's layout: the scene's roots, or - chained into one scene - its frame's children.
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.parent == transform.parent && t.name.StartsWith("Chapter ") && t != transform) props.Add(t.gameObject);   // not this: its floor stays
            if (_probe != null && _probe.Root != null) props.Add(_probe.Root.gameObject);
            return gate.Open(here, props, null, showNow: true);
        }

        void Floor(Vector3 at)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Walk Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.position = at;
            floor.transform.localScale = new Vector3(8f, 1f, 8f);   // 80 x 80 m
            floor.GetComponent<Renderer>().enabled = false;
        }
    }
}
