using MuseXR.Interaction;
using NUnit.Framework;

namespace MusePico.Tests
{
    /// <summary>Saul, 5 Oct 2026: the choice's brief lives in the compass, posted by whoever owns the choice.</summary>
    public class CompassBriefTests
    {
        [SetUp] public void Fresh() => CompassBrief.Reset();
        [TearDown] public void Clean() => CompassBrief.Reset();

        static CompassBrief.Row[] Two(bool firstDone) => new[]
        {
            new CompassBrief.Row { Label = "The crane", Done = firstDone, State = firstDone ? "heard" : "point to hear" },
            new CompassBrief.Row { Label = "The turtle", Done = false, State = "point to hear" },
        };

        [Test]
        public void ShowingPostsItsWordsAndRows()
        {
            var owner = new object();
            CompassBrief.Show(owner, "Stop 1", "Before you choose", Two(false));
            Assert.IsTrue(CompassBrief.Showing);
            Assert.AreEqual("Stop 1", CompassBrief.Kicker);
            Assert.AreEqual(2, CompassBrief.Rows.Count);
            Assert.IsFalse(CompassBrief.Rows[0].Done);
        }

        [Test]
        public void EveryPostChangesTheVersionSoTheCompassRedraws()
        {
            var owner = new object();
            CompassBrief.Show(owner, "Stop 1", "Before you choose", Two(false));
            var v = CompassBrief.Version;
            CompassBrief.Show(owner, "Stop 1", "Before you choose", Two(true));
            Assert.AreNotEqual(v, CompassBrief.Version);
            Assert.IsTrue(CompassBrief.Rows[0].Done);
        }

        [Test]
        public void OnlyTheOwnerCanTakeItDown()
        {
            var palace = new object(); var grotto = new object();
            CompassBrief.Show(palace, "Stop 1", "a", Two(false));
            CompassBrief.Show(grotto, "Stop 2", "b", Two(false));   // the next chapter's choice took over
            CompassBrief.Hide(palace);                              // the old one closing late must not clear it
            Assert.IsTrue(CompassBrief.Showing);
            Assert.AreEqual("Stop 2", CompassBrief.Kicker);
            CompassBrief.Hide(grotto);
            Assert.IsFalse(CompassBrief.Showing);
            Assert.AreEqual(0, CompassBrief.Rows.Count);
        }

        [Test]
        public void ANudgeCountsOnlyFromTheOwner()
        {
            var owner = new object();
            CompassBrief.Show(owner, "Stop 1", "a", Two(false));
            var n = CompassBrief.Nudges;
            CompassBrief.Nudge(new object());
            Assert.AreEqual(n, CompassBrief.Nudges);
            CompassBrief.Nudge(owner);
            Assert.AreEqual(n + 1, CompassBrief.Nudges);
        }
    }
}
