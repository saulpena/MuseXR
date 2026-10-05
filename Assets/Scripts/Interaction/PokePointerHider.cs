using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// The XR Interaction Toolkit starter rig puts a poke-pointer mesh on each controller's tip ("Poke Point /
    /// Pinch_Pointer_LOD0", a skinned mesh). Nothing here pokes - every choice is the trigger ray - and on the Quest it
    /// grew huge and sat round the visitor's head (Saul, 5 Oct: "the thing on the tip ... is causing more trouble").
    /// The toolkit switches the poke interactor on at runtime, so the mesh is hidden whenever it appears, not once in
    /// the scene. The interactor itself is left alone; only its visual goes.
    /// </summary>
    public sealed class PokePointerHider : MonoBehaviour
    {
        public const string MeshName = "Pinch_Pointer";
        /// <summary>How often the found meshes are re-hidden, and how often the scene is searched for new ones.</summary>
        public const float CheckSeconds = 0.25f, SearchSeconds = 5f;
        readonly System.Collections.Generic.List<Renderer> _found = new System.Collections.Generic.List<Renderer>();
        float _next, _nextSearch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var go = new GameObject("Poke Pointer Hider") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<PokePointerHider>();
        }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + CheckSeconds;
            if (Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + SearchSeconds;
                _found.Clear();
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (r.name.StartsWith(MeshName)) _found.Add(r);
            }
            foreach (var r in _found) if (r != null && r.enabled) r.enabled = false;
        }
    }
}
