using System;

namespace MusePico.Tripo
{
    /// <summary>
    /// Decides whether a request may carry the API key.
    ///
    /// <b>This exists because getting it wrong is silent in both directions.</b> Send the header
    /// too narrowly and the service answers 401 with no clue that the header was simply absent —
    /// which is exactly what happened: the transport authorised only <c>openapi.tripo3d.</c>, was
    /// then reused for OpenAI and MiniMax, and both came back 401 while looking like a bad key.
    /// Send it too widely and a provider key goes to a pre-signed CDN URL that never needed it.
    ///
    /// So the rule is explicit and per-transport: <b>the key goes to the service's own origin and
    /// nowhere else.</b> Origin, not prefix — <c>cdn.tripo3d.ai</c> must not inherit authorisation
    /// from <c>openapi.tripo3d.ai</c> merely by sharing a domain.
    ///
    /// Pure, so the rule is pinned by tests rather than discovered as a 401 on a headset.
    /// </summary>
    public static class AuthorizedOrigin
    {
        /// <summary>
        /// True when <paramref name="requestUrl"/> is on the same scheme and host as
        /// <paramref name="serviceUrl"/>. Anything unparseable is refused: no origin, no key.
        /// </summary>
        public static bool ShouldAuthorize(string requestUrl, string serviceUrl)
        {
            var request = OriginOf(requestUrl);
            var service = OriginOf(serviceUrl);
            return request != null && service != null &&
                   string.Equals(request, service, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"scheme://host" for a well-formed absolute URL, or null.</summary>
        public static string OriginOf(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
            if (string.IsNullOrEmpty(uri.Host)) return null;
            return uri.Scheme + "://" + uri.Host;
        }
    }
}
