using System.Net;

namespace Brain.KernelTests;

public sealed class StubHttpHandler : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public string Body { get; set; } = "";
    public Exception? Throws { get; set; }
    public Exception? StreamThrows { get; set; }

    public void Reset()
    {
        StatusCode = HttpStatusCode.OK;
        Body = "";
        Throws = null;
        StreamThrows = null;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (Throws is { } exception)
            throw exception;

        var content = StreamThrows is { } streamException
            ? new ThrowingContent(streamException)
            : (HttpContent)new StringContent(Body);

        return Task.FromResult(new HttpResponseMessage(StatusCode) { Content = content });
    }
}

internal sealed class ThrowingContent(Exception exception) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var tcs = new TaskCompletionSource();
        tcs.SetException(exception);
        return tcs.Task;
    }

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }

    protected override Task<Stream> CreateContentReadStreamAsync()
    {
        var tcs = new TaskCompletionSource<Stream>();
        tcs.SetException(exception);
        return tcs.Task;
    }
}
