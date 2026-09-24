using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>
    /// One public-domain work, as muse-infinity records it.
    ///
    /// <b>The rights line is not decoration.</b> These are museum Open Access images and the
    /// attribution travels with the work — dropping it while keeping the picture is the one thing
    /// this port must not do.
    /// </summary>
    [Serializable]
    public class ArtworkRecord
    {
        public string id;
        public string title;
        public string artist;
        public string date;
        public string source;
        public string sourceUrl;
        public string rights;

        public override string ToString() =>
            string.IsNullOrEmpty(artist) ? title : title + " · " + artist;
    }

    [Serializable]
    public class ChapterCollection
    {
        public string sceneId;
        public ArtworkRecord[] works = new ArtworkRecord[0];
    }

    [Serializable]
    public class ArtworkCatalogData
    {
        public ChapterCollection[] chapters = new ChapterCollection[0];
    }

    /// <summary>
    /// Which four works hang in which chapter, ported from muse-infinity's
    /// <c>config/sceneCollections.js</c>.
    ///
    /// <b>Her gallery is not one wall.</b> <c>syncSceneWall</c> swaps <c>scene.artworks</c> every
    /// time the visitor steps to the next chapter, so the eight rooms of the spine hold
    /// **thirty-six different works**, four at a time. Ours hung eight, once, and never changed
    /// them — the same pictures in the conservatory, the water garden and the van Gogh gallery.
    ///
    /// The images were already here; only the records were missing. An earlier note in this project
    /// claimed we had no artwork metadata at all, which was simply wrong: it has been sitting in
    /// her config the whole time, complete with titles, artists, dates and rights.
    ///
    /// Pure: parsing and lookup only, no scene and no textures. The renderer resolves an
    /// <see cref="ArtworkRecord.id"/> to a picture the same way the portraits are resolved.
    /// </summary>
    public static class ArtworkCatalog
    {
        /// <summary>How many works hang in a chapter. Hers is four everywhere.</summary>
        public const int PerChapter = 4;

        public static ArtworkCatalogData Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new ArtworkCatalogData();
            var data = JsonUtility.FromJson<ArtworkCatalogData>(json);
            return data ?? new ArtworkCatalogData();
        }

        /// <summary>
        /// The works for a chapter, by her collection key.
        ///
        /// Returns an empty list rather than null for an unknown key, because a chapter with no
        /// collection must leave bare walls, not throw — which is exactly what her <c>wallFor</c>
        /// guards against ("A missing collection must never leave a world with bare walls", and
        /// she falls back to the three local records).
        /// </summary>
        public static IReadOnlyList<ArtworkRecord> For(ArtworkCatalogData catalog, string collectionId)
        {
            var empty = new List<ArtworkRecord>();
            if (catalog?.chapters == null || string.IsNullOrEmpty(collectionId)) return empty;

            foreach (var chapter in catalog.chapters)
            {
                if (chapter == null || chapter.works == null) continue;
                if (!string.Equals(chapter.sceneId, collectionId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var works = new List<ArtworkRecord>();
                foreach (var w in chapter.works) if (w != null) works.Add(w);
                return works;
            }
            return empty;
        }

        /// <summary>Every work in the catalogue, for counting and for checking nothing is orphaned.</summary>
        public static IReadOnlyList<ArtworkRecord> All(ArtworkCatalogData catalog)
        {
            var all = new List<ArtworkRecord>();
            if (catalog?.chapters == null) return all;

            foreach (var chapter in catalog.chapters)
            {
                if (chapter?.works == null) continue;
                foreach (var w in chapter.works) if (w != null) all.Add(w);
            }
            return all;
        }
    }
}
