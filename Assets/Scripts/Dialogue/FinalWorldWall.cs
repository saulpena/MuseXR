using System;
using System.Collections.Generic;

namespace MusePico.Dialogue
{
    /// <summary>
    /// What hangs in the visitor's own world after ENTER YOUR WORLD.
    ///
    /// Hers (app.js initMuseumExperience): the final scene first hangs its own collection, then asks
    /// the Art Institute for the artist the visitor's two strongest philosophy axes name
    /// (<c>PHILOSOPHY_QUERIES[philosophyKey()]</c>: Monet, Kandinsky or Van Gogh) and re-hangs four of
    /// those works plus the final interpretive study, "A World Still Becoming". That live fetch is
    /// the payoff of every answer the visitor gave at a painting and at Socrates' question.
    ///
    /// Ours has no live fetch (the Art Institute's image server refuses downloads), so the same
    /// three artists come from the collections already in the project: the chapter that hangs that
    /// artist. Same key, same artist, works that exist.
    /// </summary>
    public static class FinalWorldWall
    {
        /// <summary>Her final scene's own collection, and where its interpretive study lives.</summary>
        public const string FinalCollection = "personal-dream-world";

        /// <summary>Her PHILOSOPHY_QUERIES artist for a philosophy key, or null.</summary>
        public static string ArtistFor(string key)
        {
            switch (key)
            {
                case "emotion+perception": return "Claude Monet";
                case "invention+perception": return "Wassily Kandinsky";
                case "emotion+invention": return "Vincent van Gogh";
                default: return null;
            }
        }

        /// <summary>The chapter collection in this project that hangs that artist, or null.</summary>
        public static string CollectionFor(string key)
        {
            switch (key)
            {
                case "emotion+perception": return "water-and-light";        // Monet
                case "invention+perception": return "infinite-repetition";  // Kandinsky
                case "emotion+invention": return "burning-sky";             // Van Gogh
                default: return null;
            }
        }

        /// <summary>
        /// Four works by the visitor's artist, then the final study: her
        /// <c>[...live.slice(0, 4), study]</c>. Falls back to the final scene's own wall when the key
        /// names no artist or the collection is missing, as hers keeps its static wall on a failed fetch.
        /// </summary>
        public static List<ArtworkRecord> Works(ArtworkCatalogData catalog, PhilosophyAxes axes)
        {
            var own = ArtworkCatalog.For(catalog, FinalCollection);
            ArtworkRecord study = null;
            foreach (var w in own) if (IsStudy(w)) { study = w; break; }

            var wall = new List<ArtworkRecord>();
            var collection = CollectionFor(axes.Key());
            if (collection != null)
                foreach (var w in ArtworkCatalog.For(catalog, collection))
                    if (!IsStudy(w) && wall.Count < 4) wall.Add(w);

            if (wall.Count == 0) return new List<ArtworkRecord>(own);
            if (study != null) wall.Add(study);
            return wall;
        }

        /// <summary>One of her AI interpretive studies rather than a real work.</summary>
        public static bool IsStudy(ArtworkRecord w) =>
            w != null && string.Equals(w.source, "MUSE visual study", StringComparison.OrdinalIgnoreCase);
    }
}
