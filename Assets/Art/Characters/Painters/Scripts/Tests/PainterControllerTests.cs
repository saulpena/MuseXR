using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;

namespace Painters.Tests
{
    /// <summary>The panel's names come from PainterAnims; this fails if Painter.controller stops matching them.</summary>
    public class PainterControllerTests
    {
        const string Path = "Assets/Art/Characters/Painters/Painter.controller";

        static string[] StatesFor(string parameter, int count)
        {
            var sm = AssetDatabase.LoadAssetAtPath<AnimatorController>(Path).layers[0].stateMachine;
            return Enumerable.Range(0, count).Select(i => sm.anyStateTransitions
                .Where(t => t.conditions.Any(c => c.parameter == parameter && c.mode == AnimatorConditionMode.Equals && (int)c.threshold == i))
                .Select(t => t.destinationState.name).SingleOrDefault()).ToArray();
        }

        [Test]
        public void EveryTalkStyleReachesTheStateItIsNamedFor()
        {
            var expected = new[] { "Talk_UAL", "Talk_Chat", "Talk_Passionate", "Talk_HandOnHip", "Talk_LeftHandRaised", "Talk_Angry", "Talk_HandsOpen", "Talk_RightHandOpen" };
            Assert.AreEqual(PainterAnims.TalkNames.Length, expected.Length);
            CollectionAssert.AreEqual(expected, StatesFor("TalkStyle", PainterAnims.TalkNames.Length));
        }

        [Test]
        public void EveryWalkStyleReachesTheStateItIsNamedFor()
        {
            var expected = new[] { "Walk_UAL", "Walk_Casual", "Walk_Thoughtful", "Walk_Formal", "Walk_Generated" };
            Assert.AreEqual(PainterAnims.WalkNames.Length, expected.Length);
            CollectionAssert.AreEqual(expected, StatesFor("WalkStyle", PainterAnims.WalkNames.Length));
        }

        [Test]
        public void EveryStateHasAClip()
        {
            var sm = AssetDatabase.LoadAssetAtPath<AnimatorController>(Path).layers[0].stateMachine;
            foreach (var s in sm.states) Assert.IsNotNull(s.state.motion, s.state.name + " has no clip");
        }
    }
}
