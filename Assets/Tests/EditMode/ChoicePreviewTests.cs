using System.Linq;
using MuseXR.Interaction;
using NUnit.Framework;

namespace MusePico.Tests
{
    /// <summary>Saul, 4 Oct 2026: every option is heard before the choice unlocks.</summary>
    public class ChoicePreviewTests
    {
        [Test]
        public void TheChoiceUnlocksOnlyWhenEveryOptionIsHeard()
        {
            var o = new OptionsHeard(new[] { ("The crane", "The crane"), ("The turtle", "The turtle") });
            Assert.IsFalse(o.AllHeard);
            Assert.IsFalse(o.MarkHeard("The crane"), "one of two is not enough");
            Assert.IsFalse(o.MarkHeard("The crane"), "hearing the same one twice is still one");
            Assert.IsFalse(o.MarkHeard("The lamp"), "something that is not an option does not count");
            Assert.IsFalse(o.AllHeard);
            Assert.IsTrue(o.MarkHeard("The turtle"), "the second completes it");
            Assert.IsTrue(o.AllHeard);
            Assert.IsFalse(o.MarkHeard("The turtle"), "unlocking happens once");
        }

        [Test]
        public void TheChecklistTicksWhatHasBeenHeard()
        {
            var o = new OptionsHeard(new[] { ("a", "The crane"), ("b", "The turtle") });
            o.MarkHeard("b");
            var lines = o.Checklist("point at it").ToList();
            Assert.AreEqual("○  The crane  ·  point at it", lines[0]);
            Assert.AreEqual("✓  The turtle  ·  heard", lines[1]);
        }
    }
}
