// SPDX-License-Identifier: MIT
// MuseXR local addition to the embedded package.

using UnityEngine;

namespace GaussianSplatting.Runtime
{
    /// <summary>
    /// Which side of the one active portal a splat renderer belongs to.
    /// </summary>
    public enum SplatPortalRole
    {
        /// <summary>No portal involvement: drawn everywhere, as before.</summary>
        None,
        /// <summary>The world the visitor stands in. Hidden where the door shows the other world.</summary>
        Outside,
        /// <summary>The world beyond the door. Drawn only inside the door, beyond its plane.</summary>
        Through,
    }

    /// <summary>
    /// The single active splat portal, read by the renderers and the URP feature every frame. Set
    /// by whatever owns the door (MuseXR: <c>SplatPortalDoor</c>). One door at a time is the whole
    /// design: two worlds, one threshold.
    ///
    /// How the two worlds share the screen: each eye renders a MASK — the door's aperture quad —
    /// into a texture the size of the splat layer. The Outside world drops a splat's pixels where
    /// the mask is set AND the splat lies beyond the door plane; the Through world keeps only
    /// those. The two rules are exact complements, so the worlds never overlap. Nothing uses the
    /// stencil buffer, which the reduced-resolution splat layer's depth copy would not carry.
    /// </summary>
    public static class SplatPortal
    {
        public static bool Active;

        /// <summary>World → door space (see <see cref="PortalGeometry"/>).</summary>
        public static Matrix4x4 WorldToDoor = Matrix4x4.identity;

        /// <summary>Door → world. Scaled by the aperture it draws the mask quad.</summary>
        public static Matrix4x4 DoorToWorld = Matrix4x4.identity;

        /// <summary>Half the aperture's width and height, metres.</summary>
        public static Vector2 HalfSize = new(1f, 1.5f);

        /// <summary>
        /// Through-world splats whose centre is seen further than this outside the aperture are
        /// culled whole in the compute pass, before any pixel work. Must exceed the largest splat
        /// footprint the door frame has to hide, or edges thin out.
        /// </summary>
        public static float CullMargin = 1.5f;

        /// <summary>Distance from the plane inside which the mask becomes the whole screen.</summary>
        public static float CrossingZone = 0.12f;

        /// <summary>
        /// Hidden/Gaussian Splatting/Portal Mask. Its properties (_DoorOn, _Seep, ...) shape what
        /// the door shows; the door's owner animates them. Assigned by the door's owner.
        /// </summary>
        public static Material MaskMaterial;

        /// <summary>A unit quad in XY (Unity's Quad primitive mesh).</summary>
        public static Mesh MaskMesh;

        /// <summary>
        /// Half size of the quad the mask shape is drawn on, metres. Must contain the aperture and
        /// the whole seep patch. Zero means "the aperture".
        /// </summary>
        public static Vector2 MaskHalfSize;

        /// <summary>
        /// Also erase SOLID geometry that lies beyond the door plane inside the shape, so a mesh
        /// the door is set into (the Buddha) does not hide the world behind it. Needed only while
        /// the visitor is on the approach side: afterwards everything beyond the door belongs to
        /// the world being left, including the door's own frame and leaves, which must stay.
        /// </summary>
        public static bool EraseMeshes;

        internal static bool Drawable => Active && MaskMaterial != null && MaskMesh != null;

        internal static Matrix4x4 MaskMatrix
        {
            get
            {
                var half = MaskHalfSize.x > 0f && MaskHalfSize.y > 0f ? MaskHalfSize : HalfSize;
                return DoorToWorld * Matrix4x4.Scale(new Vector3(half.x * 2f, half.y * 2f, 1f));
            }
        }

        public static Vector3 ToDoor(Vector3 world) => WorldToDoor.MultiplyPoint3x4(world);
    }
}
