using MuseXR.Interaction;
using MuseXR.Slots;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>The time ring turned from code (the desktop keys): it once stuck on Mist, turning by the target angle.</summary>
    public class TimeRingDialStepTests
    {
        [Test]
        public void SteppingWalksEveryDetentBothWays()
        {
            // Each detent plays its water-drop tick, whose clean-up calls Destroy - refused in edit mode, and not under test.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var go = new GameObject("dial");
            try
            {
                var dial = TimeRingDial.Make(go, null, null);
                Assert.AreEqual(1, dial.Detents.Detent, "starts on Afternoon");
                dial.Step(-1); Assert.AreEqual(0, dial.Detents.Detent, "Mist");
                dial.Step(1); Assert.AreEqual(1, dial.Detents.Detent, "back to Afternoon");
                dial.Step(1); Assert.AreEqual(2, dial.Detents.Detent, "Dusk");
                dial.Step(1); Assert.AreEqual(2, dial.Detents.Detent, "stops at Dusk");
                dial.Step(-1); dial.Step(-1); Assert.AreEqual(0, dial.Detents.Detent, "all the way back to Mist");
                Assert.AreEqual(DialDetents.AngleOf(0), dial.Detents.Angle, 1e-3f, "and sits on its angle");
            }
            finally { Object.DestroyImmediate(go); UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false; }
        }
    }
}
