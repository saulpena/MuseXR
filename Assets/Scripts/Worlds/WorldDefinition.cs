using System;
using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>
    /// One Marble world and the hand-measured data needed to stand in it.
    ///
    /// These values are ported from muse-infinity/config/worlds.js, where they were tuned by
    /// playtesting over 27 commits — spawn points were re-derived from colliders after visitors
    /// started "in the sky", entries were turned 90 and 180 degrees to face the right thing, and
    /// worldScale was settled by eye so a fixed 1.75 m visitor feels right inside the space.
    /// They are worth far more than they look; rediscovering them means repeating that work.
    ///
    /// Pure data plus pure arithmetic — no MonoBehaviour, no scene, so it is testable in EditMode.
    /// </summary>
    [Serializable]
    public class WorldDefinition
    {
        public string key;
        public string displayName;

        /// <summary>Immersion multiplier. The visitor is a fixed size; the world scales instead,
        /// so the space feels large without shrinking the person.</summary>
        public float worldScale = 1f;

        /// <summary>Spawn in world-space XZ, BEFORE worldScale is applied.</summary>
        public Vector2 spawn;

        /// <summary>Floor height at the spawn, before worldScale.</summary>
        public float groundY;

        /// <summary>Facing on arrival, degrees about Y.</summary>
        public float yawDegrees;

        /// <summary>Far clip. Outdoor worlds need much more than interiors.</summary>
        public float cameraFar = 200f;

        /// <summary>False for worlds with no hand-measured spawn (the World Labs samples), which
        /// the cycler places from the asset's own bounds instead of from data.</summary>
        public bool hasMeasuredSpawn = true;

        /// <summary>Addressables key. Set by AddressableWorldSetup to the asset's file name,
        /// which is the world key.</summary>
        public string Address => key;

        /// <summary>Spawn position with the immersion scale applied — the same transformation
        /// scaleProfile() performs in the original.</summary>
        public Vector3 ScaledSpawn =>
            new Vector3(spawn.x * worldScale, groundY * worldScale, spawn.y * worldScale);

        public Quaternion SpawnRotation => Quaternion.Euler(0f, yawDegrees, 0f);

        /// <summary>Uniform scale for the splat object. Every world shipped in the release uses
        /// the pre-baked .spz convention, so this is the ONLY transform they need — no rotation,
        /// no offset. (Only bright-gallery-hall is rawMarble, and its .spz is not in the release.)</summary>
        public Vector3 SplatScale => Vector3.one * worldScale;
    }
}
