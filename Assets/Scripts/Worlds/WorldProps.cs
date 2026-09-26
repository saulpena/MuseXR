using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>
    /// Objects that belong to one splat world: shown while that world is loaded, hidden otherwise.
    ///
    /// Put the objects under this GameObject at their world positions (the splat world sits at
    /// the origin, scaled by its worldScale, so positions are in the same metres the visitor
    /// walks in). The component listens to <see cref="WorldCycler.WorldChanged"/>, so the props
    /// arrive with their world in whichever stage loads it, and nowhere else.
    /// </summary>
    public sealed class WorldProps : MonoBehaviour
    {
        [Tooltip("WorldCatalog key including the size suffix, e.g. celestial-peach-blossom-paradise-500k")]
        public string worldKey;

        WorldCycler _cycler;

        /// <summary>Pure rule, so it is testable: show only for this exact world.</summary>
        public static bool ShouldShow(string loadedWorldKey, string propsWorldKey) =>
            !string.IsNullOrEmpty(propsWorldKey) &&
            string.Equals(loadedWorldKey, propsWorldKey, System.StringComparison.Ordinal);

        void Awake()
        {
            // Hidden until its world arrives; the children are what get toggled, so this
            // component keeps listening while they are off.
            SetChildren(false);
        }

        void OnEnable()
        {
            _cycler = _cycler != null ? _cycler : FindFirstObjectByType<WorldCycler>();
            if (_cycler == null)
            {
                Debug.LogWarning($"[WorldProps] no WorldCycler in the scene; '{worldKey}' props stay hidden");
                return;
            }
            _cycler.WorldChanged += OnWorldChanged;
            if (_cycler.Current != null) OnWorldChanged(_cycler.Current);
        }

        void OnDisable()
        {
            if (_cycler != null) _cycler.WorldChanged -= OnWorldChanged;
        }

        void OnWorldChanged(WorldDefinition world)
        {
            bool show = ShouldShow(world != null ? world.key : null, worldKey);
            SetChildren(show);
            Debug.Log($"[WorldProps] {worldKey}: {(show ? "shown" : "hidden")} ({transform.childCount} objects) " +
                      $"for world '{(world != null ? world.key : "none")}'");
        }

        void SetChildren(bool on)
        {
            for (int i = 0; i < transform.childCount; i++)
                transform.GetChild(i).gameObject.SetActive(on);
        }
    }
}
