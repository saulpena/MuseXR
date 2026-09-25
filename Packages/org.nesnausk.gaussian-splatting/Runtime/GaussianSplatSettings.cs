// SPDX-License-Identifier: MIT
// MuseXR local addition to the embedded package.

using UnityEngine;

namespace GaussianSplatting.Runtime
{
    /// <summary>
    /// Settings read by the URP render feature every frame, so they can change at runtime.
    /// </summary>
    public static class GaussianSplatSettings
    {
        /// <summary>
        /// Resolution of the splat layer relative to the camera target, per axis. 1 is the
        /// package's original path, byte for byte: splats render into a target the size of the
        /// camera's and are depth-tested against the camera's own depth. Below 1 the splats render
        /// into a smaller target, depth-tested against a reduced copy of the camera depth (the
        /// farthest depth of each block, so geometry keeps hiding the splats behind it), and are
        /// upsampled bilinearly in the composite. Everything else in the frame (UI, meshes, text)
        /// is untouched. Clamped to [MinScale, 1]; the depth copy samples 2x2, which covers a
        /// low-res pixel fully only down to 0.5.
        /// </summary>
        public static float ResolutionScale
        {
            get => s_ResolutionScale;
            set => s_ResolutionScale = Mathf.Clamp(value, MinScale, 1f);
        }

        public const float MinScale = 0.5f;

        static float s_ResolutionScale = 1f;

        internal static bool IsScaled => s_ResolutionScale < 0.999f;
    }
}
