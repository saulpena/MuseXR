using System.Collections.Generic;
using System.Text;
using MusePico.Dialogue;
using NUnit.Framework;

namespace MusePico.Tests
{
    public class VrJourneyTests
    {
        static List<VrStage> Walk(VrJourney j)
        {
            var seen = new List<VrStage> { j.Current };
            while (j.Advance()) seen.Add(j.Current);
            return seen;
        }

        [Test]
        public void HerDemoRouteIsGateVanGoghMonetTableYourWorld()
        {
            CollectionAssert.AreEqual(
                new[] { VrStage.Gate, VrStage.VanGogh, VrStage.Monet, VrStage.Table, VrStage.YourWorld },
                Walk(new VrJourney(demo: true)));
        }

        [Test]
        public void TheFullRouteVisitsAllNineWhenThePalaceAndGrottoExist()
        {
            var j = new VrJourney { PalaceAvailable = true, GrottoAvailable = true };
            CollectionAssert.AreEqual(
                new[] { VrStage.Gate, VrStage.Company, VrStage.Curate, VrStage.Palace, VrStage.Grotto,
                        VrStage.VanGogh, VrStage.Monet, VrStage.Table, VrStage.YourWorld },
                Walk(j));
        }

        [Test]
        public void HerP1ChaptersAreSkippedUntilTheirWorldsExist()
        {
            CollectionAssert.DoesNotContain(Walk(new VrJourney()), VrStage.Palace);
            CollectionAssert.DoesNotContain(Walk(new VrJourney()), VrStage.Grotto);
        }

        [Test]
        public void TheDefaultTrioIsHers()
        {
            CollectionAssert.AreEqual(new[] { "monet", "van_gogh", "socrates" }, new VrJourney().Record.Companions);
        }

        [Test]
        public void CompanionsStayBetweenOneAndThree()
        {
            var j = new VrJourney();
            Assert.IsFalse(j.ToggleCompanion("frida"), "a fourth is refused");
            Assert.IsTrue(j.ToggleCompanion("socrates"));
            Assert.IsTrue(j.ToggleCompanion("frida"));
            Assert.IsTrue(j.ToggleCompanion("monet"));
            Assert.IsTrue(j.ToggleCompanion("van_gogh"));
            Assert.IsFalse(j.ToggleCompanion("frida"), "the last one cannot be dropped");
            CollectionAssert.AreEqual(new[] { "frida" }, j.Record.Companions);
        }

        [Test]
        public void YourWorldGetsAPlinthOnlyForChaptersActuallyDone()
        {
            var j = new VrJourney(demo: true);
            Walk(j);
            CollectionAssert.AreEqual(new[] { VrStage.VanGogh, VrStage.Monet }, j.Record.ChaptersDone);
        }

        [Test]
        public void FourSecondsOfGazeIsSeenAndLongestComesFirst()
        {
            var r = new JourneyRecord();
            r.AddDwell("aic-16568", 2f);
            r.AddDwell("aic-28560", 3.9f);
            r.AddDwell("aic-16568", 12f);
            var seen = r.Seen();
            Assert.AreEqual(1, seen.Count);
            Assert.AreEqual("aic-16568", seen[0].id);
            Assert.AreEqual(14f, seen[0].sec, 1e-4f);
        }

        [Test]
        public void AStrokeKeepsAtMost256PointsIncludingBothEnds()
        {
            var r = new JourneyRecord();
            var pts = new List<float[]>();
            for (var i = 0; i < 1000; i++) pts.Add(new float[] { i, 0, 0 });
            r.SetVanGogh("#2f4f8f", "aic-28560", pts);
            Assert.AreEqual(256, r.VanGogh.Points.Count);
            Assert.AreEqual(0f, r.VanGogh.Points[0][0]);
            Assert.AreEqual(999f, r.VanGogh.Points[255][0]);
        }

        [Test]
        public void TheRoundtableGetsTextOnlyUnderTwoKilobytesEvenWhenEverythingIsFull()
        {
            var r = new JourneyRecord();
            var longText = "a \"quoted\" \n line " + new string('x', 900);
            r.SetQuestion(longText);
            r.SetCompanions(new[] { "monet", "van_gogh", "socrates" });
            r.SetPalace(new JourneyRecord.PalaceChoice { Object = "crane", YawDeg = 35, Mode = "miniature", Reason = longText });
            r.SetGrotto(new JourneyRecord.GrottoChoice { LampSlot = "detail", ExhibitId = "aic-142512" });
            var pts = new List<float[]>(); for (var i = 0; i < 400; i++) pts.Add(new float[] { 1.234f, 5.678f, 9.1011f });
            r.SetVanGogh("#2f4f8f", "aic-28560", pts);
            r.SetMonet(new JourneyRecord.MonetChoice { Preset = "dusk", ArtworkId = "aic-16568", Reason = longText });
            for (var i = 0; i < 20; i++) r.AddDwell("aic-" + i, 5f + i);
            r.FinalAnswer.Draft = longText; r.FinalAnswer.Final = longText; r.FinalAnswer.RewrittenBy = "monet";

            var json = r.SummaryJson();
            Assert.LessOrEqual(Encoding.UTF8.GetByteCount(json), JourneyRecord.MaxSummaryBytes);
            StringAssert.DoesNotContain("points", json);
            StringAssert.DoesNotContain("1.234", json, "no stroke coordinates leave the headset");
            StringAssert.Contains("\"preset\":\"dusk\"", json);
            StringAssert.Contains("\\\"quoted\\\"", json, "quotes are escaped, so the payload stays valid JSON");
        }

        [Test]
        public void StartAgainClearsTheRecordAndReturnsToTheGate()
        {
            var j = new VrJourney(demo: true);
            j.Record.SetQuestion("What is worth keeping?");
            Walk(j);
            j.Reset();
            Assert.AreEqual(VrStage.Gate, j.Current);
            Assert.AreEqual(string.Empty, j.Record.Question);
            Assert.AreEqual(0, j.Record.ChaptersDone.Count);
            CollectionAssert.AreEqual(VrJourney.DefaultCompany, j.Record.Companions);
        }
    }
}
