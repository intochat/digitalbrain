using System.Net;
using System.Net.Http.Headers;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Integrations.Mcp;

namespace DigitalBrain.Integrations.Gmail;

internal sealed class GmailBearerHandler(GmailConnections connections, OwnerId owner, GmailIdentity identity)
    : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
{
    private ResponseBudget _budget = new();
    internal void BeginOperation(ResponseBudget budget) => _budget = budget;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        McpIntegrationEndpoint.ValidateGmailUri(request.RequestUri!);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            await connections.AccessTokenAsync(owner, identity, false, cancellationToken).ConfigureAwait(false));
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if ((int)response.StatusCode is >= 300 and < 400)
        { response.Dispose(); throw new GmailOperationException("Gmail MCP redirected a request; redirects are forbidden."); }
        response.Content = new LimitedContent(response.Content, _budget);
        return response;
    }
    internal sealed class ResponseBudget
    {
        private long _bytes;
        internal void Add(int bytes)
        {
            if (Interlocked.Add(ref _bytes, bytes) > 1048576)
            {
                throw new GmailOperationException("Gmail MCP exceeded the 1 MiB response limit. Narrow the request.");
            }
        }
    }
    private sealed class LimitedContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly ResponseBudget _budget;
        internal LimitedContent(HttpContent inner, ResponseBudget budget)
        {
            _inner = inner; _budget = budget;
            foreach (var h in inner.Headers)
            {
                Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            using var source = await CreateContentReadStreamAsync().ConfigureAwait(false);
            await source.CopyToAsync(stream).ConfigureAwait(false);
        }
        protected override async Task<Stream> CreateContentReadStreamAsync()
            => new LimitedStream(await _inner.ReadAsStreamAsync().ConfigureAwait(false), _budget);
        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => new LimitedStream(await _inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), _budget);
        protected override void Dispose(bool disposing) { if (disposing) { _inner.Dispose(); } base.Dispose(disposing); }
    }
    private sealed class LimitedStream(Stream inner, ResponseBudget budget) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { var n = inner.Read(buffer, offset, count); budget.Add(n); return n; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { var n = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); budget.Add(n); return n; }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { var n = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false); budget.Add(n); return n; }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); } base.Dispose(disposing); }
    }
}
