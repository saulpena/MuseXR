using System;

namespace MuseXR.UI
{
    /// <summary>
    /// Her CSS pixels to metres. Her rule (section 2, shared placement): body text at least 1 degree
    /// of visual angle - "about 3.5 cm cap at 2 m". Her body text is 14px, and Inter's cap height is
    /// 0.727 em, so a panel read from distance d gets metres-per-pixel = d * tan(1 deg) / (14 * 0.727).
    /// At 2 m that is 3.4 mm a pixel: her 440px artwork card is about 1.5 m wide.
    /// </summary>
    public static class PanelScale
    {
        public const float CapHeightEm = 0.727f;   // Inter, measured from the font's metrics
        public const float MinBodyDegrees = 1f;

        public static float MetresPerPixel(float viewingDistance, float bodyPx = MuseTheme.BodyPx)
        {
            if (viewingDistance <= 0f) throw new ArgumentOutOfRangeException(nameof(viewingDistance));
            double capMetres = viewingDistance * Math.Tan(MinBodyDegrees * Math.PI / 180.0);
            return (float)(capMetres / (bodyPx * CapHeightEm));
        }

        /// <summary>The visual angle, degrees, of a cap of <paramref name="px"/>-pixel text on a panel
        /// scaled for <paramref name="viewingDistance"/>, read from <paramref name="actualDistance"/>.</summary>
        public static float CapDegrees(float px, float viewingDistance, float actualDistance)
        {
            double cap = px * CapHeightEm * MetresPerPixel(viewingDistance);
            return (float)(Math.Atan(cap / actualDistance) * 180.0 / Math.PI);
        }
    }
}
