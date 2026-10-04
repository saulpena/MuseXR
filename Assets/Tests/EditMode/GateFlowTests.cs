using MusePico.Dialogue;
using NUnit.Framework;

namespace MusePico.Tests
{
    public class GateFlowTests
    {
        [Test]
        public void HerSampleQuestionLeadsHerThree()
        {
            Assert.AreEqual("What makes a life not wasted?", GateFlow.Samples[0]);
            Assert.AreEqual(3, GateFlow.Samples.Count);
        }

        [Test]
        public void ChoosingLettersTheArchAndOpensTheDoorsAfterTheUndoBar()
        {
            var g = new GateFlow();
            g.ChooseSample(0);
            Assert.AreEqual(GateFlow.Phase.Chosen, g.Current);
            Assert.AreEqual("“What makes a life not wasted?”", GateFlow.Lettering(g.Question));
            g.Tick(2.9f);
            Assert.AreEqual(GateFlow.Phase.Chosen, g.Current, "doors stay shut during the 3 s undo bar");
            g.Tick(0.2f);
            Assert.AreEqual(GateFlow.Phase.DoorsOpen, g.Current);
        }

        [Test]
        public void UndoWithinThreeSecondsTakesTheQuestionBack()
        {
            var g = new GateFlow();
            g.ChooseSample(1);
            g.Tick(1f);
            Assert.IsTrue(g.Undo());
            Assert.AreEqual(GateFlow.Phase.Asking, g.Current);
            Assert.AreEqual(string.Empty, g.Question);
        }

        [Test]
        public void UndoAfterTheBarIsGoneDoesNothing()
        {
            var g = new GateFlow();
            g.ChooseSample(1);
            g.Tick(3.5f);
            Assert.IsFalse(g.Undo());
            Assert.AreEqual(GateFlow.Phase.DoorsOpen, g.Current);
        }

        [Test]
        public void ASpokenQuestionReplacesTheSampleAndRestartsTheBar()
        {
            var g = new GateFlow();
            g.ChooseSample(0);
            g.Tick(3.5f);
            Assert.IsTrue(g.SetSpoken("  Who am I when nobody is watching?  "));
            Assert.AreEqual("Who am I when nobody is watching?", g.Question);
            Assert.IsTrue(g.Spoken);
            Assert.AreEqual(GateFlow.Phase.Chosen, g.Current, "a retake closes the doors and restarts the bar");
        }

        [Test]
        public void SilenceIsNotAQuestion()
        {
            var g = new GateFlow();
            Assert.IsFalse(g.SetSpoken("   "));
            Assert.AreEqual(GateFlow.Phase.Asking, g.Current);
        }

        [Test]
        public void TheDoorsMustBeOpenToWalkThrough()
        {
            var g = new GateFlow();
            Assert.IsFalse(g.Enter());
            g.ChooseSample(0);
            Assert.IsFalse(g.Enter());
            g.Tick(3f);
            Assert.IsTrue(g.Enter());
            Assert.AreEqual(GateFlow.Phase.Entered, g.Current);
            g.ChooseSample(2);
            Assert.AreEqual("What makes a life not wasted?", g.Question, "nothing changes once through the doors");
        }

        [Test]
        public void ALongSpokenQuestionIsCappedAtHerLimit()
        {
            var g = new GateFlow();
            g.SetSpoken(new string('a', 400));
            Assert.AreEqual(GateFlow.MaxChars, g.Question.Length);
        }
    }
}
