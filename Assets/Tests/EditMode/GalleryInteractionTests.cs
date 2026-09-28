using System.Collections.Generic;
using NUnit.Framework;
using MusePico.Dialogue;

namespace MusePico.Tests
{
    /// <summary>
    /// The gallery after Saul's 27 Sep comparison with the web build: changing world is the main
    /// control, FORM MY ANSWER sits behind a menu, the masters can be asked by typing as well as
    /// speaking, and clicking a work opens her artwork popup with her three fixed answers.
    /// </summary>
    public class GalleryInteractionTests
    {
        static MuseumJourney Gallery()
        {
            var j = new MuseumJourney();
            j.GoTo(Stage.WorldExploration);
            return j;
        }

        static readonly MasterLens Monet = new MasterLens { id = "monet", name = "MONET", fullName = "Claude Monet" };

        // --- the gallery panel -------------------------------------------------------------

        [Test]
        public void FormMyAnswerLivesBehindTheFinishMenu()
        {
            var panel = JourneyScript.For(Gallery());
            Assert.IsTrue(panel.ActionInMenu);
            StringAssert.Contains("FORM MY ANSWER", panel.Action);
            StringAssert.Contains("FINISH", panel.MenuLabel);
            Assert.IsNotEmpty(panel.MenuNote);
        }

        [Test]
        public void TheArrowsNameTheWorldsTheyGoTo()
        {
            var j = Gallery();
            var first = JourneyScript.For(j);
            Assert.IsEmpty(first.NavPrevLabel, "nothing before the first room");
            Assert.AreEqual(ExhibitionSpine.Chapters[1].Title, first.NavNextLabel);

            j.Spine.GoTo(ExhibitionSpine.Chapters.Count - 1);
            var last = JourneyScript.For(j);
            Assert.AreEqual(ExhibitionSpine.Chapters[ExhibitionSpine.Chapters.Count - 2].Title, last.NavPrevLabel);
            Assert.IsEmpty(last.NavNextLabel, "nothing after the last room");
        }

        [Test]
        public void TheGalleryHintSaysTheMastersCanBeAsked()
        {
            var hint = JourneyScript.For(Gallery()).Hint.ToUpperInvariant();
            StringAssert.Contains("MASTER", hint);
            StringAssert.Contains("ASK", hint);
        }

        [Test]
        public void TheFinalWorldKeepsBeginAgainInTheMenuWithNoNavigator()
        {
            var j = Gallery();
            j.Spine.EnterFinalWorld();
            var panel = JourneyScript.For(j);
            Assert.IsTrue(panel.ActionInMenu);
            StringAssert.Contains("BEGIN AGAIN", panel.Action);
            Assert.AreEqual(0, panel.NavTotal);
        }

        // --- asking by keyboard ------------------------------------------------------------

        [Test]
        public void TypingShowsACaretAndNamesTheKeys()
        {
            var panel = JourneyScript.AskDialogue(Monet, "Water Lilies", "Why light?", string.Empty, typing: true);
            StringAssert.Contains("Why light?|", panel.Heading);
            StringAssert.DoesNotContain("”", panel.Heading, "no closing quote around the caret");
            StringAssert.Contains("ENTER TO ASK", panel.Hint);
            StringAssert.DoesNotContain("GRIP", panel.Hint);
        }

        [Test]
        public void TheHeadsetFormStillSpeaksOfTheGrip()
        {
            var panel = JourneyScript.AskDialogue(Monet, "Water Lilies", string.Empty, string.Empty);
            StringAssert.Contains("GRIP", panel.Hint);
        }

        [Test]
        public void TheReadyQuestionIsAPlateUnderAnEmptyBox_NeverThePrefill()
        {
            // Saul, 27 Sep: clicking a master must stay "ask my own question"; the Buddha question
            // is an extra, not what the box holds.
            var panel = JourneyScript.AskDialogue(Monet, null, string.Empty, string.Empty, typing: true,
                suggestion: "What do you see in the Great Buddha?");
            StringAssert.Contains("What do you want to ask?", panel.Heading);
            Assert.IsFalse(panel.ActionEnabled, "nothing to send until the visitor asks something");
            Assert.AreEqual(1, panel.Choices.Count);
            Assert.AreEqual(JourneyScript.SuggestedQuestionId, panel.Choices[0].Id);
            StringAssert.Contains("the Great Buddha", panel.Choices[0].Label);

            var headset = JourneyScript.AskDialogue(Monet, null, string.Empty, string.Empty, typing: false,
                suggestion: "What do you see in the Great Buddha?");
            StringAssert.Contains("SPEAK YOUR QUESTION", headset.Hint);
        }

        [Test]
        public void TheFirstKeyReplacesTheSuggestion()
        {
            var suggestion = true;
            var text = AskTyping.Apply("What do you see in Water Lilies?", ref suggestion, "W", 0);
            Assert.AreEqual("W", text);
            Assert.IsFalse(suggestion);

            text = AskTyping.Apply(text, ref suggestion, "hy", 0);
            Assert.AreEqual("Why", text);
        }

        [Test]
        public void BackspaceOnASuggestionClearsItAndThenEdits()
        {
            var suggestion = true;
            Assert.AreEqual(string.Empty, AskTyping.Apply("What do you see?", ref suggestion, "\b", 1));
            Assert.IsFalse(suggestion);

            var typed = AskTyping.Apply(string.Empty, ref suggestion, "abc", 0);
            Assert.AreEqual("a", AskTyping.Apply(typed, ref suggestion, string.Empty, 2));
        }

        [Test]
        public void ControlCharactersAreNeverTyped()
        {
            var suggestion = false;
            Assert.AreEqual("ab", AskTyping.Apply("a", ref suggestion, "\r\n\tb\u001b", 0));
        }

        [Test]
        public void AQuestionHasAMaximumLength()
        {
            var suggestion = false;
            var text = AskTyping.Apply(new string('x', AskTyping.MaxLength - 1), ref suggestion, "yz", 0);
            Assert.AreEqual(AskTyping.MaxLength, text.Length);
        }

        // --- her artwork popup ---------------------------------------------------------------

        [Test]
        public void HerThreeAnswersWordForWordEachMovingOneAxis()
        {
            Assert.AreEqual(3, ArtworkDialogue.Choices.Count);
            Assert.AreEqual("It changes how my eyes work. I will see differently when I leave.",
                ArtworkDialogue.Choices[0].Label);
            Assert.AreEqual(new PhilosophyAxes(1, 0, 0), ArtworkDialogue.Choices[0].Delta);
            Assert.AreEqual(new PhilosophyAxes(0, 1, 0), ArtworkDialogue.Choices[1].Delta);
            Assert.AreEqual(new PhilosophyAxes(0, 0, 1), ArtworkDialogue.Choices[2].Delta);
        }

        [Test]
        public void AnAnswerIsSavedIntoTheScoreTheEndingIsBuiltFrom()
        {
            var session = new VisitSession();
            session.ApplyChoice(ArtworkDialogue.ChoiceById("emotion").Delta);
            session.ApplyChoice(ArtworkDialogue.ChoiceById("emotion").Delta);
            session.ApplyChoice(ArtworkDialogue.ChoiceById("invention").Delta);
            Assert.AreEqual(0, session.Philosophy.Perception);
            Assert.AreEqual(2, session.Philosophy.Emotion);
            Assert.AreEqual(1, session.Philosophy.Invention);
        }

        [Test]
        public void EveryMasterOnTheRosterHasAVoiceWithAllThreeReactions()
        {
            foreach (var id in new[] { "monet", "picasso", "hilma", "van_gogh", "frida", "socrates", "morisot" })
            {
                Assert.IsTrue(ArtworkDialogue.Voices.ContainsKey(id), id);
                foreach (var c in ArtworkDialogue.Choices)
                    Assert.IsNotEmpty(ArtworkDialogue.VoiceFor(id).Reactions[c.Id], id + "/" + c.Id);
            }
            Assert.AreSame(ArtworkDialogue.Fallback, ArtworkDialogue.VoiceFor("nobody"));
        }

        [Test]
        public void OpeningLinesNameTheWorkAndFallBackLikeHers()
        {
            var line = ArtworkDialogue.Format(ArtworkDialogue.VoiceFor("socrates").Opening, "Water Lilies", "Monet");
            StringAssert.Contains("“Water Lilies”", line);
            StringAssert.Contains("what Monet made", line);
            StringAssert.Contains("its maker", ArtworkDialogue.Format("{artist}", "x", null));
            StringAssert.Contains("this work", ArtworkDialogue.Format("{title}", null, "y"));
        }

        [Test]
        public void TheInvitedMastersTakeTurnsToOpen()
        {
            var invited = new List<string> { "monet", "van_gogh", "socrates" };
            Assert.AreEqual("monet", ArtworkDialogue.PickOpening(invited, 0));
            Assert.AreEqual("van_gogh", ArtworkDialogue.PickOpening(invited, 1));
            Assert.AreEqual("monet", ArtworkDialogue.PickOpening(invited, 3));
            Assert.IsNull(ArtworkDialogue.PickOpening(new List<string>(), 0));
        }

        [Test]
        public void TheChampionOfTheAnswerReactsNeverTheOpenerWhenAnotherCan()
        {
            var invited = new List<string> { "monet", "van_gogh", "socrates" };
            Assert.AreEqual("van_gogh", ArtworkDialogue.PickReaction("emotion", "monet", invited));
            Assert.AreEqual("socrates", ArtworkDialogue.PickReaction("invention", "monet", invited));
            // Monet champions perception but opened, and no other champion is invited.
            Assert.AreEqual("van_gogh", ArtworkDialogue.PickReaction("perception", "monet", invited));
        }

        [Test]
        public void ThePopupListensThenOffersHerAnswersThenOneReplyAndTheWalk()
        {
            // Saul, 27 Sep: the masters speak about it, you choose, one replies, you walk on.
            var looking = JourneyScript.ArtDialogue("The Great Buddha", JourneyScript.ArtPhase.Looking, "MONET IS SPEAKING (1 / 3)");
            Assert.IsEmpty(looking.Choices, "no answers while the readings are still being spoken");
            Assert.IsEmpty(looking.Action, "no NOT NOW button");
            StringAssert.Contains("MONET IS SPEAKING", looking.Lede);

            var choosing = JourneyScript.ArtDialogue("The Great Buddha", JourneyScript.ArtPhase.Choosing, "ALL THREE HAVE SPOKEN");
            Assert.AreEqual(3, choosing.Choices.Count);
            StringAssert.StartsWith("01", choosing.Choices[0].Label);
            Assert.AreEqual("perception", choosing.Choices[0].Id);
            Assert.IsEmpty(choosing.Action, "no NOT NOW button");
            Assert.AreEqual(ArtworkDialogue.Disclaimer, choosing.Notice);

            var done = JourneyScript.ArtDialogue("The Great Buddha", JourneyScript.ArtPhase.Answered, null,
                youSaid: "It makes me feel something I don't have words for yet.", reactor: Monet, reply: "What you feel is the weather.");
            Assert.IsEmpty(done.Choices);
            Assert.AreEqual("CLAUDE MONET REPLIES", done.Eyebrow);
            StringAssert.Contains("CONTINUE THE WALK", done.Action);
            StringAssert.Contains("It makes me feel something", done.Lede, "the reply is shown with the answer it replies to");
            StringAssert.Contains("What you feel is the weather.", done.Lede);
        }

        [Test]
        public void TheAskFormAlwaysOpensWithAQuestionReadyToAsk()
        {
            Assert.AreEqual("What do you see in Water Lilies?",
                ArtworkDialogue.DefaultQuestion("Water Lilies", "the Great Buddha"), "the work stopped at wins");
            Assert.AreEqual("What do you see in the Great Buddha?",
                ArtworkDialogue.DefaultQuestion(null, "the Great Buddha"), "else the room's focal object");
            Assert.IsNotEmpty(ArtworkDialogue.DefaultQuestion(null, null), "never an empty box");
        }

        [Test]
        public void AnObjectTakesItsTitleFromItsAssetName()
        {
            Assert.AreEqual("Golden Buddha Statues", ArtworkDialogue.TitleFromName("golden-buddha-statues"));
            Assert.AreEqual("Buddha Statue", ArtworkDialogue.TitleFromName("buddha_statue"));
            Assert.AreEqual(string.Empty, ArtworkDialogue.TitleFromName(null));
        }
    }
}
