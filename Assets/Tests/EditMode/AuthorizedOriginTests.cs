using MusePico.Tripo;
using NUnit.Framework;

namespace MusePico.Tests
{
    /// <summary>
    /// Which requests carry the API key.
    ///
    /// This exists because the bug it prevents was shipped. The transport authorised only URLs
    /// starting with "https://openapi.tripo3d.", was then reused for OpenAI and MiniMax, and both
    /// answered 401 — with no Authorization header sent at all. A 401 reads as "bad key", so the
    /// symptom pointed at the wrong thing entirely.
    ///
    /// Both directions are silent failures: too narrow is a 401 that blames the key, too wide
    /// hands a provider key to a pre-signed CDN that never needed it.
    /// </summary>
    public class AuthorizedOriginTests
    {
        [Test]
        public void EachProviderAuthorisesItsOwnEndpoint()
        {
            Assert.IsTrue(AuthorizedOrigin.ShouldAuthorize(
                "https://api.openai.com/v1/responses", DialogueClientEndpoint));
            Assert.IsTrue(AuthorizedOrigin.ShouldAuthorize(
                "https://api.minimax.io/v1/t2a_v2", "https://api.minimax.io/v1/t2a_v2"));
            Assert.IsTrue(AuthorizedOrigin.ShouldAuthorize(
                "https://openapi.tripo3d.ai/v3/generation/text-to-model", TripoApi.GlobalBaseUrl));
        }

        const string DialogueClientEndpoint = "https://api.openai.com/v1/responses";

        [Test]
        public void OneProvidersKeyNeverGoesToAnother()
        {
            // The actual defect: an OpenAI key in a transport configured for Tripo sent nothing.
            Assert.IsFalse(AuthorizedOrigin.ShouldAuthorize(
                "https://api.openai.com/v1/responses", TripoApi.GlobalBaseUrl));
            Assert.IsFalse(AuthorizedOrigin.ShouldAuthorize(
                "https://api.minimax.io/v1/t2a_v2", TripoApi.GlobalBaseUrl));
        }

        [Test]
        public void APreSignedCdnOnTheSameDomainIsNotAuthorised()
        {
            // Origin, not prefix. cdn.tripo3d.ai must not inherit authorisation from
            // openapi.tripo3d.ai just by sharing a domain — the download is already signed.
            Assert.IsFalse(AuthorizedOrigin.ShouldAuthorize(
                "https://cdn.tripo3d.ai/output/model_pbr.glb", TripoApi.GlobalBaseUrl));
        }

        [Test]
        public void TheTwoTripoRegionsAreDistinct()
        {
            // Accounts are region-bound, so a key for one must not be sent to the other.
            Assert.IsFalse(AuthorizedOrigin.ShouldAuthorize(
                TripoApi.ChinaBaseUrl + "/tasks/x", TripoApi.GlobalBaseUrl));
            Assert.IsTrue(AuthorizedOrigin.ShouldAuthorize(
                TripoApi.ChinaBaseUrl + "/tasks/x", TripoApi.ChinaBaseUrl));
        }

        [Test]
        public void SchemeMatters()
        {
            Assert.IsFalse(AuthorizedOrigin.ShouldAuthorize(
                "http://api.openai.com/v1/responses", "https://api.openai.com/v1/responses"),
                "a key must never be sent over plain http");
        }

        [Test]
        public void AnythingUnparseableIsRefused()
        {
            Assert.IsFalse(AuthorizedOrigin.ShouldAuthorize("not a url", TripoApi.GlobalBaseUrl));
            Assert.IsFalse(AuthorizedOrigin.ShouldAuthorize(null, TripoApi.GlobalBaseUrl));
            Assert.IsFalse(AuthorizedOrigin.ShouldAuthorize("/relative/path", TripoApi.GlobalBaseUrl));
            Assert.IsFalse(AuthorizedOrigin.ShouldAuthorize(TripoApi.GlobalBaseUrl, null));
        }

        [Test]
        public void HostComparisonIsCaseInsensitive()
        {
            Assert.IsTrue(AuthorizedOrigin.ShouldAuthorize(
                "https://API.OpenAI.com/v1/responses", "https://api.openai.com/v1/responses"));
        }
    }
}
