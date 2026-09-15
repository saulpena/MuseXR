using UnityEngine;

namespace MusePico.Worlds
{
    /// <summary>
    /// Where to stand in a splat world that carries no measured spawn.
    ///
    /// World Labs' CDN samples come without the semantics_metadata the Marble API returns, so
    /// there is no ground plane and no metric scale to read — unlike Skylar's worlds, whose
    /// spawns were playtested over 27 commits in muse-infinity/config/worlds.js. Everything here
    /// is therefore a heuristic, and each constant is the result of looking at the render rather
    /// than of reasoning:
    ///
    ///   - MEASURED on the five samples: the ground is near the TOP of the bounds, not the
    ///     bottom. Interiors sit around -2.2..+1.3 and the two outdoor captures run to -47.6
    ///     and -19.6 in Y while topping out just under +1. The deep negative extent is sky-facing
    ///     floaters and capture noise below the walkable surface, so a "fraction of the height"
    ///     eye level — which is what MuseXR's cycler used — puts the viewer 20 m underground in
    ///     the outdoor worlds. Anchoring to the top instead works for both kinds.
    ///   - The XR Origin is FLOOR-tracked, so the headset adds its own eye height on top of
    ///     wherever the origin is put. Placing the origin at eye height double-counts it and
    ///     lifts the camera through the ceiling. The head offset is subtracted here instead.
    ///   - Standing at the exact centre of the bounds puts the camera inside furniture; backing
    ///     off the full 45% of the extent that MuseXR used puts it inside the back wall. A
    ///     quarter of the depth clears the furniture without reaching the wall.
    ///
    /// Pure arithmetic, no MonoBehaviour and no scene, so it is testable in EditMode.
    /// </summary>
    public static class SplatPlacement
    {
        /// <summary>How far below the top of the bounds the eye sits, in metres.</summary>
        public const float EyeBelowTop = 1.35f;

        /// <summary>Never put the eye closer than this to the bottom of the bounds.</summary>
        public const float MinHeightAboveFloor = 0.5f;

        /// <summary>Fraction of the depth to step back from the centre.</summary>
        public const float StandBackFraction = 0.25f;

        /// <summary>
        /// The pose to put the XR Origin at so the world is in front of the viewer.
        ///
        /// <paramref name="headHeight"/> is the camera's height above the origin as the tracking
        /// system reports it — pass 0 for a non-tracked camera. Bounds are the asset's own, in
        /// its local space, BEFORE worldScale is applied.
        /// </summary>
        public static void FromBounds(
            Vector3 boundsMin, Vector3 boundsMax, float worldScale, float headHeight,
            out Vector3 position, out Quaternion rotation)
        {
            var min = boundsMin * worldScale;
            var max = boundsMax * worldScale;
            var centre = (min + max) * 0.5f;
            var size = max - min;

            // Eye just below the top of the capture, but never underneath its floor.
            float eyeY = Mathf.Max(max.y - EyeBelowTop * worldScale,
                                   min.y + MinHeightAboveFloor * worldScale);

            float back = size.z * StandBackFraction;
            var eye = new Vector3(centre.x, eyeY, centre.z - back);
            var lookAt = new Vector3(centre.x, eyeY, centre.z);

            rotation = eye == lookAt
                ? Quaternion.identity
                : Quaternion.LookRotation(lookAt - eye, Vector3.up);

            // The headset lifts the camera off the origin by itself; place the origin low enough
            // that the eye lands where it was asked to.
            position = new Vector3(eye.x, eye.y - headHeight, eye.z);
        }

        /// <summary>
        /// A far clip that actually contains the world. A fixed 300 m clipped the larger outdoor
        /// samples and wasted depth precision on the interiors, so it is derived instead: the
        /// diagonal of the scaled bounds plus the stand-back distance, with a floor of 50 m so a
        /// tiny world still has somewhere to put its skybox.
        /// </summary>
        public static float FarClipFor(Vector3 boundsMin, Vector3 boundsMax, float worldScale)
        {
            var size = (boundsMax - boundsMin) * worldScale;
            float diagonal = size.magnitude;
            float back = size.z * StandBackFraction;
            return Mathf.Max(50f, (diagonal + back) * 1.5f);
        }
    }
}
