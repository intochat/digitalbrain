using System.Globalization;
using System.Net;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DigitalBrain.Tests.Slots;

// Two slot backends and the gateway, all in this process on loopback ports the operating system picks: the
// switch, /active, the health passthrough, an event stream that must arrive event by event rather than at
// the end, and the lease row the gateway reads before it accepts anything.
public sealed class GatewayFacts
{
    // Azurite's published development key. The lease table here is a loopback stand-in, so this only has to
    // be base64 the shared-key signer accepts; nothing authenticates against it.
    private const string DevelopmentAccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    [Fact]
    public async Task The_switch_moves_every_later_request_to_the_other_slot()
    {
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        await using var gateway = await GatewayAsync(slotA, slotB);
        using var client = Client(gateway);

        Assert.Equal("a", await ActiveAsync(client));
        Assert.Equal("ok:a", await client.GetStringAsync("/health", TestContext.Current.CancellationToken));

        using var switched = await client.PostAsync("/switch/b", content: null, TestContext.Current.CancellationToken);
        switched.EnsureSuccessStatusCode();
        // The switch answers with the same body as /active, so the caller confirms from the answer it has.
        Assert.Equal("b", SlotOf(await switched.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));

        Assert.Equal("b", await ActiveAsync(client));
        Assert.Equal("ok:b", await client.GetStringAsync("/health", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Switching_to_the_slot_that_is_already_active_is_accepted_and_changes_nothing()
    {
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        await using var gateway = await GatewayAsync(slotA, slotB);
        using var client = Client(gateway);

        using var switched = await client.PostAsync("/switch/a", content: null, TestContext.Current.CancellationToken);
        switched.EnsureSuccessStatusCode();
        Assert.Equal("a", await ActiveAsync(client));
    }

    [Fact]
    public async Task An_unknown_slot_is_refused()
    {
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        await using var gateway = await GatewayAsync(slotA, slotB);
        using var client = Client(gateway);

        using var refused = await client.PostAsync("/switch/c", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        // The gateway's own refusal, not a 404 the catch-all route forwarded to a backend.
        Assert.Contains("configured slot", await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal("a", await ActiveAsync(client));
    }

    [Fact]
    public async Task The_configured_active_slot_decides_the_first_route()
    {
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        await using var gateway = await GatewayAsync(slotA, slotB, active: "b");
        using var client = Client(gateway);

        Assert.Equal("b", await ActiveAsync(client));
        Assert.Equal("ok:b", await client.GetStringAsync("/health", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_early_re_switch_is_answered_with_the_wait_it_owes()
    {
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        // The product's own debounce, not the zero the other facts use.
        await using var gateway = await GatewayAsync(slotA, slotB, minSwitchInterval: "00:00:15");
        using var client = Client(gateway);

        using var first = await client.PostAsync("/switch/b", content: null, TestContext.Current.CancellationToken);
        first.EnsureSuccessStatusCode();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var early = await client.PostAsync("/switch/a", content: null, TestContext.Current.CancellationToken);
        clock.Stop();

        Assert.Equal(HttpStatusCode.TooManyRequests, early.StatusCode);
        // Answered, not held open: the caller owns the wait.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"the gateway held the switch for {clock.Elapsed}");
        var retryAfter = Assert.Single(early.Headers.GetValues("Retry-After"));
        Assert.InRange(int.Parse(retryAfter, CultureInfo.InvariantCulture), 1, 15);
        Assert.Equal("b", await ActiveAsync(client));
        Assert.Equal("ok:b", await client.GetStringAsync("/health", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_event_stream_arrives_event_by_event_through_the_gateway()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var slotA = await BackendAsync("a", release);
        await using var slotB = await BackendAsync("b");
        await using var gateway = await GatewayAsync(slotA, slotB);
        using var client = Client(gateway);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/stream");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(body);

        // The backend holds the rest of the stream until this line is read, so a gateway that buffered the
        // response would deadlock here instead of answering.
        Assert.Equal("""data: {"slot":"a","seq":1}""", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Equal(string.Empty, await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        release.SetResult();
        Assert.Equal("""data: {"slot":"a","seq":2}""", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Equal(string.Empty, await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Equal("data: [DONE]", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_stream_in_flight_finishes_on_the_slot_it_started_on()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var slotA = await BackendAsync("a", release);
        await using var slotB = await BackendAsync("b");
        await using var gateway = await GatewayAsync(slotA, slotB);
        using var client = Client(gateway);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/stream");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(body);
        Assert.Equal("""data: {"slot":"a","seq":1}""", await reader.ReadLineAsync(TestContext.Current.CancellationToken));

        using var switched = await client.PostAsync("/switch/b", content: null, TestContext.Current.CancellationToken);
        switched.EnsureSuccessStatusCode();
        Assert.Equal("ok:b", await client.GetStringAsync("/health", TestContext.Current.CancellationToken));

        // The stream was routed while a was active and stays on a: the switch moves later requests only.
        release.SetResult();
        Assert.Equal(string.Empty, await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Equal("""data: {"slot":"a","seq":2}""", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_lease_rows_owner_decides_the_first_active_slot()
    {
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        await using var leases = await LeaseTableAsync(owner: "b");
        await using var gateway = await GatewayAsync(slotA, slotB, active: "a", clustering: TableConnection(leases));
        using var client = Client(gateway);

        Assert.Equal("b", await ActiveAsync(client));
        Assert.Equal("ok:b", await client.GetStringAsync("/health", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_absent_lease_row_leaves_the_configured_slot_active()
    {
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        await using var leases = await LeaseTableAsync(owner: null);
        await using var gateway = await GatewayAsync(slotA, slotB, active: "b", clustering: TableConnection(leases));
        using var client = Client(gateway);

        Assert.Equal("b", await ActiveAsync(client));
        Assert.Equal("ok:b", await client.GetStringAsync("/health", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_lease_row_that_never_answers_leaves_the_gateway_listening_on_the_configured_slot()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        // The row would name a, so a gateway that waited for this answer would route to a instead of b. The
        // read's budget is a fixed ten seconds, which is what this fact waits out once.
        await using var leases = await LeaseTableAsync(owner: "a", hold: hold);
        await using var gateway = await GatewayAsync(slotA, slotB, active: "b", clustering: TableConnection(leases));
        using var client = Client(gateway);

        Assert.Equal("b", await ActiveAsync(client));
        Assert.Equal("ok:b", await client.GetStringAsync("/health", TestContext.Current.CancellationToken));
        hold.SetResult();
    }

    [Fact]
    public async Task A_slot_named_in_another_case_is_the_same_slot()
    {
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        await using var gateway = await GatewayAsync(slotA, slotB);
        using var client = Client(gateway);

        using var switched = await client.PostAsync("/switch/B", content: null, TestContext.Current.CancellationToken);
        switched.EnsureSuccessStatusCode();

        // A route built from "B" would name a cluster nothing answers to, so /health would fail rather than
        // move: the switch alone answering 200 is not evidence that traffic followed.
        Assert.Equal("b", SlotOf(await switched.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
        Assert.Equal("b", await ActiveAsync(client));
        Assert.Equal("ok:b", await client.GetStringAsync("/health", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Configuration_the_gateway_cannot_route_on_is_refused_by_its_key()
    {
        var notAnAddress = Assert.Throws<InvalidOperationException>(
            () => GatewayOptions.From(Configuration(("DigitalBrain:Gateway:Slots:b", "localhost:5082"))));
        Assert.Contains("Slots:b", notAnAddress.Message, StringComparison.Ordinal);

        var noSuchSlot = Assert.Throws<InvalidOperationException>(
            () => GatewayOptions.From(Configuration(("DigitalBrain:Gateway:Active", "c"))));
        Assert.Contains("Active", noSuchSlot.Message, StringComparison.Ordinal);

        var notADuration = Assert.Throws<InvalidOperationException>(
            () => GatewayOptions.From(Configuration(("DigitalBrain:Gateway:MinSwitchInterval", "15s"))));
        Assert.Contains("MinSwitchInterval", notADuration.Message, StringComparison.Ordinal);

        var negativeDuration = Assert.Throws<InvalidOperationException>(
            () => GatewayOptions.From(Configuration(("DigitalBrain:Gateway:MinSwitchInterval", "-00:00:01"))));
        Assert.Contains("MinSwitchInterval", negativeDuration.Message, StringComparison.Ordinal);

        // The configured active slot is resolved to the name the clusters are built from, once, here.
        Assert.Equal("b", GatewayOptions.From(Configuration(("DigitalBrain:Gateway:Active", "B"))).Active);

        var defaults = GatewayOptions.From(new ConfigurationBuilder().Build());
        Assert.Equal("a", defaults.Active);
        Assert.Equal(new Uri("http://localhost:5081"), defaults.Slots["a"]);
        Assert.Equal(new Uri("http://localhost:5082"), defaults.Slots["b"]);
        Assert.Equal(TimeSpan.FromSeconds(15), defaults.MinSwitchInterval);
    }

    private static HttpClient Client(WebApplication gateway)
        => new() { BaseAddress = new Uri(Address(gateway)), Timeout = TimeSpan.FromSeconds(30) };

    private static async Task<string> ActiveAsync(HttpClient client)
        => SlotOf(await client.GetStringAsync("/active", TestContext.Current.CancellationToken));

    private static string SlotOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("active").GetString()!;
    }

    private static string Address(WebApplication app) => app.Urls.First();

    private static IConfiguration Configuration(params (string Key, string Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build();

    // The stream endpoint holds its second half until the fact that owns this source releases it, so
    // "the bytes arrived before the response ended" is an assertion rather than a timing guess.
    private static async Task<WebApplication> BackendAsync(string slot, TaskCompletionSource? release = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapGet("/health", () => Results.Text("ok:" + slot));
        app.MapGet("/stream", async http =>
        {
            http.Response.ContentType = "text/event-stream";
            await http.Response.WriteAsync($"data: {{\"slot\":\"{slot}\",\"seq\":1}}\n\n", http.RequestAborted);
            await http.Response.Body.FlushAsync(http.RequestAborted);
            if (release is not null)
            {
                await release.Task.WaitAsync(TimeSpan.FromSeconds(20), http.RequestAborted);
            }

            await http.Response.WriteAsync($"data: {{\"slot\":\"{slot}\",\"seq\":2}}\n\n", http.RequestAborted);
            await http.Response.WriteAsync("data: [DONE]\n\n", http.RequestAborted);
            await http.Response.Body.FlushAsync(http.RequestAborted);
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    // The one table read the gateway performs at startup, answered either with a row naming an owner or with
    // the 404 an empty table gives. A hold, when the fact passes one, keeps the answer back until the fact
    // releases it, which is how "the gateway gave up on its own" becomes an assertion.
    private static async Task<WebApplication> LeaseTableAsync(string? owner, TaskCompletionSource? hold = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapGet("/{**rest}", async http =>
        {
            if (hold is not null)
            {
                await hold.Task.WaitAsync(TimeSpan.FromSeconds(60), http.RequestAborted);
            }

            if (owner is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            http.Response.ContentType = "application/json;odata=minimalmetadata";
            await http.Response.WriteAsync(
                $$"""{"PartitionKey":"{{ActiveSlotNames.PartitionKey}}","RowKey":"{{ActiveSlotNames.RowKey}}","{{ActiveSlotNames.Owner}}":"{{owner}}"}""",
                http.RequestAborted);
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static string TableConnection(WebApplication leases)
        => $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={DevelopmentAccountKey};"
            + $"TableEndpoint={Address(leases)}/devstoreaccount1;";

    private static async Task<WebApplication> GatewayAsync(
        WebApplication slotA,
        WebApplication slotB,
        string active = "a",
        string minSwitchInterval = "00:00:00",
        string? clustering = null)
    {
        List<string> args =
        [
            "--urls=http://127.0.0.1:0",
            // The gateway keeps its real logging pipeline; only the level is turned down, as the backends do.
            "--Logging:LogLevel:Default=Warning",
            "--DigitalBrain:Gateway:Slots:a=" + Address(slotA),
            "--DigitalBrain:Gateway:Slots:b=" + Address(slotB),
            "--DigitalBrain:Gateway:Active=" + active,
            "--DigitalBrain:Gateway:MinSwitchInterval=" + minSwitchInterval,
        ];
        if (clustering is not null)
        {
            args.Add($"--ConnectionStrings:{DigitalBrainNames.Clustering}={clustering}");
        }

        var app = await GatewayApplication.CreateAsync([.. args]);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
