using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusePico.Journey
{
    /// <summary>Where one work hangs in one world, baked from that world's collider.</summary>
    [Serializable]
    public struct WallAnchor
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    /// <summary>Every hanging spot found in a single capture.</summary>
    [Serializable]
    public class WorldAnchors
    {
        public string worldKey;
        public WallAnchor[] anchors = new WallAnchor[0];
    }

    /// <summary>
    /// Wall positions swept from the Marble colliders in the Editor, so the runtime does not have
    /// to.
    ///
    /// <b>Why bake rather than sweep live.</b> The sweep is the good placement — it puts a work on
    /// a surface the visitor can actually see, and `WallSupport` throws away the collider fragments
    /// that are not wall. But it needs the collider MESH, which is an 85,000-triangle glTF import,
    /// and doing that on every chapter change would add seconds to a load that is already 3.4 s.
    /// The geometry never changes, so the answer never changes: sweep once, store, read.
    ///
    /// A world with no entry here falls back to the playtested walk box, which is weaker but never
    /// absent — the same graceful-degradation rule the sweep itself follows for a capture whose
    /// collider is patchy.
    /// </summary>
    [CreateAssetMenu(fileName = "WallAnchors", menuName = "MuseXR/Wall Anchors")]
    public class WallAnchors : ScriptableObject
    {
        public List<WorldAnchors> worlds = new List<WorldAnchors>();

        /// <summary>The anchors for a capture, or null when it was never baked.</summary>
        public WallAnchor[] For(string worldKey)
        {
            if (string.IsNullOrEmpty(worldKey)) return null;

            foreach (var w in worlds)
            {
                if (w == null || w.anchors == null || w.anchors.Length == 0) continue;
                if (string.Equals(w.worldKey, worldKey, StringComparison.OrdinalIgnoreCase))
                    return w.anchors;
            }
            return null;
        }

        /// <summary>Replace one capture's anchors, adding the capture if it is new.</summary>
        public void Set(string worldKey, WallAnchor[] anchors)
        {
            if (string.IsNullOrEmpty(worldKey)) return;

            foreach (var w in worlds)
            {
                if (!string.Equals(w.worldKey, worldKey, StringComparison.OrdinalIgnoreCase)) continue;
                w.anchors = anchors ?? new WallAnchor[0];
                return;
            }
            worlds.Add(new WorldAnchors { worldKey = worldKey, anchors = anchors ?? new WallAnchor[0] });
        }
    }
}
