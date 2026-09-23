using System.Net;
using System.Net.Sockets;

namespace IntoChat.Tests.E2E.Diagnostics;

/// <summary>
/// Receives OTLP/HTTP protobuf trace exports from the test host and its child processes so a
/// test can count real spans. It listens on a loopback port and keeps a thread-safe snapshot.
/// </summary>
internal sealed class TestTelemetryCollector : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _loop;
    private readonly List<CapturedSpan> _spans = [];
    private readonly List<CapturedLog> _logs = [];
    private readonly List<string> _errors = [];
    private readonly Lock _gate = new();

    private TestTelemetryCollector(HttpListener listener, Uri endpoint)
    {
        _listener = listener;
        Endpoint = endpoint;
        _loop = AcceptAsync();
    }

    public Uri Endpoint { get; }

    public static TestTelemetryCollector Start()
    {
        var port = FreePort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        return new(listener, new Uri($"http://127.0.0.1:{port}"));
    }

    public IReadOnlyList<CapturedSpan> Snapshot()
    {
        lock (_gate) { return _spans.ToArray(); }
    }

    public IReadOnlyList<CapturedLog> LogSnapshot()
    {
        lock (_gate) { return _logs.ToArray(); }
    }

    public IReadOnlyList<string> Errors()
    {
        lock (_gate) { return _errors.ToArray(); }
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (InvalidOperationException) { return; }
            _ = HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            using var buffer = new MemoryStream();
            await context.Request.InputStream.CopyToAsync(buffer, _stopping.Token).ConfigureAwait(false);
            // Logs and metrics share the endpoint; only trace and log exports are parsed.
            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/v1/traces", StringComparison.Ordinal))
            {
                var parsed = OtlpTraceParser.Parse(buffer.ToArray());
                lock (_gate) { _spans.AddRange(parsed); }
            }
            else if (path.EndsWith("/v1/logs", StringComparison.Ordinal))
            {
                var parsed = OtlpTraceParser.ParseLogs(buffer.ToArray());
                lock (_gate) { _logs.AddRange(parsed); }
            }
            context.Response.StatusCode = (int)HttpStatusCode.OK;
        }
        catch (Exception error) when (error is HttpListenerException or IOException or ObjectDisposedException
            or OperationCanceledException or ArgumentException)
        {
            lock (_gate) { _errors.Add(error.Message); }
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        _listener.Close();
        try { await _loop.ConfigureAwait(false); }
        catch (Exception error) when (error is HttpListenerException or ObjectDisposedException or OperationCanceledException) { }
        _stopping.Dispose();
    }
}