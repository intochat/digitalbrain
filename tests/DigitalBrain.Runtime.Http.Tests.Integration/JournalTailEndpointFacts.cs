using System.Net;
using System.Security.Claims;
using System.Text.Json;
using DigitalBrain;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Runtime.Http.Surface;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Http.Tests.Integration;

public sealed class JournalTailEndpointFacts
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly NeuronId Publisher = NeuronId.Parse("b1/tail.counter/a");

    [Fact]
    public async Task TailWithoutASessionIs401AndAnotherBrainIs403()
    {
        using var history = new LockedHistory();
        await using var app = await Start(history);
        using var client = Client(app);

        var open = await client.GetAsync("/brains/b1/journal/tail", Ct);
        using var other = new HttpRequestMessage(HttpMethod.Get, "/brains/b2/journal/tail");
        other.Headers.Add("X-Test-Caller", "user");
        var denied = await client.SendAsync(other, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, open.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task TwoSignalsAppendedWithinTheWindowArriveInOneEvent()
    {
        using var history = new LockedHistory();
        history.Append(new Tick(1));
        await using var app = await Start(history);
        using var client = Client(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/brains/b1/journal/tail?since=0");
        request.Headers.Add("X-Test-Caller", "user");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));

        var first = await NextEvent(reader);
        // Longer than a poll, shorter than the 100 ms window: the second signal still joins the first one's batch.
        history.Append(new Tick(2));
        await Task.Delay(60, Ct);
        history.Append(new Tick(3));
        var second = await NextEvent(reader);

        Assert.Equal("journaled-signal", first.Type);
        Assert.Equal([1L], Sequences(first.Data));
        Assert.Equal("journaled-signal", second.Type);
        Assert.Equal([2L, 3L], Sequences(second.Data));
        var signal = second.Data.RootElement.GetProperty("signals")[0];
        Assert.Equal("tail.tick", signal.GetProperty("alias").GetString());
        Assert.Equal(Publisher.ToString(), signal.GetProperty("publisher").GetString());
        Assert.True(signal.TryGetProperty("at", out _));
        Assert.False(signal.TryGetProperty("signal", out _));
    }

    [Fact]
    public async Task MappingTheModuleSurfacesExposesTheTail()
    {
        using var history = new LockedHistory();
        history.Append(new Tick(1));
        await using var app = await Start(history, surfaces: true);
        using var client = Client(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/brains/b1/journal/tail");
        request.Headers.Add("X-Test-Caller", "user");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));

        var first = await NextEvent(reader);
        Assert.Equal([1L], Sequences(first.Data));
    }

    private static long[] Sequences(JsonDocument data)
        => [.. data.RootElement.GetProperty("signals").EnumerateArray().Select(item => item.GetProperty("seq").GetInt64())];

    private static async Task<(string Type, JsonDocument Data)> NextEvent(StreamReader reader)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string? type = null;
        string? data = null;
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal)) { type = line[7..]; }
            else if (line.StartsWith("data: ", StringComparison.Ordinal)) { data = line[6..]; }
            else if (line.Length == 0 && data is not null) { return (type ?? "message", JsonDocument.Parse(data)); }
        }

        throw new InvalidOperationException("The stream ended.");
    }

    private static HttpClient Client(WebApplication app) => new() { BaseAddress = new Uri(app.Urls.First()) };

    private static async Task<WebApplication> Start(ISignalHistory history, bool surfaces = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = typeof(JournalTailEndpointFacts).Assembly.GetName().Name,
            ContentRootPath = Path.GetTempPath(),
        });
        builder.WebHost.UseSetting(WebHostDefaults.PreventHostingStartupKey, "true");
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddKeyedSingleton<IIdentity>(Capability.Identity.V1Id, new StorageContentEndpointFacts.FakeIdentity());
        builder.Services.AddSingleton<Func<BrainId, ISignalHistory>>(_ => history);
        var app = builder.Build();
        app.Use(async (http, next) =>
        {
            if (http.Request.Headers.ContainsKey("X-Test-Caller"))
            {
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SessionClaims.Principal, "ada")], "TestAuth"));
            }
            await next(http);
        });
        if (surfaces) { app.MapModuleHttpSurfaces(); }
        else { JournalTailEndpoints.Map(app); }
        await app.StartAsync();
        return app;
    }

    // The in-memory history behind a lock, so the test appends while the tail reads; its tail wakes on every append, so the
    // timing of a batch is the endpoint's window alone.
    private sealed class LockedHistory : ISignalHistory, IDisposable
    {
        private readonly Lock _gate = new();
        private readonly InMemorySignalHistory _inner = new();
        private readonly SemaphoreSlim _appended = new(0);

        public void Dispose() => _appended.Dispose();

        public void Append(Signal signal)
        {
            lock (_gate) { _inner.Append(Publisher, signal, DateTimeOffset.UtcNow); }
            _appended.Release();
        }

        public async IAsyncEnumerable<JournaledSignal> Watch(long since, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var cursor = since;
            while (true)
            {
                var page = await Read(cursor, SignalPage.MaxLimit, ct);
                foreach (var item in page.Signals)
                {
                    cursor = item.Sequence;
                    yield return item;
                }

                if (page.Signals.Count == 0) { await _appended.WaitAsync(ct); }
            }
        }

        public Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct)
        {
            lock (_gate) { return _inner.Read(publisher, since, limit, ct); }
        }

        public Task<SignalPage> Read(long since, int limit, CancellationToken ct)
        {
            lock (_gate) { return _inner.Read(since, limit, ct); }
        }

        public Task<SignalPage> ReadTrace(string traceId, long since, int limit, CancellationToken ct)
        {
            lock (_gate) { return _inner.ReadTrace(traceId, since, limit, ct); }
        }
    }
}

[Signal("tail.tick")]
public sealed record Tick(int Value) : Signal;
