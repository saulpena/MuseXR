using System.Linq;
using System.Text;
using NUnit.Framework;
using MusePico.Dialogue;

namespace MusePico.Tests
{
    /// <summary>
    /// The closing roundtable, against the scripted transport — no key, no network, no credits.
    ///
    /// These pin muse-infinity's contract (<c>server.mjs:619-764</c>): the response shape, the
    /// validation rules, and above all the honesty rule — a failure must surface as a failure and
    /// never as invented prose.
    /// </summary>
    public class RoundtableClientTests
    {
        static MasterRosterData Roster()
        {
            return new MasterRosterData
            {
                effects = new[] { "mist", "fracture", "garden", "network" },
                masters = new[]
                {
                    new MasterLens { id = "monet", name = "MONET", fullName = "Claude Monet",
                        lens = "light", attention = new[] { "haze" }, questionStyle = "stage a change",
                        vocabulary = new[] { "vapour" }, forbidden = new[] { "fracture" },
                        systemPrompt = "STAGE A CHANGE IN TIME" },
                    new MasterLens { id = "van_gogh", name = "VAN GOGH", fullName = "Vincent van Gogh",
                        lens = "urgency", attention = new[] { "impasto" }, questionStyle = "address directly",
                        vocabulary = new[] { "writhing" }, forbidden = new[] { "serene" },
                        systemPrompt = "ADDRESS THE VIEWER DIRECTLY" },
                    new MasterLens { id = "socrates", name = "SOCRATES", fullName = "Socrates",
                        lens = "inquiry", attention = new[] { "premises" }, questionStyle = "only questions",
                        vocabulary = new[] { "hypothesis" }, forbidden = new[] { "obviously" },
                        systemPrompt = "WRITE ONLY QUESTIONS" },
                }
            };
        }

        static System.Collections.Generic.List<MasterLens> Masters() => Roster().masters.ToList();

        static string Envelope(string payloadJson)
        {
            // The Responses API wraps output_text; ExtractOutputText walks to it.
            var escaped = payloadJson.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "{\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":\"" + escaped + "\"}]}]}";
        }

        const string GoodPayload =
            "{\"worldTitle\":\"The World Between Two Silences\"," +
            "\"synthesis\":\"You stopped at Water Lilies and asked what to keep.\"," +
            "\"threads\":[" +
            "{\"speakerId\":\"monet\",\"speaker\":\"Claude Monet\",\"text\":\"An hour later the light would have moved.\"}," +
            "{\"speakerId\":\"van_gogh\",\"speaker\":\"Vincent van Gogh\",\"text\":\"You carried the question further than you think.\"}," +
            "{\"speakerId\":\"socrates\",\"speaker\":\"Socrates\",\"text\":\"What did you assume before you entered?\"}]}";

        [Test]
        public async System.Threading.Tasks.Task AGoodClosingParsesIntoThreeThreads()
        {
            var fake = new FakeTripoTransport();
            fake.EnqueueOk(Envelope(GoodPayload));

            var result = await new RoundtableClient(fake, Roster()).AskAsync(new VisitSession(), Masters());

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual("The World Between Two Silences", result.worldTitle);
            Assert.AreEqual(3, result.threads.Count);
            Assert.AreEqual("Claude Monet", result.threads[0].speaker);
            Assert.IsTrue(result.Live);
        }

        [Test]
        public async System.Threading.Tasks.Task ItIsONECall_NotAFanOutOfThree()
        {
            var fake = new FakeTripoTransport();
            fake.EnqueueOk(Envelope(GoodPayload));

            await new RoundtableClient(fake, Roster()).AskAsync(new VisitSession(), Masters());

            Assert.AreEqual(1, fake.Sent.Count,
                "the threads synthesise one trajectory and need the same view of it");
        }

        [Test]
        public async System.Threading.Tasks.Task TheRequestCarriesTheDigestAndTheFramingExactlyOnce()
        {
            var session = new VisitSession();
            session.RecordArtwork("Water Lilies", "Claude Monet");
            session.RecordQuestion("What should I keep?");

            var fake = new FakeTripoTransport();
            fake.EnqueueOk(Envelope(GoodPayload));
            await new RoundtableClient(fake, Roster()).AskAsync(session, Masters());

            var body = fake.LastBodyText;
            StringAssert.Contains("Water Lilies", body);
            StringAssert.Contains("What should I keep?", body);
            StringAssert.Contains("museum_roundtable", body);

            var framing = PerspectivePrompt.InterpretiveFraming;
            var first = body.IndexOf(framing.Substring(0, 40), System.StringComparison.Ordinal);
            Assert.Greater(first, -1, "the interpretive framing must be carried by the call site");
            var second = body.IndexOf(framing.Substring(0, 40), first + 1, System.StringComparison.Ordinal);
            Assert.AreEqual(-1, second, "and it must appear exactly once, never per master");
        }

        [Test]
        public async System.Threading.Tasks.Task AnEmptyWalkSaysSoRatherThanInventingAStop()
        {
            var fake = new FakeTripoTransport();
            fake.EnqueueOk(Envelope(GoodPayload));
            await new RoundtableClient(fake, Roster()).AskAsync(new VisitSession(), Masters());

            StringAssert.Contains("(none: the visitor stopped at no artwork)", fake.LastBodyText);
            StringAssert.Contains("(none: the visitor asked nothing aloud)", fake.LastBodyText);
        }

        [Test]
        public async System.Threading.Tasks.Task AFailureIsReportedHonestly_NeverAsCannedProse()
        {
            var fake = new FakeTripoTransport();
            fake.Enqueue(500, "upstream exploded");
            fake.Enqueue(500, "upstream exploded");   // retry also fails

            var result = await new RoundtableClient(fake, Roster()).AskAsync(new VisitSession(), Masters());

            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Error);
            Assert.IsEmpty(result.threads, "no closing is better than an invented one");
            Assert.IsTrue(string.IsNullOrEmpty(result.worldTitle));
            Assert.IsFalse(result.Live);
        }

        [Test]
        public async System.Threading.Tasks.Task ASchemaBreachRetriesOnceThenSucceeds()
        {
            var fake = new FakeTripoTransport();
            fake.EnqueueOk(Envelope("{\"worldTitle\":\"\",\"synthesis\":\"x\",\"threads\":[]}"));
            fake.EnqueueOk(Envelope(GoodPayload));

            var result = await new RoundtableClient(fake, Roster()).AskAsync(new VisitSession(), Masters());

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(2, fake.Sent.Count, "a malformed closing is worth one more attempt");
        }

        [Test]
        public async System.Threading.Tasks.Task APlain4xxIsNotRetried()
        {
            var fake = new FakeTripoTransport();
            fake.Enqueue(400, "bad request");

            var result = await new RoundtableClient(fake, Roster()).AskAsync(new VisitSession(), Masters());

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, fake.Sent.Count,
                "a malformed request is malformed the second time too, and retrying doubles the bill");
        }

        [Test]
        public void ValidationRejectsTheThingsHerServerRejects()
        {
            Assert.IsNull(RoundtableClient.DescribeInvalid(GoodPayload, 3));

            StringAssert.Contains("worldTitle", RoundtableClient.DescribeInvalid(
                GoodPayload.Replace("The World Between Two Silences", ""), 3));

            StringAssert.Contains("threads", RoundtableClient.DescribeInvalid(
                "{\"worldTitle\":\"t\",\"synthesis\":\"s\",\"threads\":[]}", 3));

            var duplicate = GoodPayload.Replace("\"speakerId\":\"van_gogh\"", "\"speakerId\":\"monet\"");
            StringAssert.Contains("share the speakerId", RoundtableClient.DescribeInvalid(duplicate, 3));
        }

        [Test]
        public async System.Threading.Tasks.Task AnUnknownSpeakerIdFallsBackToThePositionAsked()
        {
            var payload = GoodPayload.Replace("\"speakerId\":\"socrates\"", "\"speakerId\":\"nobody\"");
            var fake = new FakeTripoTransport();
            fake.EnqueueOk(Envelope(payload));

            var result = await new RoundtableClient(fake, Roster()).AskAsync(new VisitSession(), Masters());

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual("socrates", result.threads[2].speakerId,
                "a copied id is not a trusted id; re-anchor onto who was actually asked");
        }
    }

    /// <summary>The shared Responses machinery, independent of either caller.</summary>
    public class ResponsesCallTests
    {
        [Test]
        public void RetryablesAreTheTransientOnesOnly()
        {
            Assert.IsTrue(ResponsesCall.IsRetryableStatus(408));
            Assert.IsTrue(ResponsesCall.IsRetryableStatus(429));
            Assert.IsTrue(ResponsesCall.IsRetryableStatus(503));
            Assert.IsFalse(ResponsesCall.IsRetryableStatus(400));
            Assert.IsFalse(ResponsesCall.IsRetryableStatus(401));
            Assert.IsFalse(ResponsesCall.IsRetryableStatus(404));
        }

        [Test]
        public void TheModelDefaultsToLuna_NotTheSolAlias()
        {
            var call = new ResponsesCall(new FakeTripoTransport());
            Assert.AreEqual("gpt-5.6-luna", call.Model,
                "gpt-5.6 is the Sol alias and measured 13-14s per turn");
            Assert.AreEqual("https://api.openai.com/v1/responses", ResponsesCall.DefaultEndpoint);
        }

        [Test]
        public void TheTextFormatIsStrictJsonSchema()
        {
            var schema = new MusePico.Tripo.JsonBuilder().Add("type", "object");
            var json = ResponsesCall.TextFormat("my_schema", schema);
            StringAssert.Contains("\"type\":\"json_schema\"", json);
            StringAssert.Contains("\"name\":\"my_schema\"", json);
            StringAssert.Contains("\"strict\":true", json);
        }
    }
}
