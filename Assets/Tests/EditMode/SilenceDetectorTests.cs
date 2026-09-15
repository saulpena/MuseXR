using MusePico.Generation;
using NUnit.Framework;

namespace MusePico.Tests
{
    /// <summary>
    /// The rules ported from DuckyMayhem's VoiceInputManager, pinned so they survive being moved
    /// into a headset. Each test is a situation someone actually gets into while wearing one.
    /// </summary>
    public class SilenceDetectorTests
    {
        static SilenceDetector New() => new SilenceDetector
        {
            Threshold = 0.01f,
            SilenceTimeout = 1.5f,
            MaxSeconds = 12f,
        };

        const float Frame = 1f / 60f;

        static SilenceDetector.StopReason Run(ref SilenceDetector detector, float level, float seconds)
        {
            var frames = (int)(seconds / Frame);
            for (var i = 0; i < frames; i++)
            {
                var reason = detector.Update(level, Frame);
                if (reason != SilenceDetector.StopReason.Continue) return reason;
            }
            return SilenceDetector.StopReason.Continue;
        }

        [Test]
        public void ThePauseBeforeSomeoneStartsSpeakingDoesNotEndTheUtterance()
        {
            // Press the button, then think for two seconds before speaking. Ending here would
            // make the feature feel broken for anyone who does not talk instantly.
            var detector = New();

            Assert.AreEqual(SilenceDetector.StopReason.Continue, Run(ref detector, 0f, 2f));
        }

        [Test]
        public void SilenceAfterSpeakingEndsTheUtterance()
        {
            var detector = New();
            Run(ref detector, 0.2f, 1f);                                   // speaking

            Assert.AreEqual(SilenceDetector.StopReason.Silence, Run(ref detector, 0f, 2f));
        }

        [Test]
        public void AShortPauseMidSentenceDoesNotCutSomeoneOff()
        {
            // "a marble bust of ... Socrates" — a one-second beat while they remember the name.
            var detector = New();
            Run(ref detector, 0.2f, 0.5f);
            Assert.AreEqual(SilenceDetector.StopReason.Continue, Run(ref detector, 0f, 1.0f));
            Assert.AreEqual(SilenceDetector.StopReason.Continue, Run(ref detector, 0.2f, 0.5f));
        }

        [Test]
        public void TheSilenceTimerResetsWhenTheyStartSpeakingAgain()
        {
            var detector = New();
            Run(ref detector, 0.2f, 0.3f);
            Run(ref detector, 0f, 1.0f);
            Run(ref detector, 0.2f, 0.1f);

            Assert.Less(detector.SilenceFor, 0.1f);
        }

        [Test]
        public void SomeoneWhoNeverStopsTalkingIsCutOffAtTheCeiling()
        {
            var detector = New();

            Assert.AreEqual(SilenceDetector.StopReason.MaxDuration, Run(ref detector, 0.3f, 13f));
        }

        [Test]
        public void TotalSilenceIsReportedAsNoAudioRatherThanAsAFinishedUtterance()
        {
            // The emulator case. A microphone that opens and hears nothing looks exactly like a
            // missing one unless this is separated out — and the fix for each is different.
            var detector = New();

            Assert.AreEqual(SilenceDetector.StopReason.NeverHeardAnything, Run(ref detector, 0f, 5f));
        }

        [Test]
        public void OneLoudClickDoesNotCountAsSpeech()
        {
            // Mean amplitude rather than peak is the whole reason: a knock on the desk pins the
            // peak but barely moves the mean over a frame of samples.
            var quietWithOneSpike = new float[512];
            quietWithOneSpike[100] = 1f;

            Assert.Greater(WavEncoder.PeakLevel(quietWithOneSpike), 0.9f, "peak sees the click");
            Assert.Less(WavEncoder.MeanLevel(quietWithOneSpike), 0.01f, "mean does not");
        }

        [Test]
        public void MeanLevelIsTheAverageMagnitude()
        {
            Assert.AreEqual(0.5f, WavEncoder.MeanLevel(new[] { 0.5f, -0.5f, 0.5f, -0.5f }), 1e-6f);
            Assert.AreEqual(0f, WavEncoder.MeanLevel(new float[64]), 1e-6f);
        }

        [Test]
        public void ResetClearsEverythingForTheNextUtterance()
        {
            var detector = New();
            Run(ref detector, 0.2f, 1f);
            detector.Reset();

            Assert.IsFalse(detector.HeardAnything);
            Assert.AreEqual(0f, detector.Elapsed, 1e-6f);
            Assert.AreEqual(0f, detector.SilenceFor, 1e-6f);
        }
    }
}
