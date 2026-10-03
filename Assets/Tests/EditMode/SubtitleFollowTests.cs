using MuseXR.UI;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    public class SubtitleFollowTests
    {
        static readonly Vector3 Eye = new Vector3(0f, 1.6f, 0f);
        static readonly Vector3 Head = new Vector3(0f, 1.75f, 2f);   // a speaker 2 m ahead

        [Test]
        public void HomeIsUpperRightOfTheHeadAsTheVisitorSeesIt()
        {
            var h = SubtitleFollow.Home(Head, Eye);
            Assert.AreEqual(SubtitleFollow.Right, h.x - Head.x, 1e-4f, "to the viewer's right");
            Assert.Greater(h.y, Head.y, "above the head");
            Assert.AreEqual(Head.z, h.z, 1e-4f);
        }

        [Test]
        public void ItStaysHomeWhileTheVisitorLooksWithinThirtyFiveDegrees()
        {
            var f = new SubtitleFollow(); f.Reset(Head, Eye);
            var gaze = Quaternion.Euler(0f, 30f, 0f) * Vector3.forward;   // home is ~11 deg right of ahead, so ~19 deg off
            for (int i = 0; i < 60; i++) f.Tick(Head, Eye, gaze, 1f / 60f);
            Assert.IsFalse(f.Following);
            Assert.Less(Vector3.Distance(f.Current, SubtitleFollow.Home(Head, Eye)), 1e-3f);
        }

        [Test]
        public void PastThirtyFiveDegreesItEasesAfterTheGazeAndNeverSnaps()
        {
            var f = new SubtitleFollow(); f.Reset(Head, Eye);
            var home = SubtitleFollow.Home(Head, Eye);
            var gaze = Quaternion.Euler(0f, -90f, 0f) * Vector3.forward;   // turned hard left
            var first = f.Tick(Head, Eye, gaze, 1f / 72f);
            Assert.IsTrue(f.Following);
            Assert.Less(Vector3.Distance(first, home), 0.1f, "one frame moves it a little, not all the way");
            for (int i = 0; i < 72 * 4; i++) f.Tick(Head, Eye, gaze, 1f / 72f);
            Assert.LessOrEqual(SubtitleFollow.OffGaze(Eye, gaze, f.Current), SubtitleFollow.FollowDegrees,
                               "after a few seconds it is back inside the visitor's view");
            Assert.Greater(f.Current.y, Eye.y, "away from the speaker it waits just above eye height");
            Assert.Less(f.Current.y, home.y, "not up at the speaker's head height");
        }

        [Test]
        public void AVisiblePanelDoesNotMoveWhileTheHeadLooksAround()
        {
            var f = new SubtitleFollow(); f.Reset(Head, Eye);
            var start = f.Current;
            for (int i = 0; i < 72 * 6; i++)   // six seconds of glancing between -20 and +40 degrees
            {
                var yaw = 10f + 30f * Mathf.Sin(i / 72f * 2f);
                f.Tick(Head, Eye, Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, 1f / 72f);
                Assert.AreEqual(start, f.Current, "frame " + i + ": the panel is in view and must stand still");
            }
        }

        [Test]
        public void ItDoesNotChaseTheGazeWhileTheVisitorTurnsToReadIt()
        {
            var f = new SubtitleFollow(); f.Reset(Head, Eye);
            var hardLeft = Quaternion.Euler(0f, -90f, 0f) * Vector3.forward;
            f.Tick(Head, Eye, hardLeft, 1f / 72f);
            Assert.IsTrue(f.Following);
            for (int i = 0; i < 72 * 3; i++) f.Tick(Head, Eye, hardLeft, 1f / 72f);
            var settled = f.Current;
            Assert.IsFalse(f.Following, "it arrives and stops");
            // Now the visitor turns towards the panel to read it: it must not slide away or back.
            for (int i = 0; i < 72 * 2; i++)
            {
                var yaw = Mathf.Lerp(-90f, -65f, i / (72f * 2f));
                f.Tick(Head, Eye, Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, 1f / 72f);
                Assert.AreEqual(settled, f.Current, "frame " + i);
            }
        }

        [Test]
        public void ALineFromBehindTheVisitorStartsAtTheEdgeOfViewAndStaysThere()
        {
            var f = new SubtitleFollow();
            var gaze = Quaternion.Euler(0f, 150f, 0f) * Vector3.forward;   // the speaker is behind
            f.Reset(Head, Eye, gaze);
            var start = f.Current;
            Assert.LessOrEqual(SubtitleFollow.OffGaze(Eye, gaze, start), SubtitleFollow.FollowDegrees, "readable at once");
            Assert.Greater(start.y, Eye.y, "lower edge above eye height, clear of what the hands hold");
            Assert.Less(start.y - Eye.y, 0.5f, "and not up out of view");
            for (int i = 0; i < 72 * 2; i++) f.Tick(Head, Eye, gaze, 1f / 72f);
            Assert.AreEqual(start, f.Current, "and it does not move");
        }

        [Test]
        public void TurningBackBringsItHome()
        {
            var f = new SubtitleFollow(); f.Reset(Head, Eye);
            var away = Quaternion.Euler(0f, -90f, 0f) * Vector3.forward;
            for (int i = 0; i < 300; i++) f.Tick(Head, Eye, away, 1f / 72f);
            for (int i = 0; i < 300; i++) f.Tick(Head, Eye, Vector3.forward, 1f / 72f);
            Assert.Less(Vector3.Distance(f.Current, SubtitleFollow.Home(Head, Eye)), 0.01f);
        }
    }
}
