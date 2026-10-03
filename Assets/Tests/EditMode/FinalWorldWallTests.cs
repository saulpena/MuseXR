using System.IO;
using MusePico.Dialogue;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    public class FinalWorldWallTests
    {
        static ArtworkCatalogData Catalog() =>
            ArtworkCatalog.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "Museum/artworks.json")));

        [Test]
        public void TheTopTwoAxesPickHerArtist()
        {
            // Her PHILOSOPHY_QUERIES, keyed by philosophyKey().
            Assert.AreEqual("Claude Monet", FinalWorldWall.ArtistFor("emotion+perception"));
            Assert.AreEqual("Wassily Kandinsky", FinalWorldWall.ArtistFor("invention+perception"));
            Assert.AreEqual("Vincent van Gogh", FinalWorldWall.ArtistFor("emotion+invention"));
        }

        [Test]
        public void TheFinalWallIsFourWorksByThatArtistThenHerStudy()
        {
            var wall = FinalWorldWall.Works(Catalog(), new PhilosophyAxes(1, 0, 3));   // invention + perception
            Assert.AreEqual(5, wall.Count);
            for (var i = 0; i < 4; i++) StringAssert.Contains("Kandinsky", wall[i].artist);
            Assert.AreEqual("future-being", wall[4].id, "her final study closes the wall");
        }

        [Test]
        public void EveryChapterThatHasHerStudyHangsIt()
        {
            // Her wallFor: chapters 04-08 and the final world close on an AI interpretive study.
            var c = Catalog();
            foreach (var scene in new[] { "sunset-frames", "burning-sky", "petal-transition", "living-memory", "infinite-repetition", "personal-dream-world" })
            {
                var works = ArtworkCatalog.For(c, scene);
                Assert.IsTrue(FinalWorldWall.IsStudy(works[works.Count - 1]), scene + " should end on her study");
            }
        }
    }
}
