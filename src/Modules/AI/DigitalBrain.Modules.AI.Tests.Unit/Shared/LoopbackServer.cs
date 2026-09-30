using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DigitalBrain.Tests;

internal sealed record CapturedRequest(string Method, string? Path, string Body, string? Authorization);

internal sealed class LoopbackServer : IDisposable
{
    private readonly HttpListener listener = new();

    public LoopbackServer()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        Url = $"http://localhost:{port}/";
        listener.Prefixes.Add(Url);
        listener.Start();
    }

    public string Url { get; }

    public async Task<CapturedRequest> ReplyOnce(string mediaType, byte[] response, CancellationToken cancellationToken, HttpStatusCode status = HttpStatusCode.OK)
    {
        var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
        using var reader = new StreamReader(context.Request.InputStream);
        var captured = new CapturedRequest(context.Request.HttpMethod, context.Request.Url?.AbsolutePath,
            await reader.ReadToEndAsync(cancellationToken), context.Request.Headers["Authorization"]);
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = mediaType;
        context.Response.ContentLength64 = response.Length;
        await context.Response.OutputStream.WriteAsync(response, cancellationToken);
        context.Response.Close();
        return captured;
    }

    public Task<CapturedRequest> ReplyOnce(string mediaType, string response, CancellationToken cancellationToken, HttpStatusCode status = HttpStatusCode.OK)
        => ReplyOnce(mediaType, Encoding.UTF8.GetBytes(response), cancellationToken, status);

    public void Dispose() => listener.Close();
}
