using MusePico.Dialogue;
using MuseXR.Slots;
using NUnit.Framework;

namespace MusePico.Tests
{
    /// <summary>Her reply to a work (artworkChoices, AXIS_CHAMPIONS, companionVoices reactions).</summary>
    public class InsightRepliesTests
    {
        [Test]
        public void HerThreeRepliesInHerOrder()
        {
            Assert.AreEqual(3, Insights.Replies.Count);
            Assert.AreEqual("perception", Insights.Replies[0].axis);
            Assert.AreEqual("It makes me feel something I don't have words for yet.", Insights.Replies[1].label);
            Assert.AreEqual("invention", Insights.Replies[2].axis);
        }

        [Test]
        public void TheChampionAnswersAndNeverTheOneWhoOpened()
        {
            var trio = Masters.DefaultTrio;   // Monet, Van Gogh, Socrates
            Assert.AreEqual(Masters.VanGogh, Insights.ReactionSpeaker("emotion", trio, Masters.Monet));
            Assert.AreEqual(Masters.Socrates, Insights.ReactionSpeaker("invention", trio, Masters.Monet), "Picasso and Hilma are not in the company");
            Assert.AreNotEqual(Masters.Monet, Insights.ReactionSpeaker("perception", trio, Masters.Monet), "Monet opened: someone else answers");
        }

        [Test]
        public void HerReactionsVerbatim()
        {
            Assert.AreEqual("Good. Do not tame it. A feeling with no name is the most honest visitor you will ever receive.",
                            Insights.Reaction(Masters.VanGogh, "emotion"));
            Assert.AreEqual("Now you understand. Art is the lie that tells the truth, and the lie must be built with total conviction.",
                            Insights.Reaction(Masters.Picasso, "invention"));
            Assert.AreEqual("Then let it keep changing what you notice.", Insights.Reaction("nobody", "perception"));
        }

        [Test]
        public void RepliesTallyHerAxes()
        {
            var r = new JourneyRecord();
            r.AddReply("aic-28560", "emotion"); r.AddReply("aic-16568", "emotion"); r.AddReply("aic-80607", "perception");
            Assert.AreEqual(2, r.Axis("emotion"));
            Assert.AreEqual(1, r.Axis("perception"));
            Assert.AreEqual(0, r.Axis("invention"));
            r.Reset();
            Assert.AreEqual(0, r.Replies.Count);
        }
    }
}
