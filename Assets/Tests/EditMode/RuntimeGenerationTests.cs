using System;
using MusePico.Generation;
using MusePico.Tripo;
using NUnit.Framework;

namespace MusePico.Tests
{
    /// <summary>
    /// Runtime generation is the one path where a mistake is charged for. Nobody is watching the
    /// console — it happens on a headset, from a voice command, and a wrong request still bills.
    /// These pin the decisions that are made before the money is spent.
    /// </summary>
    public class VrAssetBudgetTests
    {
        [Test]
        public void EveryTierUsesTheLowPolyModel()
        {
            foreach (VrBudgetTier tier in Enum.GetValues(typeof(VrBudgetTier)))
                Assert.AreEqual(TripoApi.Models.P1, VrAssetBudget.For(tier).Model,
                    tier + " must use P1: it is the only line that produces game-ready topology.");
        }

        [Test]
        public void NoTierExceedsWhatP1WillAccept()
        {
            foreach (VrBudgetTier tier in Enum.GetValues(typeof(VrBudgetTier)))
                Assert.LessOrEqual(VrAssetBudget.For(tier).FaceLimit, VrAssetBudget.P1MaxFaceLimit);
        }

        [Test]
        public void AnOverAmbitiousFaceLimitIsClampedRatherThanSent()
        {
            // 2,000,000 is roughly what the bundled characters are. Sent as-is P1 rejects it, so
            // the request would fail after the user has already waited.
            var budget = VrAssetBudget.For(VrBudgetTier.Exhibit).WithFaceLimit(2_000_000);

            Assert.AreEqual(VrAssetBudget.P1MaxFaceLimit, budget.FaceLimit);
        }

        [Test]
        public void ATinyFaceLimitIsRaisedToSomethingThatStillReadsAsAnObject()
        {
            Assert.AreEqual(VrAssetBudget.MinFaceLimit,
                VrAssetBudget.For(VrBudgetTier.Exhibit).WithFaceLimit(1).FaceLimit);
        }

        [Test]
        public void ApplyTo_NeverRequestsCompression()
        {
            // EXT_meshopt_compression needs an add-on glTFast package that is NOT installed, so a
            // compressed model downloads fine and then fails to import — after it has been paid
            // for. This is the single most expensive way to get the request wrong.
            var request = new TextToModelRequest { Prompt = "x", Compress = "geometry" };
            VrAssetBudget.For(VrBudgetTier.Hero).ApplyTo(request);

            Assert.IsNull(request.Compress);
            StringAssert.DoesNotContain("compress", request.ToJson());
        }

        [Test]
        public void ApplyTo_NeverRequestsQuads()
        {
            // quad:true forces FBX output, which glTFast cannot open at all.
            var request = new TextToModelRequest { Prompt = "x", Quad = true };
            VrAssetBudget.For(VrBudgetTier.Exhibit).ApplyTo(request);

            Assert.AreEqual(false, request.Quad);
        }

        [Test]
        public void ApplyTo_TurnsOffPartsWhichWouldStripTheTexture()
        {
            var request = new TextToModelRequest { Prompt = "x", GenerateParts = true };
            VrAssetBudget.For(VrBudgetTier.Exhibit).ApplyTo(request);

            Assert.AreEqual(false, request.GenerateParts);
        }

        [Test]
        public void TurningTextureOffAlsoTurnsPbrOff()
        {
            // pbr:true forces texture:true server-side, so leaving it set would quietly undo the
            // cheaper, faster untextured request that was asked for.
            var budget = VrAssetBudget.For(VrBudgetTier.Hero).WithTexture(false);
            var request = new TextToModelRequest { Prompt = "x" };
            budget.ApplyTo(request);

            Assert.AreEqual(false, request.Texture);
            Assert.AreEqual(false, request.Pbr);
            Assert.IsNull(request.TextureQuality);
        }

        [Test]
        public void UntexturedIsCheaperAndFasterThanTextured()
        {
            var textured = VrAssetBudget.For(VrBudgetTier.Exhibit);
            var bare = textured.WithTexture(false);

            Assert.Less(bare.EstimatedCredits(false), textured.EstimatedCredits(false));
            Assert.Less(bare.EstimatedSeconds.slow, textured.EstimatedSeconds.fast);
        }

        [Test]
        public void BackgroundTierDropsPbrToSaveTextureMemory()
        {
            Assert.IsFalse(VrAssetBudget.For(VrBudgetTier.Background).Pbr);
            Assert.IsTrue(VrAssetBudget.For(VrBudgetTier.Hero).Pbr);
        }
    }

    public class GenerationSubjectTests
    {
        [Test]
        public void DictationFillerIsStrippedBeforeItIsPaidFor()
        {
            Assert.AreEqual("bust of Socrates", GenerationSubject.Normalise("Can you make me a bust of Socrates."));
            Assert.AreEqual("bronze horse", GenerationSubject.Normalise("  generate   a   bronze   horse  "));
            Assert.AreEqual("Monet", GenerationSubject.Normalise("show me Monet"));
        }

        [Test]
        public void EmptyInputThrowsRatherThanGeneratingScaffoldingAlone()
        {
            // Without this the prompt would still be a valid sentence, generate something, and
            // charge for it.
            Assert.Throws<ArgumentException>(() => GenerationSubject.BuildPrompt(SubjectKind.Artist, "   "));
            Assert.Throws<ArgumentException>(() => GenerationSubject.BuildPrompt(SubjectKind.Artist, "make me a"));
        }

        [Test]
        public void ArtistPromptAsksForAWholeFigure()
        {
            var prompt = GenerationSubject.BuildPrompt(SubjectKind.Artist, "Claude Monet");

            StringAssert.Contains("Claude Monet", prompt);
            StringAssert.Contains("full-body", prompt);
            StringAssert.Contains("single complete object", prompt);
        }

        [Test]
        public void FreeFormPassesTheWordsThrough()
        {
            Assert.AreEqual("a brass telescope", GenerationSubject.BuildPrompt(SubjectKind.FreeForm, "a brass telescope"));
        }

        [Test]
        public void PromptsAreCappedAtTripoLimit()
        {
            var prompt = GenerationSubject.BuildPrompt(SubjectKind.Artist, new string('a', 4000));

            Assert.LessOrEqual(prompt.Length, GenerationSubject.MaxPromptLength);
        }

        [Test]
        public void AskingForAPlaceIsFlagged_BecauseTripoMakesObjectsNotEnvironments()
        {
            Assert.IsTrue(GenerationSubject.LooksLikeAnEnvironment("a Parisian street at dusk"));
            Assert.IsTrue(GenerationSubject.LooksLikeAnEnvironment("Monet's garden"));
            Assert.IsTrue(GenerationSubject.LooksLikeAnEnvironment("a gothic interior"));
        }

        [Test]
        public void AskingForAnObjectIsNotFlagged()
        {
            Assert.IsFalse(GenerationSubject.LooksLikeAnEnvironment("a marble bust of Socrates"));
            Assert.IsFalse(GenerationSubject.LooksLikeAnEnvironment("an easel with a canvas"));
            // "roomy" contains "room" — a substring match here would produce a false warning.
            Assert.IsFalse(GenerationSubject.LooksLikeAnEnvironment("a roomy armchair"));
        }

        [Test]
        public void PropsGetTheCheapestBudget_BecauseManyOfThemShareTheFrame()
        {
            Assert.AreEqual(VrBudgetTier.Background, GenerationSubject.TierFor(SubjectKind.Prop));
            Assert.AreEqual(VrBudgetTier.Exhibit, GenerationSubject.TierFor(SubjectKind.Artist));
        }
    }

    public class GenerationProgressTests
    {
        [Test]
        public void ProgressNeverGoesBackwardsAcrossPhases()
        {
            var order = new[]
            {
                GenerationPhase.Submitting, GenerationPhase.Queued, GenerationPhase.Generating,
                GenerationPhase.Downloading, GenerationPhase.Importing, GenerationPhase.Done,
            };

            var previous = 0f;
            foreach (var phase in order)
            {
                var start = GenerationProgress.FractionFor(phase, 0f);
                var end = GenerationProgress.FractionFor(phase, 1f);

                Assert.GreaterOrEqual(start, previous - 1e-4f, phase + " starts behind the phase before it");
                Assert.GreaterOrEqual(end, start, phase + " does not advance");
                previous = end;
            }

            Assert.AreEqual(1f, previous, 1e-4f, "the phases must add up to a full bar");
        }

        [Test]
        public void GenerationOwnsMostOfTheBar_BecauseItOwnsMostOfTheWait()
        {
            var generating = GenerationProgress.FractionFor(GenerationPhase.Generating, 1f)
                           - GenerationProgress.FractionFor(GenerationPhase.Generating, 0f);

            Assert.Greater(generating, 0.5f);
        }

        [Test]
        public void OutOfRangeInputIsClampedRatherThanOverflowingTheBar()
        {
            Assert.AreEqual(GenerationProgress.FractionFor(GenerationPhase.Generating, 1f),
                GenerationProgress.FractionFor(GenerationPhase.Generating, 5f), 1e-6f);
            Assert.AreEqual(GenerationProgress.FractionFor(GenerationPhase.Generating, 0f),
                GenerationProgress.FractionFor(GenerationPhase.Generating, -3f), 1e-6f);
        }

        [Test]
        public void TerminalPhasesAreNotRunning()
        {
            Assert.IsTrue(new GenerationProgress { Phase = GenerationPhase.Done }.IsTerminal);
            Assert.IsTrue(new GenerationProgress { Phase = GenerationPhase.Failed }.IsTerminal);
            Assert.IsTrue(new GenerationProgress { Phase = GenerationPhase.Cancelled }.IsTerminal);
            Assert.IsTrue(new GenerationProgress { Phase = GenerationPhase.Generating }.IsRunning);
            Assert.IsFalse(new GenerationProgress { Phase = GenerationPhase.Idle }.IsRunning);
        }

        [Test]
        public void QueueDepthIsShownWhenKnown()
        {
            var progress = new GenerationProgress { Phase = GenerationPhase.Queued, QueueAhead = 4 };

            StringAssert.Contains("4", progress.Headline());
        }
    }

    public class GeneratedAssetReportTests
    {
        [Test]
        public void ABundledTripoCharacterWouldBeOverBudget()
        {
            // ~1.96M triangles — the measured figure for the meshes already in this project, and
            // the reason runtime generation has to budget the REQUEST rather than the result.
            Assert.AreEqual(AssetVerdict.OverBudget, GeneratedAssetReport.Judge(1_960_000, 0));
        }

        [Test]
        public void ABudgetedExhibitIsGood()
        {
            Assert.AreEqual(AssetVerdict.Good, GeneratedAssetReport.Judge(10_000, 8L * 1024 * 1024));
        }

        [Test]
        public void TextureMemoryAloneCanBlowTheBudget()
        {
            // A modest mesh with four 2048² PBR maps. Triangles look fine; memory does not.
            var fourMaps = 4 * GeneratedAssetReport.EstimateTextureBytes(2048, 2048, true);

            Assert.AreEqual(AssetVerdict.OverBudget, GeneratedAssetReport.Judge(9_000, fourMaps));
        }

        [Test]
        public void MipmapsCostAThirdMore()
        {
            var without = GeneratedAssetReport.EstimateTextureBytes(1024, 1024, false);
            var with = GeneratedAssetReport.EstimateTextureBytes(1024, 1024, true);

            Assert.AreEqual(4L * 1024 * 1024, without);
            Assert.AreEqual(without * 4 / 3, with);
        }
    }

    public class WavEncoderTests
    {
        [Test]
        public void HeaderIsAValidRiffWaveOfTheRightLength()
        {
            var samples = new float[100];
            var wav = WavEncoder.Encode(samples, 1, 16000);

            Assert.AreEqual(44 + 200, wav.Length);
            Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
            Assert.AreEqual("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
            Assert.AreEqual("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));
            Assert.AreEqual(16000, BitConverter.ToInt32(wav, 24), "sample rate");
            Assert.AreEqual(1, BitConverter.ToInt16(wav, 22), "channels");
            Assert.AreEqual(16, BitConverter.ToInt16(wav, 34), "bits per sample");
            Assert.AreEqual(200, BitConverter.ToInt32(wav, 40), "data chunk size");
        }

        [Test]
        public void LoudSamplesClampInsteadOfWrappingIntoNoise()
        {
            // Without clamping, +1.5 wraps to a large negative value and a loud syllable becomes
            // a burst of static — which sounds like a broken microphone, not a clipped one.
            var wav = WavEncoder.Encode(new[] { 1.5f, -1.5f }, 1, 16000);

            Assert.AreEqual(short.MaxValue, BitConverter.ToInt16(wav, 44));
            Assert.AreEqual(-short.MaxValue, BitConverter.ToInt16(wav, 46));
        }

        [Test]
        public void StereoIsAveragedToMono()
        {
            var mono = WavEncoder.Downmix(new[] { 1f, 0f, 0.5f, 0.5f }, 2);

            CollectionAssert.AreEqual(new[] { 0.5f, 0.5f }, mono);
        }

        [Test]
        public void ResamplingHalvesTheSampleCountWhenTheRateHalves()
        {
            var input = new float[1000];
            var output = WavEncoder.Resample(input, 32000, 16000);

            Assert.AreEqual(500, output.Length);
        }

        [Test]
        public void ResamplingToTheSameRateIsANoOp()
        {
            var input = new[] { 0.1f, 0.2f };

            Assert.AreSame(input, WavEncoder.Resample(input, 16000, 16000));
        }

        [Test]
        public void SilenceTrimsToNothing_SoASilentClipIsNeverSentAndBilled()
        {
            var silence = new float[16000];

            Assert.AreEqual(0, WavEncoder.TrimSilence(silence).Length);
        }

        [Test]
        public void SpeechSurvivesTrimming_WithPadding()
        {
            var samples = new float[16000];
            for (var i = 8000; i < 8100; i++) samples[i] = 0.5f;

            var trimmed = WavEncoder.TrimSilence(samples, 0.01f, 1600);

            Assert.Greater(trimmed.Length, 100, "the speech itself must survive");
            Assert.Less(trimmed.Length, samples.Length, "the surrounding silence must not");
        }

        [Test]
        public void PeakLevelSeparatesSilenceFromSound()
        {
            // This is what tells "the emulator has no microphone" apart from "the microphone is
            // recording silence" — from code the two are otherwise identical.
            Assert.AreEqual(0f, WavEncoder.PeakLevel(new float[128]), 1e-6f);
            Assert.AreEqual(0.75f, WavEncoder.PeakLevel(new[] { 0.1f, -0.75f, 0.3f }), 1e-6f);
        }
    }
}
