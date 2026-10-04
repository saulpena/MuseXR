using MusePico.Dialogue;
using MuseXR.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>Her artwork card (4.1) and chapter question (acceptance: each chapter shows its question at entry).</summary>
    public class ArtworkCardTests
    {
        [Test]
        public void AiStudiesAreToldApartFromRealWorks()
        {
            Assert.IsTrue(ArtworkCard.IsAiStudy(new ArtworkRecord { date = "AI interpretive study" }));
            Assert.IsTrue(ArtworkCard.IsAiStudy(new ArtworkRecord { date = "", rights = "AI-generated interpretive image; not an authentic historical artwork." }));
            Assert.IsFalse(ArtworkCard.IsAiStudy(new ArtworkRecord { date = "1887", rights = "Public-domain artwork image via Art Institute of Chicago Open Access / IIIF" }));
        }

        [Test]
        public void TheCardStandsRightOfTheFrameOrBelowANarrowWork()
        {
            var wide = ArtworkCard.Offset(new Vector2(1.5f, 1.2f), 0.4f);
            Assert.Greater(wide.x, 0.75f + ArtworkCard.Beside - 1e-4f, "right of the frame, 0.3 m clear");
            Assert.AreEqual(0f, wide.y, 1e-5f);
            var narrow = ArtworkCard.Offset(new Vector2(0.8f, 1.0f), 0.4f);
            Assert.AreEqual(0f, narrow.x, 1e-5f);
            Assert.Less(narrow.y, -(0.5f + ArtworkCard.Beside) + 1e-4f, "under a work narrower than 1 m");
        }

        [Test]
        public void TheChapterQuestionReadsAsHerScriptWritesIt()
        {
            Assert.AreEqual("Stop 3  ·  What can my feeling become as expression?", ChapterQuestion.Line("Stop 3", "What can my feeling become as expression?"));
            Assert.AreEqual("What will you carry back into the life outside?", ChapterQuestion.Line("", "What will you carry back into the life outside?"));
        }
    }
}
