using System.Text;
using MusePico.Tripo;
using NUnit.Framework;

namespace MusePico.Tests
{
    /// <summary>
    /// The client's job is protocol, not network: build the right URL, read the
    /// { code, data, message, suggestion } envelope, and turn every kind of failure into
    /// something a human can act on. All of that is checked here against a scripted transport,
    /// so it is verified before an API key exists rather than after.
    ///
    /// The envelope is checked twice over because the two layers fail independently and mean
    /// different things: HTTP 200 with code != 0 is Tripo rejecting the request (bad parameter,
    /// no credit), while HTTP 401 with no JSON is the key being wrong. Collapsing them into one
    /// "request failed" loses the only useful diagnosis.
    /// </summary>
    public class TripoClientTests
    {
        static TripoClient Client(FakeTripoTransport transport) =>
            new TripoClient(transport, TripoApi.GlobalBaseUrl);

        [Test]
        public void CreateTask_PostsToTheV3GenerationPath()
        {
            var transport = new FakeTripoTransport().EnqueueOk("{\"code\":0,\"data\":{\"task_id\":\"task_abc\"}}");

            var taskId = Client(transport)
                .TextToModelAsync(new TextToModelRequest { Prompt = "a bust" })
                .GetAwaiter().GetResult();

            Assert.AreEqual("task_abc", taskId);
            Assert.AreEqual("POST", transport.LastRequest.Method);
            Assert.AreEqual("https://openapi.tripo3d.ai/v3/generation/text-to-model", transport.LastRequest.Url);
            Assert.AreEqual("application/json", transport.LastRequest.ContentType);
            StringAssert.Contains("\"prompt\":\"a bust\"", transport.LastBodyText);
        }

        [Test]
        public void GetTask_GetsTheV3TaskPath_AndParsesTheOutput()
        {
            var transport = new FakeTripoTransport().EnqueueOk(
                "{\"code\":0,\"data\":{\"task_id\":\"task_abc\",\"type\":\"text_to_model\"," +
                "\"status\":\"success\",\"progress\":100," +
                "\"output\":{\"model_url\":\"https://cdn.tripo3d.ai/out/model_pbr.glb\"," +
                "\"rendered_image_url\":\"https://cdn.tripo3d.ai/out/preview.png\"}," +
                "\"credits_consumed\":20.0}}");

            var task = Client(transport).GetTaskAsync("task_abc").GetAwaiter().GetResult();

            Assert.AreEqual("GET", transport.LastRequest.Method);
            Assert.AreEqual("https://openapi.tripo3d.ai/v3/tasks/task_abc", transport.LastRequest.Url);
            Assert.IsNull(transport.LastRequest.Body, "a GET must not carry a body");
            Assert.AreEqual(TripoTaskStatus.Success, task.Status);
            Assert.IsTrue(task.IsTerminal);
            Assert.AreEqual("https://cdn.tripo3d.ai/out/model_pbr.glb", task.output.BestModelUrl);
            Assert.AreEqual(20f, task.credits_consumed, 1e-3f);
        }

        [Test]
        public void LegacyV2OutputFieldNamesStillResolveToAModelUrl()
        {
            // v2 returned pbr_model / model / base_model. A key that generated tasks before the
            // v3 cutover can still list them, so the older names stay readable.
            var output = UnityEngine.JsonUtility.FromJson<TripoTaskOutput>(
                "{\"pbr_model\":\"https://cdn/pbr.glb\",\"base_model\":\"https://cdn/base.glb\"}");

            Assert.AreEqual("https://cdn/pbr.glb", output.BestModelUrl);
        }

        [Test]
        public void EnvelopeCodeNonZeroBecomesAReadableError_EvenOnHttp200()
        {
            var transport = new FakeTripoTransport().EnqueueOk(
                "{\"code\":2002,\"message\":\"insufficient credits\",\"suggestion\":\"Top up your balance.\"}");

            var ex = Assert.Throws<TripoException>(() =>
                Client(transport).TextToModelAsync(new TextToModelRequest { Prompt = "x" })
                    .GetAwaiter().GetResult());

            Assert.AreEqual(2002, ex.Code);
            Assert.AreEqual("insufficient credits", ex.Message);
            StringAssert.Contains("Top up your balance.", ex.ToString());
        }

        [Test]
        public void Unauthorised_SaysToCheckTheKeyAndTheRegion()
        {
            var transport = new FakeTripoTransport().Enqueue(401, "Unauthorized");

            var ex = Assert.Throws<TripoException>(() =>
                Client(transport).GetBalanceAsync().GetAwaiter().GetResult());

            Assert.AreEqual(401, ex.HttpStatus);
            StringAssert.Contains("TRIPO_API_KEY", ex.ToString());
            StringAssert.Contains("region", ex.ToString());
        }

        [Test]
        public void TransportFailureIsReportedAsUnreachable_NotAsARejection()
        {
            var transport = new FakeTripoTransport().EnqueueTransportError("HttpRequestException: no such host");

            var ex = Assert.Throws<TripoException>(() =>
                Client(transport).GetBalanceAsync().GetAwaiter().GetResult());

            Assert.AreEqual(0, ex.HttpStatus);
            StringAssert.Contains("Could not reach Tripo", ex.Message);
        }

        [Test]
        public void AcceptedButWithoutATaskIdIsAnError_NotASilentEmptyString()
        {
            var transport = new FakeTripoTransport().EnqueueOk("{\"code\":0,\"data\":{}}");

            Assert.Throws<TripoException>(() =>
                Client(transport).TextToModelAsync(new TextToModelRequest { Prompt = "x" })
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void WaitForTask_PollsUntilTerminal_AndReportsProgress()
        {
            var transport = new FakeTripoTransport()
                .EnqueueOk("{\"code\":0,\"data\":{\"task_id\":\"t\",\"status\":\"queued\",\"queuing_num\":3}}")
                .EnqueueOk("{\"code\":0,\"data\":{\"task_id\":\"t\",\"status\":\"running\",\"progress\":40}}")
                .EnqueueOk("{\"code\":0,\"data\":{\"task_id\":\"t\",\"status\":\"success\",\"progress\":100," +
                           "\"output\":{\"model_url\":\"https://cdn/m.glb\"}}}");

            var seen = new System.Collections.Generic.List<string>();
            var progress = new System.Progress<TripoTask>(t => seen.Add(t.Describe()));

            var task = Client(transport)
                .WaitForTaskAsync("t", progress, pollSeconds: 0.01f, timeoutSeconds: 30f)
                .GetAwaiter().GetResult();

            Assert.AreEqual(TripoTaskStatus.Success, task.Status);
            Assert.AreEqual(3, transport.Sent.Count, "polling must stop at the first terminal status");
        }

        [Test]
        public void WaitForTask_ReturnsAFailedTaskRatherThanThrowing()
        {
            // A failed task carries the reason. Throwing here would discard error_msg, which is
            // the only thing that says whether the input or the parameters were at fault.
            var transport = new FakeTripoTransport().EnqueueOk(
                "{\"code\":0,\"data\":{\"task_id\":\"t\",\"status\":\"failed\"," +
                "\"error_code\":1004,\"error_msg\":\"input image unreadable\"}}");

            var task = Client(transport)
                .WaitForTaskAsync("t", pollSeconds: 0.01f, timeoutSeconds: 5f)
                .GetAwaiter().GetResult();

            Assert.AreEqual(TripoTaskStatus.Failed, task.Status);
            StringAssert.Contains("input image unreadable", task.Describe());
        }

        [Test]
        public void UploadFile_PostsMultipartToTheFilesEndpoint()
        {
            var transport = new FakeTripoTransport().EnqueueOk("{\"code\":0,\"data\":{\"file_token\":\"tok_9\"}}");

            var token = Client(transport)
                .UploadFileAsync(Encoding.UTF8.GetBytes("PNGDATA"), "front.png", "image/png")
                .GetAwaiter().GetResult();

            Assert.AreEqual("tok_9", token);
            Assert.AreEqual("https://openapi.tripo3d.ai/v3/files", transport.LastRequest.Url);
            Assert.AreEqual("front.png", transport.LastRequest.MultipartFileName);
            Assert.AreEqual("image/png", transport.LastRequest.ContentType);
        }

        [Test]
        public void Download_RejectsAnExpiredOutputUrlWithAUsefulMessage()
        {
            // Tripo's output URLs are pre-signed and expire; a 403 here means "too late", not
            // "bad key".
            var transport = new FakeTripoTransport().Enqueue(403, null);

            var ex = Assert.Throws<TripoException>(() =>
                Client(transport).DownloadAsync("https://cdn.tripo3d.ai/out/m.glb").GetAwaiter().GetResult());

            StringAssert.Contains("expired", ex.Message);
        }

        [Test]
        public void Download_ReturnsTheBytes()
        {
            var payload = new byte[] { 0x67, 0x6c, 0x54, 0x46 }; // "glTF"
            var transport = new FakeTripoTransport().EnqueueBytes(200, payload);

            var bytes = Client(transport).DownloadAsync("https://cdn.tripo3d.ai/out/m.glb")
                .GetAwaiter().GetResult();

            CollectionAssert.AreEqual(payload, bytes);
        }

        [Test]
        public void Balance_ReadsCreditsAndPrintsADollarEstimate()
        {
            var transport = new FakeTripoTransport().EnqueueOk("{\"code\":0,\"data\":{\"balance\":250.0,\"frozen\":30.0}}");

            var balance = Client(transport).GetBalanceAsync().GetAwaiter().GetResult();

            Assert.AreEqual(250f, balance.balance, 1e-3f);
            StringAssert.Contains("$2.50", balance.Describe());
        }

        [Test]
        public void ChinaBaseUrlIsHonoured()
        {
            var transport = new FakeTripoTransport().EnqueueOk("{\"code\":0,\"data\":{\"balance\":0}}");

            new TripoClient(transport, TripoApi.ChinaBaseUrl).GetBalanceAsync().GetAwaiter().GetResult();

            Assert.AreEqual("https://openapi.tripo3d.com/v3/account/balance", transport.LastRequest.Url);
        }

        [Test]
        public void StatusParsingCoversEverythingTheApiCanReturn()
        {
            Assert.AreEqual(TripoTaskStatus.Queued, TripoTaskStatusExtensions.Parse("queued"));
            Assert.AreEqual(TripoTaskStatus.Running, TripoTaskStatusExtensions.Parse("running"));
            Assert.AreEqual(TripoTaskStatus.Cancelled, TripoTaskStatusExtensions.Parse("canceled"));
            Assert.AreEqual(TripoTaskStatus.Banned, TripoTaskStatusExtensions.Parse("banned"));
            Assert.AreEqual(TripoTaskStatus.Expired, TripoTaskStatusExtensions.Parse("expired"));
            Assert.AreEqual(TripoTaskStatus.Unknown, TripoTaskStatusExtensions.Parse("something_new"));

            Assert.IsFalse(TripoTaskStatus.Queued.IsTerminal());
            Assert.IsFalse(TripoTaskStatus.Running.IsTerminal());
            Assert.IsTrue(TripoTaskStatus.Banned.IsTerminal());
            Assert.IsFalse(TripoTaskStatus.Unknown.IsTerminal(), "an unrecognised status must keep polling, not stop");
        }
    }
}
