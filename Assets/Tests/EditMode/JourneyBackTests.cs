using NUnit.Framework;
using MusePico.Dialogue;

namespace MusePico.Tests
{
    /// <summary>
    /// Stepping back through the opening act.
    ///
    /// The rule worth pinning is the boundary: the opening is a form being filled in and changing
    /// your mind is free, but from the gallery onwards the visit has consequences — the walk is
    /// recorded, the salon reads it back, the choice scores the axes. Reversing those would either
    /// erase a visit that happened or let one walk be read twice.
    /// </summary>
    public class JourneyBackTests
    {
        static MuseumJourney At(Stage stage)
        {
            var j = new MuseumJourney();
            j.GoTo(stage);
            return j;
        }

        [Test]
        public void TheOpeningActStepsBackOneStageAtATime()
        {
            var j = At(Stage.AiCuration);

            Assert.IsTrue(j.Back());
            Assert.AreEqual(Stage.CompanionSelection, j.Current);

            Assert.IsTrue(j.Back());
            Assert.AreEqual(Stage.LifeQuestion, j.Current);

            Assert.IsTrue(j.Back());
            Assert.AreEqual(Stage.Threshold, j.Current);
        }

        [Test]
        public void TheThresholdHasNowhereToGoBackTo()
        {
            var j = At(Stage.Threshold);

            Assert.IsNull(j.Previous);
            Assert.IsFalse(j.Back());
            Assert.AreEqual(Stage.Threshold, j.Current);
        }

        [Test]
        public void TheJourneyIsOneWayOnceItHasConsequences()
        {
            foreach (var stage in new[]
                     {
                         Stage.WorldExploration, Stage.Summoning, Stage.Roundtable,
                         Stage.Decision, Stage.WorldTransformation, Stage.Manifesto,
                     })
            {
                var j = At(stage);
                Assert.IsNull(j.Previous, stage + " must not be reversible");
                Assert.IsFalse(j.Back(), stage + " must not be reversible");
                Assert.AreEqual(stage, j.Current);
            }
        }

        [Test]
        public void GoingBackKeepsWhatWasAlreadyChosen()
        {
            // The whole point of going back rather than starting again. Reset is how you start again.
            var j = At(Stage.AiCuration);
            j.SetQuestion("How do I live with uncertainty?");

            // A change against her opening three: withdraw one, invite another.
            j.ToggleCompanion("monet");
            j.ToggleCompanion("hilma");

            j.Back();
            j.Back();

            Assert.AreEqual(Stage.LifeQuestion, j.Current);
            Assert.AreEqual("How do I live with uncertainty?", j.Question);
            Assert.AreEqual(3, j.InvitedMasterIds.Count);
            Assert.IsFalse(j.IsInvited("monet"), "the withdrawal survived the step back");
            Assert.IsTrue(j.IsInvited("hilma"), "and so did the invitation");
        }

        [Test]
        public void ChangingYourMindAndGoingForwardAgainCarriesTheChange()
        {
            var j = At(Stage.AiCuration);
            j.SetQuestion("What makes a life meaningful?");

            j.Back();
            j.Back();
            j.SetQuestion("What should I keep, and what should I let go?");
            j.Advance();
            j.Advance();

            Assert.AreEqual(Stage.AiCuration, j.Current);
            // The curation reads the NEW question, through her branches.
            Assert.AreEqual("What Memory Chooses to Keep",
                JourneyScript.For(j).Heading);
        }

        [Test]
        public void ThePanelOffersTheStepBackAndNamesWhereItGoes()
        {
            // In a headset there is no browser history and no breadcrumb: the control has to say
            // where it goes, not just "back".
            Assert.AreEqual("← THE THRESHOLD", JourneyScript.For(At(Stage.LifeQuestion)).Back);
            Assert.AreEqual("← MY QUESTION", JourneyScript.For(At(Stage.CompanionSelection)).Back);
            Assert.AreEqual("← MY COMPANY", JourneyScript.For(At(Stage.AiCuration)).Back);
        }

        [Test]
        public void StagesThatCannotBeReversedOfferNoControl()
        {
            Assert.IsEmpty(JourneyScript.For(At(Stage.Threshold)).Back);
            Assert.IsEmpty(JourneyScript.For(At(Stage.WorldExploration)).Back);
            Assert.IsEmpty(JourneyScript.For(At(Stage.Manifesto)).Back);
        }

        [Test]
        public void TheSpineStillWalksBothWays_BecauseThatIsNotUndoing()
        {
            // Her scene navigator has both arrows. Walking to the previous room erases nothing.
            var j = At(Stage.WorldExploration);

            Assert.IsTrue(j.Spine.Advance());
            Assert.AreEqual(1, j.Spine.Index);
            Assert.IsTrue(j.Spine.Back());
            Assert.AreEqual(0, j.Spine.Index);
            Assert.IsFalse(j.Spine.Back(), "the first chapter has no predecessor");
        }
    }
}
