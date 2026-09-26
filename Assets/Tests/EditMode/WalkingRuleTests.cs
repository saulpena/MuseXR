using MusePico.Dialogue;
using NUnit.Framework;

namespace MusePico.Tests.EditMode
{
    public class WalkingRuleTests
    {
        [Test]
        public void OnlyTheGalleryStageWalks_ByDefault()
        {
            foreach (Stage s in System.Enum.GetValues(typeof(Stage)))
                Assert.AreEqual(s == Stage.WorldExploration, WalkingRule.Allowed(s, freeWalk: false, floorReady: true), s.ToString());
        }

        [Test]
        public void FreeWalkOpensEveryStage()
        {
            foreach (Stage s in System.Enum.GetValues(typeof(Stage)))
                Assert.IsTrue(WalkingRule.Allowed(s, freeWalk: true, floorReady: true), s.ToString());
        }

        /// Gravity with no floor drops the rig out of the world (measured at y = -4,791), so no
        /// switch may turn walking on before the world has arrived.
        [Test]
        public void NothingWalksWithoutAFloor()
        {
            foreach (Stage s in System.Enum.GetValues(typeof(Stage)))
            {
                Assert.IsFalse(WalkingRule.Allowed(s, freeWalk: true, floorReady: false), s.ToString());
                Assert.IsFalse(WalkingRule.Allowed(s, freeWalk: false, floorReady: false), s.ToString());
            }
        }
    }
}
