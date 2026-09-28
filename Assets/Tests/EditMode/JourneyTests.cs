using System.Linq;
using NUnit.Framework;
using MusePico.Dialogue;

namespace MusePico.Tests
{
    /// <summary>
    /// The digest that the closing roundtable reads back.
    ///
    /// These pin muse-infinity's contract, not our preferences: the field semantics come from
    /// <c>app.js:99-140</c> and the server's re-clamp at <c>server.mjs:563-607</c>. If one of these
    /// fails, the roundtable payload has stopped matching what her server describes.
    /// </summary>
    public class VisitSessionTests
    {
        [Test]
        public void CapsAreAppliedAtConstruction_NotOnTheWayOut()
        {
            var s = new VisitSession();
            for (var i = 0; i < 12; i++) s.RecordArtwork("Work " + i, "Artist " + i);
            Assert.AreEqual(VisitSession.MaxArtworks, s.VisitedArtworks.Count,
                "the list itself must never exceed the cap");
        }

        [Test]
        public void TheWindowKeepsTheLastN_NotTheFirst()
        {
            var s = new VisitSession();
            for (var i = 0; i < 8; i++) s.RecordArtwork("Work " + i, "A");
            Assert.AreEqual("Work 7", s.VisitedArtworks.Last().Title);
            Assert.AreEqual("Work 3", s.VisitedArtworks.First().Title);
        }

        [Test]
        public void ARepeatVisitMovesToTheEnd_RatherThanBeingIgnored()
        {
            var s = new VisitSession();
            s.RecordArtwork("Water Lilies", "Monet");
            s.RecordArtwork("The Bedroom", "Van Gogh");
            s.RecordArtwork("water lilies", "MONET");   // same work, different case

            Assert.AreEqual(2, s.VisitedArtworks.Count, "case-insensitive dedupe");
            Assert.AreEqual("water lilies", s.VisitedArtworks.Last().Title,
                "the repeat becomes the most recent entry");
        }

        [Test]
        public void AnArtworkWithNoTitleIsDropped()
        {
            var s = new VisitSession();
            s.RecordArtwork("   ", "Monet");
            s.RecordArtwork(null, "Monet");
            Assert.AreEqual(0, s.VisitedArtworks.Count);
        }

        [Test]
        public void WhitespaceIsCollapsedAndTextIsSliced()
        {
            Assert.AreEqual("a b c", VisitSession.Clamp("  a \n\t b   c  ", 50));
            Assert.AreEqual("abcde", VisitSession.Clamp("abcdefghij", 5));
            Assert.AreEqual(string.Empty, VisitSession.Clamp(null, 50),
                "a null becomes empty, never the string 'null'");
        }

        [Test]
        public void QuestionsRollAtThree()
        {
            var s = new VisitSession();
            s.RecordQuestion("one"); s.RecordQuestion("two");
            s.RecordQuestion("three"); s.RecordQuestion("four");
            Assert.AreEqual(3, s.AskedQuestions.Count);
            Assert.AreEqual("four", s.AskedQuestions.Last());
            CollectionAssert.DoesNotContain(s.AskedQuestions, "one");
        }

        [Test]
        public void APerspectiveReplacesThatSpeakersOlderLine()
        {
            var s = new VisitSession();
            s.RecordPerspective("monet", "Claude Monet", "first reading");
            s.RecordPerspective("monet", "Claude Monet", "second reading");
            Assert.AreEqual(1, s.PerspectiveLog.Count, "one line per speaker");
            Assert.AreEqual("second reading", s.PerspectiveLog[0].Line, "newest wins");
        }

        [Test]
        public void AMissingSpeakerNameFallsBackToTheId()
        {
            var s = new VisitSession();
            s.RecordPerspective("hilma", "", "a reading");
            Assert.AreEqual("hilma", s.PerspectiveLog[0].Speaker);
        }

        [Test]
        public void ResetClearsTheAxesAsWellAsTheDigest()
        {
            var s = new VisitSession();
            s.RecordArtwork("Water Lilies", "Monet");
            s.RecordQuestion("what is meaningful?");
            s.ApplyChoice(PhilosophyAxes.EmotionChoice);

            s.Reset();

            Assert.AreEqual(0, s.VisitedArtworks.Count);
            Assert.AreEqual(0, s.AskedQuestions.Count);
            Assert.AreEqual(0, s.PerspectiveLog.Count);
            Assert.AreEqual(0, s.Philosophy.Emotion, "the next visitor must not inherit a stranger's answers");
            Assert.IsFalse(s.HasAnything);
        }

        [Test]
        public void TheChoiceDeltasAreHers_NotOneAxisEach()
        {
            Assert.AreEqual(3, PhilosophyAxes.PerceptionChoice.Perception);
            Assert.AreEqual(1, PhilosophyAxes.PerceptionChoice.Emotion,
                "her perception answer also nudges emotion");
            Assert.AreEqual(1, PhilosophyAxes.EmotionChoice.Invention);
            Assert.AreEqual(1, PhilosophyAxes.InventionChoice.Perception);
        }

        [Test]
        public void PhilosophyKeyIsTheTopTwoAxesSortedAndJoined()
        {
            var s = new VisitSession();
            s.ApplyChoice(PhilosophyAxes.EmotionChoice);    // p0 e3 i1
            s.ApplyChoice(PhilosophyAxes.InventionChoice);  // p1 e3 i4
            Assert.AreEqual("emotion+invention", s.Philosophy.Key());
        }
    }

    public class MuseumJourneyTests
    {
        [Test]
        public void TheArcStartsAtTheThresholdAndRunsInOrder()
        {
            var j = new MuseumJourney();
            Assert.AreEqual(Stage.Threshold, j.Current);

            var seen = new System.Collections.Generic.List<Stage> { j.Current };
            while (j.Advance()) seen.Add(j.Current);

            CollectionAssert.AreEqual(
                System.Enum.GetValues(typeof(Stage)).Cast<Stage>().ToList(), seen,
                "every stage, in her order, with none skipped");
        }

        [Test]
        public void TheManifestoHasNoSuccessor()
        {
            var j = new MuseumJourney();
            j.GoTo(Stage.Manifesto);
            Assert.IsNull(j.Next);
            Assert.IsFalse(j.Advance(), "stage 09 leaves via hand-off or reset, not Advance");
        }

        [Test]
        public void EveryChangeAnnouncesLeavingThenArriving()
        {
            var j = new MuseumJourney();
            Stage? left = null; Stage? from = null, to = null;
            j.LeavingStage += s => left = s;
            j.StageChanged += (a, b) => { from = a; to = b; };

            j.Advance();

            Assert.AreEqual(Stage.Threshold, left, "narration is stopped on the way out");
            Assert.AreEqual(Stage.Threshold, from);
            Assert.AreEqual(Stage.LifeQuestion, to);
        }

        [Test]
        public void GoingToTheSameStageIsNotAChange()
        {
            var j = new MuseumJourney();
            var fired = 0;
            j.StageChanged += (a, b) => fired++;
            j.GoTo(Stage.Threshold);
            Assert.AreEqual(0, fired);
        }

        [Test]
        public void AtMostThreeCompanionsAndAFourthIsIgnored()
        {
            // Her opening company is ALREADY three - `selectedCompanions: new Set(["monet",
            // "van_gogh","socrates"])` - so the ceiling is reached before the visitor touches
            // anything, and the fourth click is the first one that can be refused.
            var j = new MuseumJourney();
            Assert.AreEqual(3, j.InvitedMasterIds.Count, "her opening company is pre-invited");

            Assert.IsFalse(j.ToggleCompanion("picasso"), "the fourth is refused, not swapped in");
            Assert.AreEqual(3, j.InvitedMasterIds.Count);
            Assert.IsFalse(j.IsInvited("picasso"));

            // Withdrawing frees exactly one slot, and no more.
            j.ToggleCompanion("monet");
            Assert.IsTrue(j.ToggleCompanion("picasso"));
            Assert.AreEqual(3, j.InvitedMasterIds.Count);
        }

        [Test]
        public void InvitingTwiceWithdrawsTheInvitation()
        {
            var j = new MuseumJourney();
            j.ToggleCompanion("monet");                  // free a slot in her opening three
            Assert.IsTrue(j.ToggleCompanion("hilma"));
            Assert.IsTrue(j.IsInvited("hilma"));

            j.ToggleCompanion("HILMA");
            Assert.IsFalse(j.IsInvited("hilma"), "case-insensitive, and it frees the slot");
            Assert.AreEqual(2, j.InvitedMasterIds.Count);
        }

        [Test]
        public void TheQuestionIsClampedLikeEveryOtherVisitorString()
        {
            var j = new MuseumJourney();
            j.SetQuestion("  what   makes\na life\tmeaningful?  ");
            Assert.AreEqual("what makes a life meaningful?", j.Question);
        }

        [Test]
        public void HandOffOnlyAppliesAtTheManifesto()
        {
            var j = new MuseumJourney();
            j.HandOff();
            Assert.IsFalse(j.HandedOff, "ENTER YOUR WORLD does not exist before stage 09");

            j.GoTo(Stage.Manifesto);
            j.HandOff();
            Assert.IsTrue(j.HandedOff);
        }

        [Test]
        public void ResetReturnsToTheThresholdAndClearsEverything()
        {
            var j = new MuseumJourney();
            j.SetQuestion("what should I keep?");
            j.ToggleCompanion("monet");
            j.Session.RecordArtwork("Water Lilies", "Monet");
            j.Spine.Advance();
            j.GoTo(Stage.Manifesto);
            j.HandOff();

            j.Reset();

            Assert.AreEqual(Stage.Threshold, j.Current);
            Assert.AreEqual(string.Empty, j.Question);

            // Her reset RESTORES the opening company rather than emptying it - app.js:1061 passes
            // `selectedCompanions: new Set(["monet","van_gogh","socrates"])` into the reset, the
            // same set the app boots with. An empty stage 02 is not a state her build can be in.
            CollectionAssert.AreEquivalent(
                MuseumJourney.DefaultCompany, j.InvitedMasterIds,
                "reset returns to her opening company, it does not clear it");
            Assert.AreEqual(0, j.Session.VisitedArtworks.Count);
            Assert.AreEqual(0, j.Spine.Index);
            Assert.IsFalse(j.HandedOff);
        }
    }

    public class ExhibitionSpineTests
    {
        [Test]
        public void TheSpineIsHerEightChaptersInOrder()
        {
            Assert.AreEqual(8, ExhibitionSpine.Chapters.Count);
            Assert.AreEqual("grand-conservatory-with-lush-gardens", ExhibitionSpine.Chapters[0].WorldKey);
            Assert.AreEqual("01 / ARRIVAL", ExhibitionSpine.Chapters[0].Chapter);
            Assert.AreEqual("yellow-polka-dot-infinity-room", ExhibitionSpine.Chapters[7].WorldKey);
            Assert.AreEqual("08 / INFINITY", ExhibitionSpine.Chapters[7].Chapter);
        }

        [Test]
        public void EveryChapterHasItsOwnWorld()
        {
            var keys = ExhibitionSpine.Chapters.Select(c => c.WorldKey).ToList();
            CollectionAssert.AllItemsAreUnique(keys, "the gallery is eight different rooms");
        }

        [Test]
        public void TheFinalWorldIsTheShimmeringSpheres()
        {
            Assert.AreEqual("fantasy-realm-of-shimmering-spheres", ExhibitionSpine.Final.WorldKey);
            Assert.IsTrue(ExhibitionSpine.Final.IsFinal);
            CollectionAssert.DoesNotContain(
                ExhibitionSpine.Chapters.Select(c => c.WorldKey).ToList(),
                ExhibitionSpine.Final.WorldKey,
                "the ending is not one of the eight stops");
        }

        [Test]
        public void TheFinalWorldReplacesTheWalkOnceEntered()
        {
            var s = new ExhibitionSpine();
            s.Advance();
            Assert.AreEqual("The Court of Light", s.Current.Title);

            s.EnterFinalWorld();
            Assert.AreEqual("Your Dream World", s.Current.Title);
            Assert.IsFalse(s.Advance(), "there is nowhere to walk to from the ending");
        }

        [Test]
        public void TheWalkStopsAtTheLastChapter()
        {
            var s = new ExhibitionSpine();
            for (var i = 0; i < 7; i++) Assert.IsTrue(s.Advance(), "chapter " + i);
            Assert.IsTrue(s.IsLast);
            Assert.IsFalse(s.Advance(), "the arc leaves via stage 05, not by running out of rooms");
        }

        [Test]
        public void ThePlaceholderKeepsTheRealWorldKeyVisible()
        {
            // The substitution must never overwrite her world key, or the fix later becomes
            // archaeology instead of deleting one line.
            var ch06 = ExhibitionSpine.Chapters[5];
            Assert.AreEqual("sunlit-palace-gardens", ch06.WorldKey, "her world, preserved");
            Assert.AreEqual("enchanted-palace-garden", ch06.EffectiveWorldKey, "what actually loads");
            Assert.IsTrue(ch06.IsPlaceholder);

            var ch08 = ExhibitionSpine.Chapters[7];
            Assert.AreEqual("yellow-polka-dot-infinity-room", ch08.WorldKey);
            Assert.AreEqual("fantasy-realm-of-shimmering-spheres", ch08.EffectiveWorldKey);
        }

        [Test]
        public void EveryPlaceholderPointsAtAWorldWeCanActuallyShip()
        {
            foreach (var c in ExhibitionSpine.Placeholders())
                CollectionAssert.DoesNotContain(ExhibitionSpine.MissingAtSmallTier.ToArray(),
                    c.EffectiveWorldKey,
                    c.Chapter + " stands in a world that also cannot ship");
        }

        [Test]
        public void ChaptersOneAndTwo_AreTheBuddhaHallThenPeachGarden_AndKeepHersOnRecord()
        {
            // Skylar's brief (27 Sep): the opening stands in the Forbidden-City courtyard (the
            // scene's home world, not a chapter); entering the exhibition is chapter 01, the
            // Buddha hall; chapter 02 the Peach Garden. Her worlds stay in WorldKey, so clearing
            // ChosenWorldKey is the whole revert.
            var ch01 = ExhibitionSpine.Chapters[0];
            Assert.AreEqual("grand-conservatory-with-lush-gardens", ch01.WorldKey, "her world, preserved");
            Assert.IsTrue(ch01.IsChosen);
            Assert.IsFalse(ch01.IsPlaceholder, "a choice is not a stand-in for a missing export");
            Assert.AreEqual("empty-chinese-imperial-temple-hall", ch01.EffectiveWorldKey, "what actually loads");
            Assert.AreSame(ch01, ExhibitionSpine.ByWorldKey("empty-chinese-imperial-temple-hall"));

            var ch02 = ExhibitionSpine.Chapters[1];
            Assert.AreEqual("elegant-floral-palace-interior", ch02.WorldKey, "her world, preserved");
            Assert.AreEqual("celestial-peach-blossom-paradise", ch02.EffectiveWorldKey, "what actually loads");
            Assert.AreSame(ch02, ExhibitionSpine.ByWorldKey("celestial-peach-blossom-paradise"));
        }

        [Test]
        public void EveryWorldTheSpineLoadsIsInTheCatalog()
        {
            // An effective key with no catalog entry fails only on the headset, at stage 04.
            foreach (var c in ExhibitionSpine.Chapters)
                Assert.IsNotNull(MuseXR.Worlds.WorldCatalog.Small.FirstOrDefault(
                        w => w.key == c.EffectiveWorldKey + MuseXR.Worlds.WorldCatalog.SmallSuffix),
                    c.Chapter + " loads '" + c.EffectiveWorldKey + "', which is not in WorldCatalog.Small");
        }

        [Test]
        public void AChapterWithItsOwnWorldLoadsThatWorld()
        {
            var ch05 = ExhibitionSpine.Chapters[4];
            Assert.IsFalse(ch05.IsPlaceholder);
            Assert.AreEqual(ch05.WorldKey, ch05.EffectiveWorldKey);
        }

        [Test]
        public void ExactlyTwoChaptersAreStandingIn_AndTheyAreTheUnshippableOnes()
        {
            var subs = ExhibitionSpine.Placeholders().ToList();
            Assert.AreEqual(2, subs.Count,
                "if this changes, either a world was re-exported or a new gap appeared");
            foreach (var c in subs)
                CollectionAssert.Contains(ExhibitionSpine.MissingAtSmallTier.ToArray(), c.WorldKey);
        }

        [Test]
        public void TwoChaptersNameAWorldThatCannotShipYet()
        {
            // Not a defect in the spine: these two have no 500k re-export. Named so a build can
            // refuse rather than a chapter silently loading nothing on a headset.
            CollectionAssert.AreEquivalent(
                new[] { "sunlit-palace-gardens", "yellow-polka-dot-infinity-room" },
                ExhibitionSpine.MissingAtSmallTier.ToArray());

            foreach (var key in ExhibitionSpine.MissingAtSmallTier)
                Assert.IsNotNull(ExhibitionSpine.ByWorldKey(key), key + " should still be in the spine");
        }

        [Test]
        public void AWorldKeyResolvesToItsChapter()
        {
            Assert.AreEqual("The Studio of the Burning Sky",
                ExhibitionSpine.ByWorldKey("van-gogh-inspired-gallery-interior").Title);
            Assert.AreEqual("Your Dream World",
                ExhibitionSpine.ByWorldKey("fantasy-realm-of-shimmering-spheres").Title);
            Assert.IsNull(ExhibitionSpine.ByWorldKey("not-a-world"));
        }
    }

    /// <summary>
    /// Stage 03. Hers is five regex branches with a default, not a model call, so these pin the
    /// exact wording and — more importantly — the ORDER, because the branches overlap.
    /// </summary>
    public class CurationDataTests
    {
        [Test]
        public void HerFourBranchesMatchTheirKeywords()
        {
            Assert.AreEqual("The Architecture of a Meaningful Life",
                CurationData.For("What makes a life meaningful?").Title);
            Assert.AreEqual("The Beauty of Not Knowing",
                CurationData.For("How do I live with uncertainty?").Title);
            Assert.AreEqual("What Memory Chooses to Keep",
                CurationData.For("What should I keep, and what should I let go?").Title);
            Assert.AreEqual("The Distance Between Two People",
                CurationData.For("Why do I feel alone?").Title);
        }

        [Test]
        public void AnUnmatchedQuestionGetsTheDefault()
        {
            Assert.AreEqual("A Museum Built Around Your Question",
                CurationData.For("What colour is the sky?").Title);
            Assert.AreEqual(CurationData.Default.Title, CurationData.For("").Title);
            Assert.AreEqual(CurationData.Default.Title, CurationData.For(null).Title);
        }

        [Test]
        public void MatchingIsCaseInsensitiveAndSubstring()
        {
            Assert.AreEqual("The Architecture of a Meaningful Life",
                CurationData.For("WHAT IS MEANINGFUL?").Title,
                "lowercased before matching, as hers is");
            Assert.AreEqual("The Architecture of a Meaningful Life",
                CurationData.For("purposeful work").Title,
                "'purpose' matches inside 'purposeful'");
        }

        [Test]
        public void TheBranchOrderIsLoadBearing_FirstMatchWins()
        {
            // This question matches BOTH `meaning` and `let go`. Hers tests meaning first, so it
            // wins. If the branches are ever reordered, this is what catches it.
            Assert.AreEqual("The Architecture of a Meaningful Life",
                CurationData.For("What should I let go of, and what remains meaningful?").Title);
        }

        [Test]
        public void EveryBranchOffersExactlyThreeChapters()
        {
            foreach (var q in new[] { "meaning", "uncertain", "keep", "love", "nothing matches here" })
                Assert.AreEqual(3, CurationData.For(q).Chapters.Count, q);
        }
    
        [Test]
        public void TheOpeningCompanyIsHers()
        {
            // app.js: `selectedCompanions: new Set(["monet", "van_gogh", "socrates"])`, both at
            // boot and in reset(). Starting empty is a divergence, not a simplification: it makes
            // stage 02 arrive with a dead forward button.
            CollectionAssert.AreEquivalent(
                new[] { "monet", "van_gogh", "socrates" }, MuseumJourney.DefaultCompany);
            CollectionAssert.AreEquivalent(
                MuseumJourney.DefaultCompany, new MuseumJourney().InvitedMasterIds);
        }

}
}
