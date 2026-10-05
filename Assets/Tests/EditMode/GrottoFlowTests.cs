using MuseXR.Slots;
using NUnit.Framework;

namespace MusePico.Tests
{
    /// <summary>Her chapter B as logic: lamp in a socket, the two ways of seeing, grotto{lampSlot, exhibitId}.</summary>
    public class GrottoFlowTests
    {
        [Test]
        public void TheSocketNamesBecomeHerLampSlotWords()
        {
            var f = new GrottoFlow();
            Assert.IsTrue(f.Placed("Detail"));
            Assert.AreEqual("detail", f.LampSlot);
            Assert.IsTrue(f.LightsTheRelief);
            Assert.IsFalse(f.RimsTheBuddha);
            Assert.IsTrue(f.Placed("Whole"));
            Assert.AreEqual("whole", f.LampSlot);
            Assert.IsTrue(f.RimsTheBuddha);
            Assert.IsFalse(f.LightsTheRelief);
        }

        [Test]
        public void AnUnknownSocketIsNotAPlacement()
        {
            var f = new GrottoFlow();
            Assert.IsFalse(f.Placed("Court"));
            Assert.AreEqual(GrottoFlow.Phase.Choosing, f.Current);
        }

        [Test]
        public void LiftingTheLampOutForgetsTheSlotAndTheRim()
        {
            var f = new GrottoFlow();
            f.Placed("Whole");
            f.Unplaced();
            Assert.AreEqual(GrottoFlow.Phase.Choosing, f.Current);
            Assert.IsEmpty(f.LampSlot);
            Assert.IsFalse(f.RimsTheBuddha);
            Assert.IsFalse(f.CanSave);
        }

        [Test]
        public void SavingNeedsAPlacedLampAndIsFinal()
        {
            var f = new GrottoFlow();
            Assert.IsFalse(f.Save());
            f.Placed("Detail");
            Assert.IsTrue(f.Save());
            Assert.AreEqual(GrottoFlow.Phase.Saved, f.Current);
            Assert.IsFalse(f.Placed("Whole"), "after saving, the choice stays");
            f.Unplaced();
            Assert.AreEqual("detail", f.LampSlot);
            Assert.AreEqual("aic-142512", f.ExhibitId, "her record example");
        }

        [Test]
        public void SocratesAsksHerQuestionAtTheRelief()
        {
            // Her fallback line for the detail, the one spoken when the live reaction cannot be had.
            Assert.AreEqual("You chose the detail. The Buddha is right behind you. Are you afraid the whole would make you look too small?",
                            GrottoFlow.Line(Masters.Socrates, GrottoFlow.Detail));
        }

        [Test]
        public void TheCompanionOfTheChosenWayOfSeeingSpeaksFirstAndSocratesLast()
        {
            CollectionAssert.AreEqual(new[] { Masters.VanGogh, Masters.Monet, Masters.Socrates }, GrottoFlow.Order(GrottoFlow.Detail));
            CollectionAssert.AreEqual(new[] { Masters.Monet, Masters.VanGogh, Masters.Socrates }, GrottoFlow.Order(GrottoFlow.Whole));
        }
    }
}
