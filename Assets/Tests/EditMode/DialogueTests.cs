using System.Collections.Generic;
using System.IO;
using MusePico.Dialogue;
using NUnit.Framework;

namespace MusePico.Tests
{
    /// <summary>
    /// The port of muse-infinity's dialogue, pinned.
    ///
    /// These matter more than most tests here because the thing being protected is not code, it is
    /// months of prompt tuning: six authored fields per master, several of them written to close a
    /// specific observed failure. A silent drop of one clause would not break a build, would not
    /// throw, and would show up only as three masters gradually sounding like one — which is the
    /// exact failure the original spent all that effort avoiding.
    /// </summary>
    public class MasterRosterTests
    {
        static MasterRosterData Load()
        {
            var path = Path.Combine(UnityEngine.Application.dataPath, "Dialogue/masters.json");
            Assert.IsTrue(File.Exists(path), "masters.json is missing — the lenses are the feature.");
            return MasterRoster.Parse(File.ReadAllText(path));
        }

        [Test]
        public void AllSevenMastersSurvivedTheExport()
        {
            var roster = Load();

            Assert.AreEqual(7, roster.masters.Length);
            CollectionAssert.AreEquivalent(
                new[] { "monet", "picasso", "hilma", "van_gogh", "frida", "socrates", "morisot" },
                System.Array.ConvertAll(roster.masters, m => m.id));
        }

        [Test]
        public void EveryMasterKeptEveryLensField()
        {
            // The six fields are the whole separation mechanism. An empty one is a master who
            // will quietly sound like the others.
            foreach (var master in Load().masters)
            {
                Assert.IsNotEmpty(master.systemPrompt, master.id + " lost its SHAPE");
                Assert.IsNotEmpty(master.questionStyle, master.id + " lost its move");
                Assert.IsNotEmpty(master.lens, master.id + " lost its lens");
                Assert.IsNotNull(master.attention);
                Assert.Greater(master.attention.Length, 0, master.id + " lost its attention list");
                Assert.Greater(master.vocabulary.Length, 0, master.id + " lost its vocabulary");
                Assert.Greater(master.forbidden.Length, 0, master.id + " lost its forbidden words");
                Assert.IsNotEmpty(master.voiceId, master.id + " lost its casting");
            }
        }

        [Test]
        public void TheEffectVocabularyMatchesTheOriginal()
        {
            CollectionAssert.AreEqual(new[] { "mist", "fracture", "garden", "network" }, Load().effects);
        }

        [Test]
        public void NoMasterIsCastWithAnotherMastersVoice()
        {
            var seen = new HashSet<string>();
            foreach (var master in Load().masters)
                Assert.IsTrue(seen.Add(master.voiceId), master.id + " shares a voice with another master");
        }

        [Test]
        public void AnEmptyInvitationFallsBackToTheAuthoredThree()
        {
            var chosen = MasterRoster.Select(Load(), null);

            Assert.AreEqual(3, chosen.Count);
            CollectionAssert.AreEqual(
                new[] { "monet", "van_gogh", "socrates" },
                chosen.ConvertAll(m => m.id));
        }

        [Test]
        public void InvitedMastersComeFirst_ThenTheRosterTopsUpToThree()
        {
            var chosen = MasterRoster.Select(Load(), new[] { "frida" });

            Assert.AreEqual(3, chosen.Count);
            Assert.AreEqual("frida", chosen[0].id);
        }

        [Test]
        public void InvitingTheSameMasterTwiceDoesNotSeatThemTwice()
        {
            // Two readings under one name is one voice twice, which is the failure the lens work
            // exists to prevent.
            var chosen = MasterRoster.Select(Load(), new[] { "monet", "monet", "monet" });

            Assert.AreEqual(3, chosen.Count);
            Assert.AreEqual(3, new HashSet<string>(chosen.ConvertAll(m => m.id)).Count);
        }

        [Test]
        public void AParticipantWithNoLensIsNeverSeated()
        {
            var roster = new MasterRosterData
            {
                effects = new[] { "mist" },
                defaultMasterIds = new string[0],
                masters = new[]
                {
                    new MasterLens { id = "ghost" },                                   // no lens at all
                    new MasterLens { id = "a", systemPrompt = "x", attention = new[] { "y" } },
                    new MasterLens { id = "b", systemPrompt = "x", attention = new[] { "y" } },
                },
            };

            var chosen = MasterRoster.Select(roster, new[] { "ghost", "a", "b" });

            CollectionAssert.AreEqual(new[] { "a", "b" }, chosen.ConvertAll(m => m.id));
        }

        [Test]
        public void AnUnrecognisedSpeakerIdFallsBackToThePositionThatWasAsked()
        {
            // The model is told to copy the id verbatim. When it does not, the reply must not end
            // up under the wrong master's name.
            var asked = MasterRoster.Select(Load(), null);

            Assert.AreEqual(asked[1].id, MasterRoster.Resolve(asked, "not-a-master", 1).id);
            Assert.AreEqual(asked[2].id, MasterRoster.Resolve(asked, asked[2].id, 0).id,
                "a recognised id wins over position");
        }
    }

    public class PerspectivePromptTests
    {
        static MasterLens Master() => new MasterLens
        {
            id = "monet",
            fullName = "Claude Monet",
            systemPrompt = "STAGE A CHANGE IN TIME.",
            questionStyle = "Turns a claim into a question about change.",
            lens = "Sees light as a momentary envelope.",
            attention = new[] { "the hour of the light", "where edges dissolve" },
            vocabulary = new[] { "shimmer", "hazy" },
            forbidden = new[] { "doctrine", "theorem" },
        };

        [Test]
        public void TheMasterBlockLeadsWithTheShape()
        {
            // systemPrompt leads because it carries the speech act. Demoting it to a description
            // line is how two masters end up performing the same move with different nouns.
            var block = PerspectivePrompt.DescribeMaster(Master(), 0);
            var lines = block.Split('\n');

            Assert.AreEqual("--- PERSPECTIVE 1 ---", lines[0]);
            Assert.AreEqual("speakerId (copy verbatim): monet", lines[1]);
            Assert.AreEqual("speaker (copy verbatim): Claude Monet", lines[2]);
            StringAssert.StartsWith("THE SHAPE OF YOUR REPLY", lines[3]);
            StringAssert.Contains("STAGE A CHANGE IN TIME.", lines[3]);
        }

        [Test]
        public void TheMasterBlockCarriesAllSixLensFields()
        {
            var block = PerspectivePrompt.DescribeMaster(Master(), 0);

            StringAssert.Contains("The move this voice makes: Turns a claim", block);
            StringAssert.Contains("Lens: Sees light", block);
            StringAssert.Contains("Attend only to: the hour of the light; where edges dissolve", block);
            StringAssert.Contains("Draw on this vocabulary: shimmer, hazy", block);
            StringAssert.Contains("Never use these words: doctrine, theorem", block);
        }

        [Test]
        public void SharedRulesReachTheModelOnce_AndAreNotInTheMasterBlock()
        {
            // THE structural invariant of the whole port. An earlier revision of the original
            // emitted shared clauses two to three times per call while the fields that separate
            // the masters arrived once — repetition is emphasis, so the model was emphatically
            // told to be a careful museum voice and only briefly told to be THIS master.
            var instructions = PerspectivePrompt.SoloInstructions(new[] { "mist", "fracture" });
            var block = PerspectivePrompt.DescribeMaster(Master(), 0);

            StringAssert.Contains(PerspectivePrompt.InterpretiveFraming, instructions);
            StringAssert.DoesNotContain(PerspectivePrompt.InterpretiveFraming, block);
            StringAssert.DoesNotContain("under 50 words", block);

            Assert.AreEqual(1, Occurrences(instructions, PerspectivePrompt.InterpretiveFraming),
                "the compliance framing must appear exactly once per call");
        }

        [Test]
        public void TheSharedRulesKeepTheClausesThatWereMeasuredIntoThem()
        {
            var rules = PerspectivePrompt.SharedPerspectiveRules;

            // 50, not 55: restating 55 produced four breaches in thirty replies.
            StringAssert.Contains("under 50 words", rules);
            // Every voice reaches for a deictic opener, so it identifies none of them.
            StringAssert.Contains("Never open with a bare deictic imperative", rules);
            // Restating the disclaimer gave all three readings the same opening phrasing.
            StringAssert.Contains("never write one into the reading", rules);
            StringAssert.Contains("Answer the visitor's specific question", rules);
        }

        [Test]
        public void SoloInstructionsOfferTheEffectVocabulary()
        {
            var instructions = PerspectivePrompt.SoloInstructions(new[] { "mist", "fracture", "garden", "network" });

            StringAssert.Contains("mist, fracture, garden, network", instructions);
            StringAssert.Contains("the shape, not the word list", instructions);
        }

        [Test]
        public void TheInputCarriesQuestionThenArtworkThenMasters()
        {
            var input = PerspectivePrompt.BuildInput(
                "What am I missing?",
                new List<MasterLens> { Master() },
                new ArtworkContext { Title = "Water Lilies", Artist = "Claude Monet", Date = "1906" });

            StringAssert.StartsWith("Visitor question: What am I missing?", input);
            StringAssert.Contains("Artwork in focus: Water Lilies by Claude Monet (1906)", input);
            StringAssert.Contains("--- PERSPECTIVE 1 ---", input);
        }

        [Test]
        public void AnUnknownArtworkStillProducesAWellFormedLine()
        {
            Assert.AreEqual("Artwork in focus: unknown by unknown (date unknown)",
                PerspectivePrompt.DescribeArtwork(ArtworkContext.Unknown));
        }

        [Test]
        public void AnEnormousQuestionIsTruncatedBeforeItIsPaidFor()
        {
            var input = PerspectivePrompt.BuildInput(
                new string('q', 5000), new List<MasterLens> { Master() }, ArtworkContext.Unknown);

            Assert.Less(input.Length, 5000, "the question must be clamped, as the original clamps it");
        }

        static int Occurrences(string haystack, string needle)
        {
            var count = 0;
            var at = haystack.IndexOf(needle, System.StringComparison.Ordinal);
            while (at >= 0)
            {
                count++;
                at = haystack.IndexOf(needle, at + needle.Length, System.StringComparison.Ordinal);
            }
            return count;
        }
    }

    public class PerspectiveValidationTests
    {
        static readonly string[] Effects = { "mist", "fracture", "garden", "network" };

        static Perspective Good() => new Perspective
        {
            speakerId = "monet", speaker = "Claude Monet", text = "The hour is already leaving.", effect = "mist",
        };

        [Test]
        public void AGoodPerspectivePasses()
        {
            Assert.IsNull(PerspectiveValidation.DescribeInvalid(Good(), Effects));
        }

        [Test]
        public void EveryEmptyFieldIsNamedSpecifically()
        {
            var noText = Good(); noText.text = "  ";
            var noSpeaker = Good(); noSpeaker.speakerId = "";

            StringAssert.Contains("text was empty", PerspectiveValidation.DescribeInvalid(noText, Effects));
            StringAssert.Contains("speakerId was empty", PerspectiveValidation.DescribeInvalid(noSpeaker, Effects));
            StringAssert.Contains("not an object", PerspectiveValidation.DescribeInvalid(null, Effects));
        }

        [Test]
        public void AnEffectOutsideTheSharedVocabularyIsRejected()
        {
            // The client maps effect to scene lighting. An unknown value renders nothing and
            // looks like the effect system being broken.
            var stray = Good(); stray.effect = "sparkles";

            StringAssert.Contains("outside the shared vocabulary",
                PerspectiveValidation.DescribeInvalid(stray, Effects));
        }

        [Test]
        public void TheWrongNumberOfPerspectivesIsDetected()
        {
            // strict json_schema has no dependable array-length keyword, so this DETECTS rather
            // than prevents — survivable only because a detected mismatch is retryable.
            var one = new List<Perspective> { Good() };

            StringAssert.Contains("expected 3 perspectives, received 1",
                PerspectiveValidation.DescribeInvalidSet(one, 3, Effects));
        }

        [Test]
        public void TwoMastersClaimingTheSameNameIsDetected()
        {
            var set = new List<Perspective> { Good(), Good(), Good() };

            StringAssert.Contains("same speakerId", PerspectiveValidation.DescribeInvalidSet(set, 3, Effects));
        }
    }

    public class ResponsesEnvelopeTests
    {
        [Test]
        public void TheOutputTextIsFoundWhereverItSits()
        {
            // A Responses payload can lead with a reasoning item that has no content at all, so
            // indexing output[0] would find nothing on a perfectly good reply.
            const string json =
                "{\"output\":[{\"type\":\"reasoning\"},{\"content\":[{\"type\":\"output_text\"," +
                "\"text\":\"{\\\"speakerId\\\":\\\"monet\\\"}\"}]}]}";

            Assert.AreEqual("{\"speakerId\":\"monet\"}", ResponsesEnvelope.ExtractOutputText(json));
        }

        [Test]
        public void JunkYieldsEmptyRatherThanThrowing()
        {
            Assert.AreEqual("", ResponsesEnvelope.ExtractOutputText("not json at all"));
            Assert.AreEqual("", ResponsesEnvelope.ExtractOutputText(""));
            Assert.AreEqual("", ResponsesEnvelope.ExtractOutputText("{\"output\":[]}"));
        }
    }

    /// <summary>
    /// Ported from muse-infinity's voiceNarrator, where the reason is recorded: MiniMax T2A
    /// synthesises one utterance per request, and a long reply sent whole comes back cut off
    /// mid-sentence, with no error to show for it.
    ///
    /// MEASURED: a 44-word reading is 232 characters, so a <b>compliant</b> 50-word reading is
    /// around 265 and never splits. This is the safety net for the readings that <b>breach</b> the
    /// word limit — and her prompt notes record four breaches in thirty replies, one at 59 words.
    /// </summary>
    public class SpeechSegmenterTests
    {
        [Test]
        public void AShortReadingIsNotDisturbed()
        {
            // The common case. Splitting this would change how every normal line is synthesised.
            var text = "The hour is already leaving it.";

            CollectionAssert.AreEqual(new[] { text }, SpeechSegmenter.Split(text));
        }

        [Test]
        public void ACompliantFiftyWordReadingStaysInOneSegment()
        {
            // 44 words, 232 characters. Measured, not assumed — this is what the word limit
            // actually produces, and splitting it would change every normal line.
            var reading =
                "The hour is already leaving it, and by dusk that water would hold none of this green. " +
                "Look again at noon and the lilies will have closed their colour into the shadow beneath them. " +
                "Nothing here is settled enough to be called a scene.";

            Assert.Less(reading.Length, SpeechSegmenter.SegmentCharLimit);
            Assert.AreEqual(1, SpeechSegmenter.Split(reading).Count);
        }

        [Test]
        public void AReadingThatBreachesTheWordLimitIsSplitRatherThanTruncated()
        {
            // 61 words, 326 characters — the failure mode her notes record, where the model runs
            // past "under 50 words". Sent whole, the tail is lost silently.
            var reading =
                "The hour is already leaving it, and by dusk that water would hold none of this green. " +
                "Look again at noon and the lilies will have closed their colour into the shadow beneath them. " +
                "The surface keeps nothing, and returns a different picture to every hour that asks it. " +
                "Nothing here is settled enough to be called a scene at all.";

            Assert.Greater(reading.Length, SpeechSegmenter.SegmentCharLimit);

            var segments = SpeechSegmenter.Split(reading);

            Assert.Greater(segments.Count, 1, "a reading over the limit must be split");
            foreach (var segment in segments)
                Assert.LessOrEqual(segment.Length, SpeechSegmenter.SegmentCharLimit + 120,
                    "no segment should run far past the limit");
        }

        [Test]
        public void SegmentsBreakOnSentences_NotMidClause()
        {
            // A segment ending mid-clause is synthesised with the wrong intonation, because the
            // model cannot hear the rest of the sentence coming.
            var reading = new string('a', 200) + ". " + new string('b', 200) + ". " + new string('c', 200) + ".";

            foreach (var segment in SpeechSegmenter.Split(reading))
                StringAssert.EndsWith(".", segment.TrimEnd());
        }

        [Test]
        public void NoWordsAreLost()
        {
            var reading =
                "He worked the paint while the light fought him. You can feel the hand hurrying! " +
                "Did he ever stop to look? Perhaps not… and that is the whole of it. " +
                "The canvas keeps the speed of the arm that made it, which is more than most pictures manage.";

            var rejoined = string.Join(" ", SpeechSegmenter.Split(reading)).Replace("  ", " ");

            foreach (var word in new[] { "worked", "hurrying", "Perhaps", "canvas", "manage" })
                StringAssert.Contains(word, rejoined);
        }

        [Test]
        public void ASingleSentenceLongerThanTheLimitIsEmittedRatherThanDropped()
        {
            // There is no boundary to break on. One over-long segment beats silently losing words.
            var runOn = new string('x', 500) + ".";

            var segments = SpeechSegmenter.Split(runOn);

            Assert.AreEqual(1, segments.Count);
            Assert.AreEqual(501, segments[0].Length);
        }

        [Test]
        public void EmptyTextProducesNoRequests()
        {
            CollectionAssert.IsEmpty(SpeechSegmenter.Split(""));
            CollectionAssert.IsEmpty(SpeechSegmenter.Split("   "));
            CollectionAssert.IsEmpty(SpeechSegmenter.Split(null));
        }

        [Test]
        public void TheLimitMatchesMuseInfinity()
        {
            // Both projects must segment the same reading the same way, or the two builds speak
            // the same words with different phrasing.
            Assert.AreEqual(280, SpeechSegmenter.SegmentCharLimit);
        }
    }

    public class MiniMaxVoiceTests
    {
        [Test]
        public void ANonZeroStatusIsAFailureEvenOnHttp200()
        {
            // Same trap as Tripo's envelope: the HTTP layer says fine, the body says no.
            var parsed = MiniMaxResponse.Parse(
                "{\"base_resp\":{\"status_code\":1004,\"status_msg\":\"invalid api key\"}}");

            StringAssert.Contains("1004", parsed.Error);
            StringAssert.Contains("invalid api key", parsed.Error);
        }

        [Test]
        public void InsufficientBalanceSaysSo_BecauseItLooksLikeABrokenIntegration()
        {
            // Hit for real on 14 Sep: the key authenticates and the free voice list works, so
            // everything looks configured — only the paid call is refused. Without the hint this
            // reads as "the voices are broken" rather than "the account is empty".
            var parsed = MiniMaxResponse.Parse(
                "{\"base_resp\":{\"status_code\":1008,\"status_msg\":\"insufficient balance\"}}");

            StringAssert.Contains("insufficient balance", parsed.Error);
            StringAssert.Contains("no credit", parsed.Error);
            StringAssert.Contains("Listing voices is free", parsed.Error);
        }

        [Test]
        public void ASuccessfulBodyYieldsTheAudio()
        {
            var parsed = MiniMaxResponse.Parse("{\"base_resp\":{\"status_code\":0},\"data\":{\"audio\":\"00ff\"}}");

            Assert.IsNull(parsed.Error);
            Assert.AreEqual("00ff", parsed.AudioHex);
        }

        [Test]
        public void AnEmptyAudioFieldIsReportedRatherThanPlayedAsSilence()
        {
            var parsed = MiniMaxResponse.Parse("{\"base_resp\":{\"status_code\":0},\"data\":{\"audio\":\"\"}}");

            StringAssert.Contains("no audio", parsed.Error);
        }

        [Test]
        public void HexPcmDecodesLittleEndianSigned16()
        {
            // 0x0000 = 0, 0x00FF -> bytes 00,FF -> 0xFF00 = -256, 0xFF7F -> bytes FF,7F -> 32767
            var samples = PcmCodec.DecodeHexPcm16("000000fffF7f");

            Assert.AreEqual(3, samples.Length);
            Assert.AreEqual(0f, samples[0], 1e-6f);
            Assert.AreEqual(-256f / 32768f, samples[1], 1e-6f);
            Assert.AreEqual(32767f / 32768f, samples[2], 1e-4f);
        }

        [Test]
        public void NonHexInputYieldsNothingRatherThanNoise()
        {
            CollectionAssert.IsEmpty(PcmCodec.DecodeHexPcm16("zzzz"));
            CollectionAssert.IsEmpty(PcmCodec.DecodeHexPcm16(""));
        }
    }
}
