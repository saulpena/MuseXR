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
            Assert.AreEqual(home.y, f.Current.y, 1e-3f, "it keeps its height");
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
