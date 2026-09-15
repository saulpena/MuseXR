using UnityEngine;

namespace MusePico.Gallery
{
    /// <summary>Where one exhibit stands, and which way it faces.</summary>
    public struct GalleryPlacement
    {
        public Vector3 Position;
        public float YawDegrees;

        public Quaternion Rotation => Quaternion.Euler(0f, YawDegrees, 0f);
    }

    /// <summary>How to scale and lift one model so it sits correctly on its pedestal.</summary>
    public struct GalleryFit
    {
        /// <summary>Uniform scale to apply to the model root.</summary>
        public float Scale;
        /// <summary>Local offset to apply AFTER scaling, so the model's base rests on the pedestal top.</summary>
        public Vector3 Offset;
    }

    /// <summary>
    /// The arithmetic behind the exhibit ring, kept out of any MonoBehaviour so it can be tested
    /// in EditMode without a scene.
    ///
    /// Two problems, and the second is the one that actually bites. Arranging N plinths on an arc
    /// is easy. Making N models from a generator look like a considered exhibition is not: Tripo
    /// returns each mesh in its own arbitrary units and its own arbitrary origin — the five
    /// characters bundled in muse-infinity are not authored assets with a pivot at the feet, they
    /// are reconstructions whose bounds happen wherever the solve put them. Dropped into a scene
    /// unchanged they are different sizes and half of them float or sink.
    ///
    /// <see cref="Fit"/> therefore derives scale and offset from the model's own renderer bounds
    /// rather than from any authored transform, which is what makes a freshly generated mesh drop
    /// into the gallery looking deliberate.
    /// </summary>
    public static class GalleryLayout
    {
        /// <summary>
        /// Exhibits on an arc centred on the viewer, each turned to face inward.
        ///
        /// A single exhibit is placed straight ahead rather than at the arc's start angle, and
        /// the arc is centred on +Z, so the gallery reads the same regardless of how many
        /// entries the catalogue happens to have.
        /// </summary>
        /// <param name="index">0-based exhibit index.</param>
        /// <param name="count">Total exhibits. Values below 1 are treated as 1.</param>
        /// <param name="radius">Metres from the viewer to each pedestal.</param>
        /// <param name="arcDegrees">Total sweep. 180 puts the outermost pieces at the viewer's shoulders.</param>
        public static GalleryPlacement Arc(int index, int count, float radius = 3.5f, float arcDegrees = 170f)
        {
            if (count < 1) count = 1;
            if (index < 0) index = 0;

            var step = count == 1 ? 0f : arcDegrees / (count - 1);
            var angle = count == 1 ? 0f : -arcDegrees * 0.5f + step * index;

            var rad = angle * Mathf.Deg2Rad;
            var position = new Vector3(Mathf.Sin(rad) * radius, 0f, Mathf.Cos(rad) * radius);

            // Face back toward the viewer at the origin: the outward heading, turned 180.
            return new GalleryPlacement { Position = position, YawDegrees = angle + 180f };
        }

        /// <summary>
        /// Scale and offset that put an arbitrary model on a pedestal at a consistent height.
        ///
        /// <paramref name="modelBounds"/> are the model's combined renderer bounds in its own
        /// local space, unscaled. The returned offset places the BOTTOM of those bounds on
        /// <paramref name="pedestalTopY"/> and centres the model horizontally over the pedestal,
        /// so a mesh whose origin sits at its navel behaves like one whose origin sits at its feet.
        /// </summary>
        /// <param name="targetHeight">Metres the tallest axis should occupy. ~1.8 reads as life-size.</param>
        public static GalleryFit Fit(Bounds modelBounds, float targetHeight = 1.8f, float pedestalTopY = 0.9f)
        {
            var size = modelBounds.size;
            var tallest = Mathf.Max(size.y, Mathf.Max(size.x, size.z));

            // A degenerate mesh (empty, or a single point) would otherwise divide by zero and
            // scale to infinity, which in a headset is a full-screen grey wall with no clue why.
            var scale = tallest > 1e-4f ? targetHeight / tallest : 1f;

            var scaledCentre = modelBounds.center * scale;
            var scaledHalfHeight = size.y * 0.5f * scale;

            return new GalleryFit
            {
                Scale = scale,
                Offset = new Vector3(-scaledCentre.x, pedestalTopY - (scaledCentre.y - scaledHalfHeight), -scaledCentre.z),
            };
        }
    }
}
