using System.Linq;
using MusePico.Dialogue;
using NUnit.Framework;

namespace MusePico.Tests
{
    public class ChapterDiagramsTests
    {
        [Test]
        public void TheEntryMapsToTheOrigin()
        {
            foreach (var d in ChapterDiagrams.All)
            {
                var (x, z) = ChapterDiagrams.ToLocal(d, new DiagramItem("e", DiagramKind.Entry, d.EntryX, d.EntryY, ""), 0.05f, 0.05f);
                Assert.AreEqual(0f, x, 1e-5f, d.Chapter); Assert.AreEqual(0f, z, 1e-5f, d.Chapter);
            }
        }

        [Test]
        public void UpThePageIsAheadAndRightOfThePageIsRight()
        {
            var d = ChapterDiagrams.VanGogh;
            var (bx, bz) = ChapterDiagrams.ToLocal(d, d.Find("aic-28560"), 0.05f, 0.05f);   // Bedroom, left wall
            var (px, pz) = ChapterDiagrams.ToLocal(d, d.Find("aic-14586"), 0.05f, 0.05f);   // Poet's Garden, further up
            Assert.Less(bx, 0f, "her left wall is on the visitor's left");
            Assert.Greater(pz, bz, "Poet's Garden is further down the corridor than The Bedroom");
            var (sx, _) = ChapterDiagrams.ToLocal(d, d.Find("side-door"), 0.05f, 0.05f);
            Assert.Greater(sx, 0f, "the side door is to the right");
        }

        [Test]
        public void ThePalaceHasHerTwelveDragonColumnsAndTheMoonGateOnTheRight()
        {
            var d = ChapterDiagrams.Palace;
            Assert.AreEqual(12, d.Items.Count(i => i.Kind == DiagramKind.Column));
            var gate = d.Find("moon-gate");
            Assert.AreEqual(WallSide.Right, gate.Wall);
            Assert.Greater(ChapterDiagrams.ToLocal(d, gate, 0.05f, 0.05f).z, ChapterDiagrams.ToLocal(d, d.Find("miniature-court"), 0.05f, 0.05f).z,
                           "the gate is in the back half, beyond the court");
        }

        [Test]
        public void EveryChapterHasHerThreeCompanionMarksAndOneExit()
        {
            foreach (var d in ChapterDiagrams.All)
            {
                CollectionAssert.AreEquivalent(new[] { "monet", "van_gogh", "socrates" },
                    d.Items.Where(i => i.Kind == DiagramKind.Mark).Select(i => i.Id).ToArray(), d.Chapter);
                Assert.AreEqual(1, d.Items.Count(i => i.Kind == DiagramKind.Exit), d.Chapter);
            }
        }

        [Test]
        public void HerChaptersCarryHerFourWorksEach()
        {
            Assert.AreEqual(4, ChapterDiagrams.VanGogh.Items.Count(i => i.Kind == DiagramKind.Work));
            Assert.AreEqual(4, ChapterDiagrams.Monet.Items.Count(i => i.Kind == DiagramKind.Work));
        }

        [Test]
        public void TheForwardConeIsHerSixtyDegreesAndNeverBehind()
        {
            Assert.IsTrue(ChapterDiagrams.WithinForwardCone(0f, 2f));
            Assert.IsTrue(ChapterDiagrams.WithinForwardCone(1.7f, 1f));    // ~59.5 degrees
            Assert.IsFalse(ChapterDiagrams.WithinForwardCone(2f, 1f));     // ~63 degrees
            Assert.IsFalse(ChapterDiagrams.WithinForwardCone(0f, -1f));
        }
    }
}
