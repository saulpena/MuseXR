using System;
using MusePico.Tripo;
using NUnit.Framework;

namespace MusePico.Tests
{
    /// <summary>
    /// Every Tripo generation call costs credits, so the body is asserted byte for byte rather
    /// than "contains prompt". Two specific failures are what these lock out:
    ///
    ///   - An unset flag serialised as false. Unity's JsonUtility would send "texture": false for
    ///     a field nobody touched, producing an untextured mesh that still bills.
    ///   - A malformed request reaching the API at all. Validation throws locally, before a
    ///     request is sent, so a mistake costs nothing.
    /// </summary>
    public class TripoRequestTests
    {
        [Test]
        public void TextToModel_SendsOnlyWhatWasSet()
        {
            var request = new TextToModelRequest { Prompt = "a marble bust of a philosopher" };

            Assert.AreEqual(
                "{\"prompt\":\"a marble bust of a philosopher\",\"model\":\"v3.1-20260211\"}",
                request.ToJson());
        }

        [Test]
        public void UnsetBooleansAreOmitted_NotSentAsFalse()
        {
            var json = new TextToModelRequest { Prompt = "x" }.ToJson();

            StringAssert.DoesNotContain("texture", json);
            StringAssert.DoesNotContain("pbr", json);
            StringAssert.DoesNotContain("quad", json);
        }

        [Test]
        public void ExplicitFalseIsSent()
        {
            var json = new TextToModelRequest { Prompt = "x", Texture = false }.ToJson();

            StringAssert.Contains("\"texture\":false", json);
        }

        [Test]
        public void TextToModel_FullOptionSetIsOrderedAndComplete()
        {
            var request = new TextToModelRequest
            {
                Prompt = "an impossible museum",
                NegativePrompt = "blurry",
                Model = TripoApi.Models.P1,
                Texture = true,
                Pbr = true,
                TextureQuality = TripoApi.TextureQuality.Detailed,
                GeometryQuality = TripoApi.GeometryQuality.Standard,
                FaceLimit = 30000,
                ModelSeed = 7,
                AutoSize = true,
                SmartLowPoly = true,
            };

            Assert.AreEqual(
                "{\"prompt\":\"an impossible museum\"," +
                "\"model\":\"P1-20260311\"," +
                "\"texture\":true," +
                "\"pbr\":true," +
                "\"texture_quality\":\"detailed\"," +
                "\"geometry_quality\":\"standard\"," +
                "\"face_limit\":30000," +
                "\"model_seed\":7," +
                "\"auto_size\":true," +
                "\"smart_low_poly\":true," +
                "\"negative_prompt\":\"blurry\"}",
                request.ToJson());
        }

        [Test]
        public void PromptsWithQuotesAndNewlinesAreEscaped()
        {
            var json = new TextToModelRequest { Prompt = "a \"bust\"\nof Socrates\\Plato" }.ToJson();

            StringAssert.Contains("\\\"bust\\\"", json);
            StringAssert.Contains("\\n", json);
            StringAssert.Contains("\\\\", json);
        }

        [Test]
        public void EmptyPromptThrowsBeforeAnythingIsSent()
        {
            Assert.Throws<ArgumentException>(() => new TextToModelRequest { Prompt = "  " }.ToJson());
        }

        [Test]
        public void ImageToModel_WrapsTheFileAsAnObject_NotABareString()
        {
            var request = new ImageToModelRequest
            {
                File = TripoFileRef.FromToken("tok_123"),
                TextureAlignment = TripoApi.TextureAlignment.OriginalImage,
            };

            Assert.AreEqual(
                "{\"file\":{\"file_token\":\"tok_123\"},\"model\":\"v3.1-20260211\"," +
                "\"texture_alignment\":\"original_image\"}",
                request.ToJson());
        }

        [Test]
        public void ImageToModel_UrlAndTokenAreMutuallyExclusiveByConstruction()
        {
            var fromUrl = TripoFileRef.Parse("https://example.com/front.png");
            var fromToken = TripoFileRef.Parse("abc123");

            Assert.AreEqual("https://example.com/front.png", fromUrl.Url);
            Assert.IsNull(fromUrl.FileToken);
            Assert.AreEqual("abc123", fromToken.FileToken);
            Assert.IsNull(fromToken.Url);
        }

        [Test]
        public void ImageToModel_WithoutAFileThrows()
        {
            Assert.Throws<ArgumentException>(() => new ImageToModelRequest().ToJson());
        }

        [Test]
        public void Multiview_SendsFourSlotsInFrontLeftBackRightOrder()
        {
            var request = new MultiviewToModelRequest();
            request.Files[MultiviewToModelRequest.Front] = TripoFileRef.FromToken("f");
            request.Files[MultiviewToModelRequest.Left] = TripoFileRef.FromToken("l");
            request.Files[MultiviewToModelRequest.Back] = TripoFileRef.FromToken("b");
            request.Files[MultiviewToModelRequest.Right] = TripoFileRef.FromToken("r");

            StringAssert.Contains(
                "\"files\":[{\"file_token\":\"f\"},{\"file_token\":\"l\"},{\"file_token\":\"b\"},{\"file_token\":\"r\"}]",
                request.ToJson());
        }

        [Test]
        public void Multiview_SkippedViewsBecomeEmptyObjects_KeepingPositions()
        {
            // Only front and back. The empty slots must still occupy positions 1 and 3, or the
            // back view would be read as the left one.
            var request = new MultiviewToModelRequest();
            request.Files[MultiviewToModelRequest.Front] = TripoFileRef.FromToken("f");
            request.Files[MultiviewToModelRequest.Back] = TripoFileRef.FromToken("b");

            StringAssert.Contains(
                "\"files\":[{\"file_token\":\"f\"},{},{\"file_token\":\"b\"},{}]",
                request.ToJson());
        }

        [Test]
        public void Multiview_WithoutAFrontViewThrows()
        {
            var request = new MultiviewToModelRequest();
            request.Files[MultiviewToModelRequest.Left] = TripoFileRef.FromToken("l");

            Assert.Throws<ArgumentException>(() => request.ToJson());
        }

        [Test]
        public void Multiview_WithNothingAtAllThrows()
        {
            Assert.Throws<ArgumentException>(() => new MultiviewToModelRequest().ToJson());
        }

        [Test]
        public void Rig_DefaultsToMixamoNaming_WhichUnityCanMapToAHumanoid()
        {
            var json = new RigRequest { Input = "task_1" }.ToJson();

            StringAssert.Contains("\"spec\":\"mixamo\"", json);
            StringAssert.Contains("\"rig_type\":\"biped\"", json);
            StringAssert.Contains("\"out_format\":\"glb\"", json);
        }

        [Test]
        public void Retarget_RefusesMoreThanFiveAnimations()
        {
            var request = new RetargetRequest { Input = "task_1" };
            request.Animations.AddRange(new[]
            {
                TripoApi.Animations.Idle, TripoApi.Animations.Walk, TripoApi.Animations.Run,
                TripoApi.Animations.Jump, TripoApi.Animations.Turn, TripoApi.Animations.Fall,
            });

            Assert.Throws<ArgumentException>(() => request.ToJson());
        }

        [Test]
        public void Retarget_EmitsAStringArray()
        {
            var request = new RetargetRequest { Input = "task_1" };
            request.Animations.Add(TripoApi.Animations.Idle);
            request.Animations.Add(TripoApi.Animations.Walk);

            StringAssert.Contains("\"animations\":[\"preset:idle\",\"preset:walk\"]", request.ToJson());
        }

        [Test]
        public void CreditEstimate_DropsToTheCheaperTierWhenTextureIsOff()
        {
            Assert.AreEqual(20, new TextToModelRequest { Prompt = "x" }.EstimatedCredits());
            Assert.AreEqual(10, new TextToModelRequest { Prompt = "x", Texture = false }.EstimatedCredits());
            Assert.AreEqual(30, new ImageToModelRequest { File = TripoFileRef.FromToken("t") }.EstimatedCredits());
        }
    }
}
