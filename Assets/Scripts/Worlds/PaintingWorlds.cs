namespace MuseXR.Worlds
{
    /// <summary>
    /// Paintings the visitor can step inside: an artwork id from muse-infinity's catalogue, and the
    /// splat world made from it (a Marble Chisel world, styled on the painting). Separate from the
    /// doors: a door leads on to the next chapter, a painting is a detour you come back out of.
    /// </summary>
    public static class PaintingWorlds
    {
        /// <summary>Camille Pissarro, Woman Bathing Her Feet in a Brook (1894-95). Compass stop 1 in
        /// the conservatory. Made in Marble's Chisel on 1 Oct 2026.</summary>
        public const string PissarroBrook = "pissarro-woman-bathing-brook";

        /// <summary>The world key (with the 500k suffix) for <paramref name="artworkId"/>, or null when
        /// that painting has no world.</summary>
        public static string For(string artworkId)
        {
            switch (artworkId)
            {
                case "aic-153799": return PissarroBrook + WorldCatalog.SmallSuffix;
                default: return null;
            }
        }
    }
}
