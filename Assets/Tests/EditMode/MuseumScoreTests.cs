using NUnit.Framework;
using UnityEngine;
using MusePico.Dialogue;

namespace MusePico.Tests
{
    /// <summary>
    /// The background score, against muse-infinity's <c>lib/backgroundMusic.js</c>.
    ///
    /// The two things worth pinning are the ones a listener notices: that an act keeps its music
    /// across a stage change, and that ducking lowers the score rather than stopping it.
    /// </summary>
    public class MuseumScoreTests
    {
        [Test]
        public void TheOpeningActKeepsOnePiece_SoStagesDoNotRestartIt()
        {
            // Threshold, question, companions and curation are one act. Her map has no entry for
            // two of them and falls through to Promenade; ours must land in the same place.
            Assert.AreEqual(MuseumScore.Promenade, MuseumScore.TrackFor(Stage.Threshold));
            Assert.AreEqual(MuseumScore.Promenade, MuseumScore.TrackFor(Stage.LifeQuestion));
            Assert.AreEqual(MuseumScore.Promenade, MuseumScore.TrackFor(Stage.CompanionSelection));
            Assert.AreEqual(MuseumScore.Promenade, MuseumScore.TrackFor(Stage.AiCuration));
        }

        [Test]
        public void TheGalleryIsDebussy_AndTheSalonIsSatie()
        {
            Assert.AreEqual(MuseumScore.ClairDeLune, MuseumScore.TrackFor(Stage.WorldExploration));

            Assert.AreEqual(MuseumScore.Gymnopedie, MuseumScore.TrackFor(Stage.Summoning));
            Assert.AreEqual(MuseumScore.Gymnopedie, MuseumScore.TrackFor(Stage.Roundtable));
            Assert.AreEqual(MuseumScore.Gymnopedie, MuseumScore.TrackFor(Stage.Decision));
        }

        [Test]
        public void TheExhibitionClosesOnThePieceItOpenedWith()
        {
            Assert.AreEqual(MuseumScore.Promenade, MuseumScore.TrackFor(Stage.WorldTransformation));
            Assert.AreEqual(MuseumScore.Promenade, MuseumScore.TrackFor(Stage.Manifesto));
        }

        [Test]
        public void DuckingLowersTheScore_ItNeverStopsIt()
        {
            var full = MuseumScore.TargetVolume(true, true, ducked: false);
            var ducked = MuseumScore.TargetVolume(true, true, ducked: true);

            Assert.Greater(ducked, 0f, "a score that cuts out when someone speaks reads as a bug");
            Assert.Less(ducked, full);
            Assert.AreEqual(MuseumScore.FullVolume, full, 1e-4f);
        }

        [Test]
        public void EverythingThatIsNotThePlayingPieceTargetsSilence()
        {
            Assert.AreEqual(0f, MuseumScore.TargetVolume(true, isActiveTrack: false, ducked: false), 1e-4f);
            Assert.AreEqual(0f, MuseumScore.TargetVolume(true, isActiveTrack: false, ducked: true), 1e-4f);
        }

        [Test]
        public void SoundOffSilencesEverything()
        {
            Assert.AreEqual(0f, MuseumScore.TargetVolume(false, true, false), 1e-4f);
            Assert.AreEqual(0f, MuseumScore.TargetVolume(false, true, true), 1e-4f);
        }

        [Test]
        public void TheFadeConvergesAndThenStopsExactly()
        {
            var v = 0f;
            for (var i = 0; i < 200; i++) v = MuseumScore.Approach(v, MuseumScore.FullVolume, 0.016f);

            Assert.AreEqual(MuseumScore.FullVolume, v, 1e-5f, "it must land, not approach forever");
            Assert.AreEqual(v, MuseumScore.Approach(v, MuseumScore.FullVolume, 0.016f), 1e-6f);
        }

        [Test]
        public void ADuckLandsInAboutHalfASecond()
        {
            // Her comment: "ducking must react in ~half a second". Anything slower and the master's
            // first words are lost under the music.
            var v = MuseumScore.FullVolume;
            var elapsed = 0f;
            while (elapsed < 0.5f)
            {
                v = MuseumScore.Approach(v, MuseumScore.DuckedVolume, 0.016f);
                elapsed += 0.016f;
            }

            // Her rate gives 0.78^(0.5/0.06) = 0.126 of the gap left, so 87.4% travelled. Measured
            // here at 88.0%. The bar is set just under that: the point is that the master's first
            // word is not lost under the music, not that the fade is instant.
            var travelled = (MuseumScore.FullVolume - v) / (MuseumScore.FullVolume - MuseumScore.DuckedVolume);
            Assert.Greater(travelled, 0.85f, "half a second should be most of the way down");
            Assert.Less(travelled, 1f, "and it should still be easing, not snapping");
        }

        [Test]
        public void TheFadeIsFrameRateIndependent()
        {
            // One 32 ms frame must land where two 16 ms frames do, or the duck runs at whatever
            // speed the headset manages that second.
            var coarse = MuseumScore.Approach(0f, MuseumScore.FullVolume, 0.032f);

            var fine = MuseumScore.Approach(0f, MuseumScore.FullVolume, 0.016f);
            fine = MuseumScore.Approach(fine, MuseumScore.FullVolume, 0.016f);

            Assert.AreEqual(coarse, fine, 1e-4f);
        }

        [Test]
        public void AZeroLengthFrameChangesNothing()
        {
            Assert.AreEqual(0.2f, MuseumScore.Approach(0.2f, MuseumScore.FullVolume, 0f), 1e-6f);
        }

        [Test]
        public void EveryStageHasAPiece()
        {
            foreach (Stage stage in System.Enum.GetValues(typeof(Stage)))
                Assert.IsNotEmpty(MuseumScore.TrackFor(stage), stage + " has no music");
        }
    }
}
