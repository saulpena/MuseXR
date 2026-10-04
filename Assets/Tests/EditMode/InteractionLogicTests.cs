using System.Collections.Generic;
using System.Linq;
using MuseXR.Slots;
using NUnit.Framework;

namespace MusePico.Tests
{
    /// <summary>Company, companions, attention, cards, plinths and calibration, as logic (her §2.2, §3.4, 4.1).</summary>
    public class InteractionLogicTests
    {
        // ---- Invitation ------------------------------------------------------------------

        [Test]
        public void WithFourSelectedTheFourthIsRefused()
        {
            // Her check (2.2): "With four selected, the fourth is refused".
            var inv = new Invitation();
            Assert.AreEqual(Invitation.Result.Added, inv.Toggle(Masters.Frida));
            Assert.AreEqual(Invitation.Result.Added, inv.Toggle(Masters.Monet));
            Assert.AreEqual(Invitation.Result.Added, inv.Toggle(Masters.Hilma));
            Assert.AreEqual(Invitation.Result.Refused, inv.Toggle(Masters.Socrates));
            Assert.AreEqual(3, inv.Chosen.Count);
            Assert.IsFalse(inv.IsChosen(Masters.Socrates));
        }

        [Test]
        public void SelectingAChosenMasterAgainUninvitesThem()
        {
            var inv = new Invitation();
            inv.Toggle(Masters.Monet);
            Assert.AreEqual(Invitation.Result.Removed, inv.Toggle(Masters.Monet));
            Assert.IsFalse(inv.CanProceed, "at least one is needed");
            Assert.AreEqual(Invitation.Result.Unknown, inv.Toggle("rembrandt"), "only her seven can be invited");
            Assert.AreEqual(Invitation.Result.Added, inv.Toggle(Masters.Picasso), "Picasso is her seventh (updated script, 2 Oct 2026)");
        }

        [Test]
        public void SpeakingOrderIsObserveFeelQuestionWhateverOrderTheyWereInvited()
        {
            var inv = new Invitation();
            inv.Toggle(Masters.Socrates); inv.Toggle(Masters.Monet); inv.Toggle(Masters.VanGogh);
            CollectionAssert.AreEqual(new[] { Masters.Monet, Masters.VanGogh, Masters.Socrates }, inv.SpeakingOrder());
        }

        [Test]
        public void TheThreeWithNoAxisYetFollowTheTrioInInvitationOrder()
        {
            var inv = new Invitation();
            inv.Toggle(Masters.Morisot); inv.Toggle(Masters.Socrates); inv.Toggle(Masters.Frida);
            CollectionAssert.AreEqual(new[] { Masters.Socrates, Masters.Morisot, Masters.Frida }, inv.SpeakingOrder());
        }

        // ---- CompanionMarks --------------------------------------------------------------

        [Test]
        public void EveryMarkAndEveryFallbackIsInsideHerLimits()
        {
            // §3.4: 1.5-2.2 m, within ±60° of forward, never behind; and off the path.
            for (var order = 0; order < 3; order++)
                foreach (var m in CompanionMarks.Candidates(order))
                    Assert.IsTrue(CompanionMarks.Allowed(m), "order " + order + " candidate " + m);
        }

        [Test]
        public void TheThreeMarksAreDistinctAndFollowHerDiagram()
        {
            var m = Enumerable.Range(0, 3).Select(CompanionMarks.For).ToArray();
            Assert.Less(m[0].Bearing, 0f, "the first speaker stands left");
            Assert.Greater(m[1].Bearing, 0f, "the second right");
            Assert.Greater(m[2].Distance, m[1].Distance, "the third further off");
            for (var i = 0; i < 3; i++)
            for (var j = i + 1; j < 3; j++)
            {
                // At their distances, at least 0.6 m apart: nobody stands in anyone.
                float Rad(float d) => d * (float)System.Math.PI / 180f;
                float X(CompanionMarks.Mark k) => k.Distance * (float)System.Math.Sin(Rad(k.Bearing));
                float Z(CompanionMarks.Mark k) => k.Distance * (float)System.Math.Cos(Rad(k.Bearing));
                var gap = System.Math.Sqrt(System.Math.Pow(X(m[i]) - X(m[j]), 2) + System.Math.Pow(Z(m[i]) - Z(m[j]), 2));
                Assert.Greater(gap, 0.6, i + " and " + j);
            }
        }

        // ---- TurnTaking ------------------------------------------------------------------

        [Test]
        public void TheNextSpeakerWaitsUntilTheGazeIsBackWithin60Degrees()
        {
            var t = new TurnTaking(new[] { Masters.Monet, Masters.VanGogh });
            var started = new List<string>();
            t.Started += started.Add;
            t.Begin();
            t.Tick(0.1f, 120f);
            Assert.IsEmpty(started, "nobody speaks from behind");
            t.Tick(0.1f, 59f);
            CollectionAssert.AreEqual(new[] { Masters.Monet }, started);
        }

        [Test]
        public void LinesNeverOverlapAndAutoAdvanceAfterTwoSeconds()
        {
            var t = new TurnTaking(new[] { Masters.Monet, Masters.VanGogh, Masters.Socrates });
            var log = new List<string>();
            t.Started += id => log.Add("start " + id);
            t.Ended += id => log.Add("end " + id);
            var done = false;
            t.Finished += () => done = true;
            t.Begin();
            for (var i = 0; i < 3; i++)
            {
                t.Tick(0.1f, 10f);                  // starts
                t.LineFinished();
                t.Tick(1.9f, 10f);
                Assert.AreEqual(TurnTaking.Phase.Pausing, t.Current, "still pausing at 1.9 s");
                t.Tick(0.2f, 10f);                  // 2.1 s: next
            }
            Assert.IsTrue(done);
            CollectionAssert.AreEqual(new[]
            {
                "start monet", "end monet", "start van_gogh", "end van_gogh", "start socrates", "end socrates",
            }, log, "each line ends before the next starts");
        }

        [Test]
        public void WithOneSelectedExactlyOneLinePlays()
        {
            var t = new TurnTaking(new[] { Masters.Hilma });
            var lines = 0;
            t.Started += _ => lines++;
            t.Begin();
            for (var i = 0; i < 50; i++) { t.Tick(0.1f, 0f); if (t.Current == TurnTaking.Phase.Speaking) t.LineFinished(); }
            Assert.AreEqual(1, lines);
            Assert.AreEqual(TurnTaking.Phase.Done, t.Current);
        }

        [Test]
        public void AAdvancesWithoutWaitingForThePause()
        {
            var t = new TurnTaking(new[] { Masters.Monet, Masters.VanGogh });
            t.Begin(); t.Tick(0.1f, 0f); t.LineFinished();
            t.Advance();
            Assert.AreEqual(TurnTaking.Phase.WaitingForGaze, t.Current);
            Assert.AreEqual(Masters.VanGogh, t.Speaker);
        }

        // ---- ArtworkAttention ------------------------------------------------------------

        [Test]
        public void TheCardOpensAfterAPointFourSecondRayDwellOnce()
        {
            var a = new ArtworkAttention("aic-16568");
            var wanted = 0;
            a.CardWanted += _ => wanted++;
            for (var i = 0; i < 3; i++) a.Update(true, 9f, false, 0.12f);      // 0.36 s
            Assert.AreEqual(0, wanted);
            a.Update(true, 9f, false, 0.05f);                                     // 0.41 s
            Assert.AreEqual(1, wanted);
            for (var i = 0; i < 20; i++) a.Update(true, 9f, false, 0.1f);
            Assert.AreEqual(1, wanted, "not again while the ray stays");
            a.Update(false, 9f, false, 0.1f);
            for (var i = 0; i < 5; i++) a.Update(true, 9f, false, 0.1f);
            Assert.AreEqual(2, wanted, "again after the ray left and came back");
        }

        [Test]
        public void TheCardOpensWithin1_2MetresOfTheViewingMark()
        {
            var a = new ArtworkAttention("aic-16568");
            var wanted = 0;
            a.CardWanted += _ => wanted++;
            a.Update(false, 1.3f, false, 0.1f);
            Assert.AreEqual(0, wanted);
            a.Update(false, 1.15f, false, 0.1f);
            Assert.AreEqual(1, wanted);
            a.Update(false, 1.3f, false, 0.1f);    // inside the margin: still "there"
            a.Update(false, 1.1f, false, 0.1f);
            Assert.AreEqual(1, wanted, "standing on the edge does not reopen it");
        }

        [Test]
        public void GazeOfFourSecondsInTotalMakesItSeenOnce()
        {
            var a = new ArtworkAttention("aic-16568");
            var seen = new List<float>();
            a.BecameSeen += (_, s) => seen.Add(s);
            for (var i = 0; i < 20; i++) a.Update(false, 9f, true, 0.1f);    // 2 s
            for (var i = 0; i < 20; i++) a.Update(false, 9f, false, 0.1f);   // look away
            for (var i = 0; i < 25; i++) a.Update(false, 9f, true, 0.1f);    // 2.5 s more
            Assert.AreEqual(1, seen.Count);
            Assert.AreEqual(4.5f, a.GazeSeconds, 1e-3, "dwell keeps counting after seen, for record.dwell");
            Assert.IsTrue(ArtworkAttention.GazeOn(19f));
            Assert.IsFalse(ArtworkAttention.GazeOn(21f));
        }

        // ---- ApproachTrigger, CardChoice, calibration ------------------------------------

        [Test]
        public void APlinthChimesOncePerApproach()
        {
            var t = new ApproachTrigger(1.2f);
            var rings = 0;
            foreach (var d in new[] { 3f, 2f, 1.1f, 0.5f, 1.3f, 1.1f, 2f, 1f })
                if (t.Update(d)) rings++;
            Assert.AreEqual(2, rings, "in, wobble at the edge (no), out, in again (yes)");
        }

        [Test]
        public void ChoosingTheOtherCardTurnsTheFirstBack()
        {
            var c = new CardChoice("Crane", "Turtle");
            var flips = new List<string>();
            c.Flipped += (i, up) => flips.Add(i + (up ? "+" : "-"));
            c.Select(0);
            c.Select(1);
            CollectionAssert.AreEqual(new[] { "0+", "0-", "1+" }, flips);
            Assert.AreEqual("Turtle", c.Choice.Summary);
            Assert.IsTrue(c.Confirm());
            Assert.IsFalse(c.Select(0), "kept");
        }

        [Test]
        public void BInsideTheBarTurnsTheCardBack()
        {
            var c = new CardChoice("Crane", "Turtle");
            c.Select(0);
            c.Choice.Tick(2.9f);
            Assert.IsTrue(c.Redo());
            Assert.AreEqual(-1, c.FaceUp);
            c.Select(1);
            c.Choice.Tick(3.1f);
            Assert.IsFalse(c.Redo());
        }

        [Test]
        public void ASeatedVisitorIsLiftedToAStandingEye()
        {
            Assert.AreEqual(0.4f, HeightCalibration.Offset(1.2f), 1e-4, "seated eye 1.2 m");
            Assert.AreEqual(0f, HeightCalibration.Offset(1.6f), 1e-4);
            Assert.AreEqual(-HeightCalibration.MaxLower, HeightCalibration.Offset(2.1f), 1e-4);
            Assert.AreEqual(0f, HeightCalibration.Offset(0.1f), "a headset on a desk is not an eye");
        }

        [Test]
        public void TheRefusalIsLowerThanEveryChapterSound()
        {
            var x = ChimeSynth.Refuse();
            Assert.Greater(x.Length, 0);
            Assert.Less(ChimeSynth.RefuseHz, new[] { ChimeSynth.BellHz, ChimeSynth.StoneHz, ChimeSynth.WoodHz, ChimeSynth.WaterHz }.Min());
            Assert.AreEqual(0.7f, x.Max(System.Math.Abs), 1e-4);
        }

        // ---- PalaceFlow: her chapter A order --------------------------------------------

        [Test]
        public void APalaceChoiceNeedsAReasonBeforeItSaves()
        {
            var f = new PalaceFlow();
            f.Placed("Crane", 35);
            Assert.IsFalse(f.CanSave, "her storyboard: pick a reason, then save");
            Assert.IsFalse(f.Save());
            Assert.IsTrue(f.ChooseReason("It still looks up"));
            Assert.AreEqual("Crane · 35° · It still looks up", f.Summary);
            Assert.IsTrue(f.Save());
            Assert.AreEqual(PalaceFlow.Phase.Saved, f.Current);
            Assert.AreEqual("miniature", f.ModeName);
        }

        [Test]
        public void TakingThePieceBackForgetsTheReason()
        {
            var f = new PalaceFlow();
            f.Placed("Crane", 35);
            f.ChooseReason("It still looks up");
            f.Unplaced();
            Assert.AreEqual(PalaceFlow.Phase.Choosing, f.Current);
            Assert.AreEqual(string.Empty, f.Reason);
            Assert.IsFalse(f.ChooseReason("anything"), "no reason without a placed piece");
        }

        [Test]
        public void TheCardFallbackSavesModeCardWithNoYaw()
        {
            var f = new PalaceFlow();
            f.Placed("Turtle", 90, PalaceFlow.Mode.Card);
            f.SpeakReason("  it endures  ");
            Assert.AreEqual("it endures", f.Reason);
            Assert.IsTrue(f.ReasonSpoken);
            Assert.AreEqual(0, f.YawDeg);
            Assert.AreEqual("card", f.ModeName);
            Assert.AreEqual("Turtle · it endures", f.Summary);
            Assert.IsFalse(f.SpeakReason("   "), "silence is not a reason");
        }

        [Test]
        public void TheCranesChipsIncludeHerReason()
        {
            CollectionAssert.Contains(PalaceFlow.ReasonsFor("Crane"), "It still looks up");
            Assert.AreEqual(3, PalaceFlow.ReasonsFor("Turtle").Count);
        }
    }
}
