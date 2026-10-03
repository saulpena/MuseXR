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

        /// <summary>
        /// The playtested WALKABLE box, in pre-scale world units: her `profile.bounds` from
        /// `muse-infinity/config/worlds.js`, tuned over 27 commits of walking these captures.
        ///
        /// <b>This is not the splat bounds, and the difference is large.</b> A capture's bounds
        /// include sky, terrain and outlying noise: van-gogh measures about x +-18 raw, while the
        /// room you can actually walk is x -2.47 to 10. Hanging artworks on the bounds put every
        /// one of them roughly 28 m outside the room, floating in black void — verified by
        /// screenshot before this field existed.
        ///
        /// Zero when unmeasured; callers should fall back to the splat bounds and accept the
        /// consequences.
        /// </summary>
        public Vector4 walkBounds;   // (minX, maxX, minZ, maxZ)

        public bool HasWalkBounds => walkBounds != Vector4.zero;

        /// <summary>The walkable box in WORLD units, after <see cref="worldScale"/>.</summary>
        public Bounds ScaledWalkBounds
        {
            get
            {
                var min = new Vector3(walkBounds.x, groundY, walkBounds.z) * worldScale;
                var max = new Vector3(walkBounds.y, groundY, walkBounds.w) * worldScale;
                var b = new Bounds();
                b.SetMinMax(new Vector3(Mathf.Min(min.x, max.x), min.y, Mathf.Min(min.z, max.z)),
                            new Vector3(Mathf.Max(min.x, max.x), max.y, Mathf.Max(min.z, max.z)));
                return b;
            }
        }

        /// <summary>Addressables key. Set by AddressableWorldSetup to the asset's file name,
        /// which is the world key.</summary>
        public string Address => key;

        /// <summary>Spawn position with the immersion scale applied — the same transformation
        /// scaleProfile() performs in the original.</summary>
        public Vector3 ScaledSpawn =>
            new Vector3(spawn.x * worldScale, groundY * worldScale, spawn.y * worldScale);

        /// <summary>
        /// True for a world whose yaw is copied from muse-infinity's worlds.js. Her camera at yaw y
        /// looks along (sin y, 0, -cos y) (three.js looks down -Z); Unity's Euler(0, a) looks along
        /// (sin a, 0, cos a). So her yaw is Unity's 180 - y. Measured 1 Oct 2026 in her running app:
        /// at the conservatory, yaw 0, her paintings run toward -Z.
        /// </summary>
        public bool yawFromWeb;

        /// <summary>The yaw as entered, before the web conversion. Door spots are bearings from this.</summary>
        public Quaternion EnteredRotation => Quaternion.Euler(0f, yawDegrees, 0f);

        public Quaternion SpawnRotation => Quaternion.Euler(0f, yawFromWeb ? 180f - yawDegrees : yawDegrees, 0f);

        /// <summary>Uniform scale for the splat object. Every world shipped in the release uses
        /// the pre-baked .spz convention, so this is the ONLY transform they need — no rotation,
        /// no offset. (Only bright-gallery-hall is rawMarble, and its .spz is not in the release.)</summary>
        public Vector3 SplatScale => new Vector3(mirrorX ? -worldScale : worldScale, worldScale, worldScale);

        /// <summary>
        /// Draw the splats mirrored in X. For worlds designed in Marble's own viewer to Skylar's
        /// top-down diagrams (Palace, Grotto): our splat import shows them left-right reversed
        /// against Marble's viewer, so "the moon gate in the right wall" lands on the left
        /// (measured 3 Oct 2026). Her older worlds are already in her app's frame and stay false.
        /// A mirrored world's splats agree with its glTFast-imported collider, which glTFast had
        /// already flipped - so collider queries for it are NOT mirrored (CaptureProbe).
        /// </summary>
        public bool mirrorX;
    }
}
