using System.Collections.Generic;
using NUnit.Framework;
using MusePico.Dialogue;

namespace MusePico.Tests
{
    /// <summary>
    /// What each stage says, against muse-infinity's view functions.
    ///
    /// These exist so the two builds cannot drift apart in wording. Where a line describes a mouse
    /// it is restated for hands, and that is the only class of change allowed.
    /// </summary>
    public class JourneyScriptTests
    {
        static List<MasterLens> Roster() => new List<MasterLens>
        {
            new MasterLens { id = "monet", name = "MONET", fullName = "Claude Monet" },
            new MasterLens { id = "van_gogh", name = "VAN GOGH", fullName = "Vincent van Gogh" },
            new MasterLens { id = "socrates", name = "SOCRATES", fullName = "Socrates" },
        };

        static MuseumJourney At(Stage stage)
        {
            var j = new MuseumJourney();
            j.GoTo(stage);
            return j;
        }

        [Test]
        public void TheThresholdIsHerOpeningWordForWord()
        {
            var panel = JourneyScript.For(At(Stage.Threshold));

            Assert.AreEqual("A LIVING ARCHIVE BEYOND TIME", panel.Eyebrow);
            Assert.AreEqual("The Impossible Museum", panel.Heading);
            StringAssert.Contains("artists disagree", panel.Lede);
            StringAssert.Contains("MEMORY SITE 00", panel.Marker);
            Assert.AreEqual("ENTER", panel.Action);
        }

        [Test]
        public void TheControlHintSpeaksOfHandsNotAMouse()
        {
            foreach (Stage stage in System.Enum.GetValues(typeof(Stage)))
            {
                var hint = JourneyScript.For(At(stage), Roster()).Hint ?? string.Empty;
                StringAssert.DoesNotContain("CLICK", hint.ToUpperInvariant());
                StringAssert.DoesNotContain("DRAG", hint.ToUpperInvariant());
                StringAssert.DoesNotContain("W A S D", hint.ToUpperInvariant());
            }
        }

        [Test]
        public void StageOneOffersHerThreeQuestions()
        {
            var panel = JourneyScript.For(At(Stage.LifeQuestion));

            Assert.AreEqual(3, panel.Choices.Count);
            Assert.AreEqual("What makes a life meaningful?", panel.Choices[0].Label);
            Assert.AreEqual("How do I live with uncertainty?", panel.Choices[1].Label);
            Assert.AreEqual("What should I keep, and what should I let go?", panel.Choices[2].Label);
        }

        [Test]
        public void AnAnsweredQuestionIsReadBackOnThePanel()
        {
            var j = At(Stage.LifeQuestion);
            j.SetQuestion("What do I owe the people who made me?");

            var panel = JourneyScript.For(j);

            StringAssert.Contains("What do I owe the people who made me?", panel.Hint);
        }

        [Test]
        public void StageOneCanAlwaysMoveOn_BecauseHerSubmitFallsBackToTheFirstSuggestion()
        {
            Assert.IsTrue(JourneyScript.For(At(Stage.LifeQuestion)).ActionEnabled);
        }

        [Test]
        public void StageTwoRefusesToCurateUntilSomebodyIsInvited()
        {
            var j = At(Stage.CompanionSelection);

            // It ARRIVES live, because her opening company is pre-invited. A forward button that
            // is dead on arrival reads as a broken screen, which is what it was.
            var opening = JourneyScript.For(j, Roster());
            Assert.IsTrue(opening.ActionEnabled, "her opening company is already three");
            StringAssert.Contains("3 / 3", opening.Hint);

            // Withdraw all three and it goes dead, which is the rule being pinned.
            foreach (var id in MuseumJourney.DefaultCompany) j.ToggleCompanion(id);
            var empty = JourneyScript.For(j, Roster());
            Assert.IsFalse(empty.ActionEnabled, "her LET AI CURATE is disabled with nobody chosen");
            StringAssert.Contains("0 / 3", empty.Hint);

            j.ToggleCompanion("monet");
            var one = JourneyScript.For(j, Roster());
            Assert.IsTrue(one.ActionEnabled);
            StringAssert.Contains("1 / 3", one.Hint);
            Assert.IsTrue(one.Choices[0].Selected, "the invited master reads as chosen");
        }

        [Test]
        public void StageTwoCarriesTheAiInterpretationDisclaimer()
        {
            var panel = JourneyScript.For(At(Stage.CompanionSelection), Roster());
            StringAssert.Contains("AI interpretations", panel.Notice);
        }

        [Test]
        public void StageThreeReadsTheQuestionThroughHerCurationBranches()
        {
            var j = At(Stage.AiCuration);
            j.SetQuestion("What should I keep, and what should I let go?");

            var panel = JourneyScript.For(j, Roster());

            Assert.AreEqual("What Memory Chooses to Keep", panel.Heading);
            Assert.AreEqual(3, panel.Choices.Count);
            StringAssert.Contains("What should I keep", panel.Lede);
        }

        [Test]
        public void StageFourNamesTheChapterTheVisitorIsStandingIn()
        {
            var j = At(Stage.WorldExploration);

            var first = JourneyScript.For(j);
            Assert.AreEqual("The Threshold Conservatory", first.Heading);
            StringAssert.Contains("01 / ARRIVAL", first.Eyebrow);
            StringAssert.Contains("cross-temporal salon", first.Eyebrow);
            StringAssert.Contains("What must become visible", first.Lede);
            Assert.AreEqual("01 / 08", first.Marker);

            j.Spine.Advance();
            Assert.AreEqual("02 / 08", JourneyScript.For(j).Marker);
        }

        [Test]
        public void TheFinalWorldReplacesTheCounterAndTheForwardAction()
        {
            var j = At(Stage.WorldExploration);
            j.Spine.EnterFinalWorld();

            var panel = JourneyScript.For(j);

            Assert.AreEqual("YOUR IMPOSSIBLE WORLD", panel.Marker);
            Assert.AreEqual("Your Dream World", panel.Heading);
            StringAssert.Contains("BEGIN AGAIN", panel.Action);
        }

        [Test]
        public void StageFiveSaysPlainlyWhenTheRecordIsEmpty()
        {
            var empty = JourneyScript.For(At(Stage.Summoning));
            StringAssert.Contains("empty record", empty.Lede);
            Assert.IsEmpty(empty.Choices);

            var j = At(Stage.Summoning);
            j.Session.RecordArtwork("Water Lilies", "Claude Monet");
            j.Session.RecordQuestion("What should I keep?");

            var walked = JourneyScript.For(j);
            Assert.AreEqual("Your walk is entering the record.", walked.Lede);
            Assert.AreEqual(2, walked.Choices.Count);
            StringAssert.Contains("Water Lilies", walked.Choices[0].Label);
        }

        [Test]
        public void StageSevenIsHerThreeChoices_LabelsAndDeltasBoth()
        {
            var panel = JourneyScript.For(At(Stage.Decision));

            Assert.AreEqual(3, panel.Choices.Count);
            Assert.AreEqual("Art should teach us to see the world again.", panel.Choices[0].Label);

            Assert.AreEqual(PhilosophyAxes.PerceptionChoice, JourneyScript.DecisionChoices[0].Delta);
            Assert.AreEqual(PhilosophyAxes.EmotionChoice, JourneyScript.DecisionChoices[1].Delta);
            Assert.AreEqual(PhilosophyAxes.InventionChoice, JourneyScript.DecisionChoices[2].Delta);
        }

        [Test]
        public void TheTransformationBeatsRunInHerOrderAndFitTheRun()
        {
            var beats = JourneyScript.TransformationBeats;
            Assert.AreEqual(4, beats.Count, "the opening line plus her three writes");

            var last = -1f;
            foreach (var b in beats)
            {
                Assert.Greater(b.Key, last, "beats must advance");
                Assert.Less(b.Key, 1f, "and all land before the manifesto");
                last = b.Key;
            }
            StringAssert.Contains("Sound falls away", beats[0].Value);
            StringAssert.Contains("Particles reassemble", beats[3].Value);
        }

        [Test]
        public void AnEndingWithoutASynthesisIsLabelledAsTheFailureItIs()
        {
            var fallback = JourneyScript.Ending("", "", live: true);
            Assert.IsFalse(fallback.FromRoundtable);
            Assert.AreEqual(JourneyScript.FallbackTitle, fallback.Title);

            var j = At(Stage.Manifesto);
            var panel = JourneyScript.For(j, Roster(), fallback);
            StringAssert.Contains("GENERIC ENDING", panel.Notice);
        }

        [Test]
        public void ASynthesisThatNoLiveModelProducedSaysSo()
        {
            var offline = JourneyScript.Ending("The World Between Two Silences", "You stopped.", live: false);
            Assert.IsTrue(offline.FromRoundtable);

            var panel = JourneyScript.For(At(Stage.Manifesto), Roster(), offline);
            StringAssert.Contains("LOCAL FALLBACK", panel.Notice);
        }

        [Test]
        public void ARealSynthesisCarriesNoNoticeAtAll()
        {
            var live = JourneyScript.Ending("The World Between Two Silences", "You stopped.", live: true);

            var panel = JourneyScript.For(At(Stage.Manifesto), Roster(), live);

            Assert.IsEmpty(panel.Notice);
            Assert.AreEqual("The World Between Two Silences", panel.Heading);
        }

        [Test]
        public void TheManifestoShowsTheScoreTheChoicesBuilt()
        {
            var j = At(Stage.Manifesto);
            j.Session.ApplyChoice(PhilosophyAxes.InventionChoice);

            var panel = JourneyScript.For(j, Roster(),
                JourneyScript.Ending("A Title", "Some copy.", true));

            StringAssert.Contains("INVENTION 03", panel.Hint);
            StringAssert.Contains("PERCEPTION 01", panel.Hint);
        }

        [Test]
        public void EveryStageSaysSomething_SoNoneCanRenderBlank()
        {
            foreach (Stage stage in System.Enum.GetValues(typeof(Stage)))
            {
                var panel = JourneyScript.For(At(stage), Roster());
                Assert.IsNotEmpty(panel.Heading, stage + " has no heading");
                Assert.IsNotEmpty(panel.Eyebrow, stage + " has no eyebrow");
            }
        }

        [Test]
        public void NoRoundtableYetStillReadsAsHerLabelledFallback()
        {
            // A default ClosingEnding carries null strings. Reaching the manifesto without a
            // synthesis must produce the generic ending, named as one — never a blank panel.
            var panel = JourneyScript.For(At(Stage.Manifesto), Roster());

            Assert.AreEqual(JourneyScript.FallbackTitle, panel.Heading);
            StringAssert.Contains("GENERIC ENDING", panel.Notice);
        }

        [Test]
        public void ANullJourneyRendersAnEmptyPanelRatherThanThrowing()
        {
            Assert.DoesNotThrow(() => JourneyScript.For(null));
            Assert.IsEmpty(JourneyScript.For(null).Heading);
            Assert.IsEmpty(JourneyScript.WalkTrail(null));
        }
    }
}
