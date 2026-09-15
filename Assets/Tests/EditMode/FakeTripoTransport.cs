using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MusePico.Tripo;

namespace MusePico.Tests
{
    /// <summary>
    /// A scripted stand-in for the network.
    ///
    /// It records every request verbatim and replays queued responses in order, which is what
    /// lets the whole Tripo client be tested with no API key, no credits and no internet — the
    /// reason <see cref="ITripoTransport"/> exists at all.
    /// </summary>
    public sealed class FakeTripoTransport : ITripoTransport
    {
        readonly Queue<TripoHttpResponse> _responses = new Queue<TripoHttpResponse>();

        public readonly List<TripoHttpRequest> Sent = new List<TripoHttpRequest>();
        public bool HasCredentials { get; set; } = true;

        public TripoHttpRequest LastRequest => Sent[Sent.Count - 1];

        public string LastBodyText =>
            LastRequest.Body == null ? null : Encoding.UTF8.GetString(LastRequest.Body);

        public FakeTripoTransport Enqueue(int status, string body)
        {
            _responses.Enqueue(new TripoHttpResponse
            {
                Status = status,
                Body = body,
                Bytes = body == null ? null : Encoding.UTF8.GetBytes(body),
            });
            return this;
        }

        public FakeTripoTransport EnqueueOk(string body) => Enqueue(200, body);

        public FakeTripoTransport EnqueueBytes(int status, byte[] bytes)
        {
            _responses.Enqueue(new TripoHttpResponse { Status = status, Bytes = bytes });
            return this;
        }

        public FakeTripoTransport EnqueueTransportError(string error)
        {
            _responses.Enqueue(new TripoHttpResponse { TransportError = error });
            return this;
        }

        public Task<TripoHttpResponse> SendAsync(TripoHttpRequest request, CancellationToken cancellationToken)
        {
            Sent.Add(request);
            if (_responses.Count == 0)
                return Task.FromResult(new TripoHttpResponse { TransportError = "FakeTripoTransport ran out of scripted responses." });
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
