using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Worlds
{
    /// <summary>Where one door stands, in world space.</summary>
    public struct DoorPose
    {
        public Vector3 position;   // centre of the threshold, on the floor
        public Quaternion rotation; // +Z points AWAY from the visitor's spawn, i.e. into the door
    }

    /// <summary>
    /// The maths of the world doors, kept out of the MonoBehaviour so it is EditMode-testable:
    /// where a door stands, what floor it stands on, and when walking has carried someone through it.
    /// </summary>
    public static class WorldDoorLayout
    {
        /// <summary>
        /// One door <paramref name="distance"/> metres from <paramref name="centre"/>, at
        /// <paramref name="bearing"/> degrees clockwise from <paramref name="forward"/> (flattened to
        /// the floor), turned so its +Z points away from the centre: square on to a visitor standing
        /// there, and readable (see <see cref="WorldDoor"/>).
        /// </summary>
        public static DoorPose Placed(Vector3 centre, Vector3 forward, float bearing, float distance)
        {
            var flat = new Vector3(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
            var dir = Quaternion.AngleAxis(bearing, Vector3.up) * flat.normalized;
            return new DoorPose { position = centre + dir * distance, rotation = Quaternion.LookRotation(dir, Vector3.up) };
        }

        /// <summary>
        /// A world-space point expressed in the frame of a Marble <c>*-collider.glb</c> as glTFast
        /// imports it. glTFast negates X converting glTF's handedness; the splats, and Skylar's
        /// numbers, never had that flip, so the collider is X-mirrored relative to everything else.
        /// Mirror the QUERY, never the collider: a MeshCollider with a negative scale is inside out,
        /// and a downward ray then passes through floors whose faces now point down.
        /// Its own inverse.
        /// </summary>
        public static Vector3 ToColliderFrame(Vector3 world) => new Vector3(-world.x, world.y, world.z);

        /// <summary>
        /// The three places a door's floor is sampled: under each post and under the middle of the
        /// opening, in world space. The door stands on the MEDIAN of the three. Not the highest: one
        /// sample landing on a flower arch lifted a whole door two metres into the air in the water
        /// garden, and a median ignores one stray hit where a maximum obeys it.
        /// </summary>
        public static Vector3[] FloorSamples(DoorPose pose, float halfWidth)
        {
            var right = pose.rotation * Vector3.right;
            return new[] { pose.position - right * halfWidth, pose.position, pose.position + right * halfWidth };
        }

        /// <summary>The middle one of three values. NaN marks a sample that found no floor: with one
        /// NaN the lower of the other two is used, with two or more the result is NaN.</summary>
        public static float Median3(float a, float b, float c)
        {
            int missing = (float.IsNaN(a) ? 1 : 0) + (float.IsNaN(b) ? 1 : 0) + (float.IsNaN(c) ? 1 : 0);
            if (missing >= 2) return float.NaN;
            if (float.IsNaN(a)) return Mathf.Min(b, c);
            if (float.IsNaN(b)) return Mathf.Min(a, c);
            if (float.IsNaN(c)) return Mathf.Min(a, b);
            return Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), c));
        }

        /// <summary>
        /// How far above or below the visitor's own floor a door may stand before its placement is
        /// reported as suspect: beyond it the door is on a different level of the capture (past a
        /// terrace edge, down a flight of steps, up on a ledge).
        /// </summary>
        public const float MaxFloorStep = 1.0f;

        /// <summary>
        /// Where a companion stands on arriving with the visitor in a new world: a little behind them
        /// and to one side (<paramref name="side"/> -1 left, +1 right), on their floor. Close enough to
        /// be with them, far enough not to stand in their face, and behind so the first sight of the
        /// new world is the world. Matches the escort's own trailing slot.
        /// </summary>
        public static Vector3 ArrivalSlot(Vector3 visitorFloor, float visitorYaw, float side,
                                          float back = 1.3f, float across = 0.9f)
        {
            var rot = Quaternion.Euler(0f, visitorYaw, 0f);
            var right = rot * Vector3.right;
            var forward = rot * Vector3.forward;
            return visitorFloor - forward * back + right * (Mathf.Sign(side == 0f ? 1f : side) * across);
        }

        /// <summary>
        /// Where to put a world that waits behind a door, as a frame (position and rotation applied to
        /// the whole capture): so that its spawn, <paramref name="nextSpawn"/> in its own coordinates,
        /// lies <paramref name="pastDoor"/> metres through the doorway at height
        /// <paramref name="floorY"/>, and the direction its visitor faces on arrival
        /// (<paramref name="nextSpawnRotation"/>) is the direction through the door. Walking through
        /// then lands the visitor exactly where that world's playtested spawn would have.
        /// </summary>
        public static void NextWorldFrame(DoorPose door, Vector3 nextSpawn, Quaternion nextSpawnRotation,
                                          float pastDoor, float floorY, out Vector3 position, out Quaternion rotation)
        {
            rotation = door.rotation * Quaternion.Inverse(nextSpawnRotation);
            var arrival = door.position + door.rotation * Vector3.forward * pastDoor;
            arrival.y = floorY;
            position = arrival - rotation * nextSpawn;
        }

        /// <summary>
        /// True when the head has stepped through the doorway between two frames: it was on the
        /// visitor's side of the threshold (door-local z &lt; 0), is now past it (z &gt;= 0), and passed
        /// within the opening's half-width. Walking round the side of a door does not count.
        /// </summary>
        public static bool Crossed(Vector3 previousLocal, Vector3 currentLocal, float halfWidth)
        {
            if (!(previousLocal.z < 0f && currentLocal.z >= 0f)) return false;
            float dz = currentLocal.z - previousLocal.z;
            float t = dz > 1e-6f ? -previousLocal.z / dz : 0f;
            float x = Mathf.Lerp(previousLocal.x, currentLocal.x, t);
            return Mathf.Abs(x) <= halfWidth;
        }
    }
}
