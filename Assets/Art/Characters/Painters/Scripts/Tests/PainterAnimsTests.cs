using System;
using NUnit.Framework;

namespace Painters.Tests
{
    public class PainterAnimsTests
    {
        [Test]
        public void TalkStandsStillAndPicksTheStyle()
        {
            var s = PainterAnims.Walk(default, 2);
            s = PainterAnims.Talk(s, 5);
            Assert.AreEqual(0f, s.Speed);
            Assert.IsTrue(s.Talking);
            Assert.IsFalse(s.Listening);
            Assert.AreEqual(5, s.TalkStyle);
            Assert.AreEqual("talking: Angry", PainterAnims.Describe(s));
        }

        [Test]
        public void WalkStopsTalkingAndMovesAboveTheControllerThreshold()
        {
            var s = PainterAnims.Talk(default, 3);
            s = PainterAnims.Walk(s, 1);
            Assert.Greater(s.Speed, 0.1f);
            Assert.IsFalse(s.Talking);
            Assert.AreEqual(1, s.WalkStyle);
            Assert.AreEqual(3, s.TalkStyle, "the last talk style is remembered for next time");
            Assert.AreEqual("walking: Casual", PainterAnims.Describe(s));
        }

        [Test]
        public void ListenAndIdleClearTheOthers()
        {
            var s = PainterAnims.Listen(PainterAnims.Walk(default, 0));
            Assert.IsTrue(s.Listening);
            Assert.AreEqual(0f, s.Speed);
            Assert.AreEqual("listening", PainterAnims.Describe(s));
            s = PainterAnims.Idle(PainterAnims.Talk(s, 1));
            Assert.IsFalse(s.Talking || s.Listening);
            Assert.AreEqual("idle", PainterAnims.Describe(s));
        }

        [Test]
        public void StylesOutsideTheControllerAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PainterAnims.Talk(default, PainterAnims.TalkNames.Length));
            Assert.Throws<ArgumentOutOfRangeException>(() => PainterAnims.Walk(default, -1));
        }

        [Test]
        public void ShuffleNeverRepeatsAndReachesEveryOtherStyle()
        {
            var rng = new Random(7);
            for (int current = 0; current < PainterAnims.TalkNames.Length; current++)
            {
                var seen = new bool[PainterAnims.TalkNames.Length];
                for (int i = 0; i < 400; i++)
                {
                    int next = PainterAnims.OtherTalk(current, rng);
                    Assert.AreNotEqual(current, next);
                    seen[next] = true;
                }
                for (int s = 0; s < seen.Length; s++)
                    if (s != current) Assert.IsTrue(seen[s], "style " + s + " never picked from " + current);
            }
        }
    }
}
