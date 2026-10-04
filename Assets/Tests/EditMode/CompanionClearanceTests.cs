using MuseXR.Slots;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    public class CompanionClearanceTests
    {
        static readonly Vector3 Eye = new Vector3(0f, 1.6f, 0f);
        static readonly Vector3 Ahead = Vector3.forward;

        static CompanionClearance.Kind Judge(Vector3 eye, Vector3 gaze, Vector3 feet) =>
            CompanionClearance.Judge(eye.x, eye.z, gaze.x, gaze.z, feet.x, feet.z);

        static ClearanceLog.Fault Add(ClearanceLog log, string id, string phase, Vector3 feet, float dt, float time) =>
            log.Add(id, phase, Eye.x, Eye.z, Ahead.x, Ahead.z, feet.x, feet.z, dt, time);

        [Test]
        public void AFigureStraightAheadIsInTheWay() =>
            Assert.AreEqual(CompanionClearance.Kind.InTheWay, Judge(Eye, Ahead, new Vector3(0f, 0f, 2f)));

        [Test]
        public void AFigureAtTheSideIsClear()   // the crowd's own place: 50 degrees, 1.8 m
        {
            var feet = Quaternion.Euler(0f, 50f, 0f) * Vector3.forward * 1.8f;
            Assert.AreEqual(CompanionClearance.Kind.Clear, Judge(Eye, Ahead, feet));
        }

        [Test]
        public void AFigureAtArmsLengthIsTooCloseWhereverItIs() =>
            Assert.AreEqual(CompanionClearance.Kind.TooClose, Judge(Eye, Ahead, new Vector3(-0.9f, 0f, -0.2f)));

        [Test]
        public void TheBodysEdgeCountsNotJustItsCentre()
        {
            // Centre 27 degrees off the gaze at 1.5 m: its near edge is inside the 25 degree cone.
            var feet = Quaternion.Euler(0f, 27f, 0f) * Vector3.forward * 1.5f;
            Assert.AreEqual(CompanionClearance.Kind.InTheWay, Judge(Eye, Ahead, feet));
        }

        [Test]
        public void FarAheadIsClear() =>
            Assert.AreEqual(CompanionClearance.Kind.Clear, Judge(Eye, Ahead, new Vector3(0f, 0f, 5f)));

        [Test]
        public void PassingThroughTheGazeWithinGraceIsNotAFault()
        {
            var log = new ClearanceLog();
            for (var i = 0; i < 5; i++) Add(log, "monet", "turn", new Vector3(0f, 0f, 2f), 0.1f, i * 0.1f);
            Add(log, "monet", "turn", new Vector3(2f, 0f, 0f), 0.1f, 0.6f);
            Assert.IsTrue(log.Passed, log.Verdict());
        }

        [Test]
        public void StayingInTheWayPastGraceIsOneFault()
        {
            var log = new ClearanceLog();
            for (var i = 0; i < 30; i++) Add(log, "monet", "hold", new Vector3(0f, 0f, 2f), 0.1f, i * 0.1f);
            Assert.AreEqual(1, log.Faults.Count);
            Assert.Greater(log.Faults[0].Seconds, 2.5f);
            StringAssert.StartsWith("VERDICT: NOT YET", log.Verdict());
        }

        [Test]
        public void OneFrameTooCloseFailsAtOnce()
        {
            var log = new ClearanceLog();
            var f = Add(log, "socrates", "teleport", new Vector3(0.3f, 0f, 0.4f), 0.016f, 0f);
            Assert.IsNotNull(f);
            Assert.AreEqual(CompanionClearance.Kind.TooClose, f.Kind);
        }
    }
}
