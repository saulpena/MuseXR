using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using MusePico.Dialogue;

namespace MusePico.Tests
{
    /// <summary>
    /// The per-chapter walls, against muse-infinity's <c>config/sceneCollections.js</c>.
    ///
    /// The case these exist for: her gallery swaps its four works every time the visitor steps to
    /// the next chapter, so the spine holds thirty-six. Ours hung eight, once, and showed the same
    /// eight in every room.
    /// </summary>
    public class ArtworkCatalogTests
    {
        const string Path = "Assets/Museum/artworks.json";

        static ArtworkCatalogData Load() => ArtworkCatalog.Parse(File.ReadAllText(Path));

        [Test]
        public void TheCatalogueIsElevenChaptersOfFour()
        {
            var catalog = Load();

            Assert.AreEqual(11, catalog.chapters.Length, "eight chapters plus the finale, and her Palace and Grotto (design doc, 2 Oct 2026)");
            foreach (var chapter in catalog.chapters)
                Assert.AreEqual(ArtworkCatalog.PerChapter, chapter.works.Length,
                    chapter.sceneId + " should hang four works");

            Assert.AreEqual(44, ArtworkCatalog.All(catalog).Count);
        }

        [Test]
        public void EverySpineChapterFindsItsCollection()
        {
            var catalog = Load();

            foreach (var chapter in ExhibitionSpine.Chapters)
            {
                Assert.IsNotEmpty(chapter.CollectionId ?? string.Empty,
                    chapter.Chapter + " has no collection id");

                var works = ArtworkCatalog.For(catalog, chapter.CollectionId);
                Assert.AreEqual(ArtworkCatalog.PerChapter, works.Count,
                    chapter.Chapter + " -> " + chapter.CollectionId + " found no wall");
            }

            var final = ArtworkCatalog.For(catalog, ExhibitionSpine.Final.CollectionId);
            Assert.AreEqual(ArtworkCatalog.PerChapter, final.Count, "the dream world hangs works too");
        }

        [Test]
        public void TheChaptersHangDIFFERENTWorks_WhichIsTheWholePoint()
        {
            var catalog = Load();
            var seen = new HashSet<string>();

            foreach (var chapter in catalog.chapters)
                foreach (var work in chapter.works)
                    Assert.IsTrue(seen.Add(work.id),
                        work.id + " hangs in two chapters; the walk would repeat itself");
        }

        [Test]
        public void EveryWorkCarriesItsAttribution()
        {
            // Public-domain museum images travel with their rights line. Keeping the picture and
            // dropping the credit is the one thing this port must not do.
            foreach (var work in ArtworkCatalog.All(Load()))
            {
                Assert.IsNotEmpty(work.id, "a work with no id cannot find its image");
                Assert.IsNotEmpty(work.title, work.id + " has no title");
                Assert.IsNotEmpty(work.artist, work.id + " has no artist");
                Assert.IsNotEmpty(work.rights, work.id + " has no rights line");
                Assert.IsNotEmpty(work.sourceUrl, work.id + " has no source url");
            }
        }

        [Test]
        public void EveryWorkHasAnImageOnDisk()
        {
            foreach (var work in ArtworkCatalog.All(Load()))
                Assert.IsTrue(File.Exists("Assets/Museum/Artworks/" + work.id + ".jpg"),
                    "no image staged for " + work.id);
        }

        [Test]
        public void HerCollectionKeysAreNotTheSpineIds_WhichIsWhyTheJoinIsStated()
        {
            // If these ever became equal, someone could be tempted to derive one from the other.
            // They are not, and deriving would hang the wrong pictures in five of eight chapters.
            var differing = 0;
            foreach (var chapter in ExhibitionSpine.Chapters)
                if (chapter.Id != chapter.CollectionId) differing++;

            Assert.GreaterOrEqual(differing, 5,
                "the two id schemes genuinely differ; keep the mapping explicit");
        }

        [Test]
        public void AnUnknownChapterLeavesBareWallsRatherThanThrowing()
        {
            var catalog = Load();

            Assert.IsEmpty(ArtworkCatalog.For(catalog, "no-such-chapter"));
            Assert.IsEmpty(ArtworkCatalog.For(catalog, null));
            Assert.IsEmpty(ArtworkCatalog.For(null, "threshold-conservatory"));
            Assert.IsEmpty(ArtworkCatalog.All(null));
            Assert.IsEmpty(ArtworkCatalog.Parse(null).chapters);
        }

        [Test]
        public void TheOpeningChapterIsHersExactly()
        {
            var works = ArtworkCatalog.For(Load(), "threshold-conservatory");

            Assert.AreEqual("Woman Bathing Her Feet in a Brook", works[0].title);
            Assert.AreEqual("Camille Pissarro", works[0].artist);
            StringAssert.Contains("Art Institute of Chicago", works[0].rights);
        }
    }
}
