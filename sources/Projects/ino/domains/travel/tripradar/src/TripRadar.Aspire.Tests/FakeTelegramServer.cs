using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TripRadar.Aspire.Tests;

internal sealed record CapturedTelegramCall(string Method, JsonNode? Body);

internal sealed class FakeTelegramServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<CapturedTelegramCall> _calls = new();
    private readonly Channel<CapturedTelegramCall> _callChannel = Channel.CreateUnbounded<CapturedTelegramCall>();

    public string BaseUrl { get; private set; } = string.Empty;

    public IReadOnlyCollection<CapturedTelegramCall> Calls => _calls.ToArray();

    private FakeTelegramServer(WebApplication app) => _app = app;

    public static async Task<FakeTelegramServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        var server = new FakeTelegramServer(app);

        app.MapPost("/bot{token}/{method}", async (string method, HttpContext ctx) =>
        {
            JsonNode? body = null;
            try
            {
                body = await JsonNode.ParseAsync(ctx.Request.Body);
            }
            catch
            {
                body = null;
            }

            server.Record(new CapturedTelegramCall(method, body));

            return Results.Json(new
            {
                ok = true,
                result = new
                {
                    message_id = 1,
                    chat = new { id = body?["chat_id"]?.GetValue<long>() ?? 0L },
                    text = body?["text"]?.GetValue<string>() ?? string.Empty
                }
            });
        });

        app.MapGet("/bot{token}/{method}", (string method) =>
        {
            server.Record(new CapturedTelegramCall(method, null));
            return Results.Json(new { ok = true, result = true });
        });

        await app.StartAsync();
        server.BaseUrl = app.Urls.First();
        return server;
    }

    private void Record(CapturedTelegramCall call)
    {
        _calls.Enqueue(call);
        _callChannel.Writer.TryWrite(call);
    }

    public async Task<CapturedTelegramCall?> WaitForMethodAsync(string method, TimeSpan timeout, CancellationToken ct = default)
    {
        var match = _calls.FirstOrDefault(c => string.Equals(c.Method, method, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return match;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await foreach (var call in _callChannel.Reader.ReadAllAsync(timeoutCts.Token))
            {
                if (string.Equals(call.Method, method, StringComparison.OrdinalIgnoreCase))
                    return call;
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return null;
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        _callChannel.Writer.TryComplete();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
