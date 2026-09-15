using System.Threading;
using System.Threading.Tasks;

namespace MusePico.Tripo
{
    /// <summary>One HTTP exchange, reduced to the parts Tripo actually uses.</summary>
    public struct TripoHttpRequest
    {
        public string Method;
        /// <summary>Absolute URL. The client has already joined the base URL and the path.</summary>
        public string Url;
        public string ContentType;
        public byte[] Body;
        /// <summary>Set for the one multipart endpoint (<c>POST /v3/files</c>); null otherwise.</summary>
        public string MultipartFileName;
    }

    public struct TripoHttpResponse
    {
        public int Status;
        public string Body;
        public byte[] Bytes;
        /// <summary>Set when the request never produced a response at all (DNS, TLS, timeout).</summary>
        public string TransportError;
    }

    /// <summary>
    /// The seam between Tripo's protocol and the network.
    ///
    /// It exists so the whole client — URL building, request bodies, envelope parsing, polling,
    /// error mapping — can be exercised in EditMode with a scripted fake, no key and no network.
    /// The implementation is also the only place the API key is ever read, so no other type in
    /// this assembly can log it by accident.
    /// </summary>
    public interface ITripoTransport
    {
        /// <summary>True when a key is available. Never exposes the key itself.</summary>
        bool HasCredentials { get; }

        Task<TripoHttpResponse> SendAsync(TripoHttpRequest request, CancellationToken cancellationToken);
    }
}
