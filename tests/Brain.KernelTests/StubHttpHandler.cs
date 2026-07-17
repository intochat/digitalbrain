using System.Net;

namespace Brain.KernelTests;

public sealed class StubHttpHandler : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public string Body { get; set; } = "";
    public Exception? Throws { get; set; }

    public void Reset()
    {
        StatusCode = HttpStatusCode.OK;
        Body = "";
        Throws = null;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (Throws is { } exception)
            throw exception;

        return Task.FromResult(new HttpResponseMessage(StatusCode) { Content = new StringContent(Body) });
    }
}
