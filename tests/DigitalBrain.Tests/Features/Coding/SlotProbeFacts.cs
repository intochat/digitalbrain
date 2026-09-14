using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Coding;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Coding;

// The probes against a real HTTP stack: an in-process host serving the silo's own /slots/{slot} route plus
// the health, switch and active answers a kernel and a gateway give, so every failure shape the promotion
// depends on is exercised over HTTP rather than mocked.
public sealed class SlotProbeFacts
{
    private static readonly Uri SlotAddress = new("http://slot-b:5082");
    private static readonly Uri GatewayAddress = new("http://gateway:5080");

    // Nothing listens on port 1, so the handler fails to connect rather than answering.
    private static readonly Uri Unreachable = new("http://127.0.0.1:1");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_is_the_slot_answering_and_nothing_else()
    {
        await using var host = await SlotHost.StartAsync("b");
        Assert.True(await host.Endpoints.HealthyAsync(SlotAddress, Cancellation));

        host.Answers.Health = StatusCodes.Status503ServiceUnavailable;
        Assert.False(await host.Endpoints.HealthyAsync(SlotAddress, Cancellation));
    }

    [Fact]
    public async Task A_slot_that_is_not_listening_is_not_healthy_and_is_not_an_error()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var endpoints = new HttpSlotEndpoints(client);

        Assert.False(await endpoints.HealthyAsync(Unreachable, Cancellation));
        Assert.Contains("failed", await endpoints.SmokeAsync(Unreachable, "/chats/slot-smoke/brain", Cancellation) ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_smoke_read_is_the_status_the_slot_gave_it()
    {
        await using var host = await SlotHost.StartAsync("b");
        Assert.Null(await host.Endpoints.SmokeAsync(SlotAddress, SlotHost.SmokePath, Cancellation));

        var failure = await host.Endpoints.SmokeAsync(SlotAddress, "/chats/missing/brain", Cancellation);
        Assert.Contains("404", failure ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_lease_read_is_true_only_for_the_slot_the_silo_says_it_is()
    {
        await using var host = await SlotHost.StartAsync("b");

        Assert.True(await host.Endpoints.HoldsLeaseAsync(SlotAddress, "b", Cancellation));
        // Slot a asking this silo learns nothing about its own flip: the answer names slot b.
        Assert.False(await host.Endpoints.HoldsLeaseAsync(SlotAddress, "a", Cancellation));
    }

    [Fact]
    public async Task The_slot_read_names_the_silo_whether_or_not_it_holds_the_lease()
    {
        await using var host = await SlotHost.StartAsync("b");

        // What a promotion reads before it flips anything: a silo configured as some other slot could
        // never report this one's lease, so the name it gives is the difference between advice and a wait.
        Assert.Equal("b", await host.Endpoints.NamedSlotAsync(SlotAddress, "a", Cancellation));
        Assert.Equal("b", await host.Endpoints.NamedSlotAsync(SlotAddress, "b", Cancellation));

        await using var unslotted = await SlotHost.StartAsync(new SingleSlotLease(string.Empty));
        Assert.Equal(string.Empty, await unslotted.Endpoints.NamedSlotAsync(SlotAddress, "b", Cancellation));

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // A silo that is not listening names nothing, which is no evidence that the name is wrong.
        Assert.Null(await new HttpSlotEndpoints(client).NamedSlotAsync(Unreachable, "b", Cancellation));
    }

    [Fact]
    public async Task An_unslotted_silo_claims_no_slot_at_all()
    {
        // What a phase 0 or phase 1 host registers: one silo, no slot name, always its own holder.
        await using var host = await SlotHost.StartAsync(new SingleSlotLease(string.Empty));

        Assert.False(await host.Endpoints.HoldsLeaseAsync(SlotAddress, "a", Cancellation));
        Assert.False(await host.Endpoints.HoldsLeaseAsync(SlotAddress, "b", Cancellation));
    }

    [Fact]
    public async Task The_slot_route_names_the_silo_its_lease_and_its_lease_generation()
    {
        await using var host = await SlotHost.StartAsync("b");
        using var client = host.NewClient();

        using var response = await client.GetAsync(new Uri(SlotAddress, "/slots/a"), Cancellation);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("b", body.GetProperty("slot").GetString());
        Assert.False(body.GetProperty("holdsLease").GetBoolean());
        // Not the slot's git Generation: this one counts lease handovers.
        Assert.Equal(0, body.GetProperty("leaseGeneration").GetInt64());
    }

    [Fact]
    public async Task An_accepted_switch_asks_for_no_wait()
    {
        await using var host = await SlotHost.StartAsync("b");
        Assert.Null(await host.Endpoints.SwitchAsync(GatewayAddress, "b", Cancellation));
    }

    [Fact]
    public async Task A_debounced_switch_waits_as_long_as_the_gateway_asked()
    {
        await using var host = await SlotHost.StartAsync("b");
        host.Answers.Switch = StatusCodes.Status429TooManyRequests;
        host.Answers.RetryAfter = "5";

        Assert.Equal(TimeSpan.FromSeconds(5), await host.Endpoints.SwitchAsync(GatewayAddress, "b", Cancellation));
    }

    [Fact]
    public async Task A_retry_after_that_has_already_passed_still_waits_a_second()
    {
        await using var host = await SlotHost.StartAsync("b");
        host.Answers.Switch = StatusCodes.Status429TooManyRequests;
        host.Answers.RetryAfter = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("R", CultureInfo.InvariantCulture);

        // A date in the past, a zero delta or a missing header would all be a busy loop otherwise.
        Assert.Equal(TimeSpan.FromSeconds(1), await host.Endpoints.SwitchAsync(GatewayAddress, "b", Cancellation));

        host.Answers.RetryAfter = "0";
        Assert.Equal(TimeSpan.FromSeconds(1), await host.Endpoints.SwitchAsync(GatewayAddress, "b", Cancellation));

        host.Answers.RetryAfter = null;
        Assert.Equal(TimeSpan.FromSeconds(1), await host.Endpoints.SwitchAsync(GatewayAddress, "b", Cancellation));
    }

    [Fact]
    public async Task A_refused_or_unreachable_gateway_is_one_failure_naming_it()
    {
        await using var host = await SlotHost.StartAsync("b");
        host.Answers.Switch = StatusCodes.Status500InternalServerError;
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Endpoints.SwitchAsync(GatewayAddress, "b", Cancellation));
        Assert.Contains("gateway", refused.Message, StringComparison.Ordinal);
        Assert.Contains("500", refused.Message, StringComparison.Ordinal);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var unreachable = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new HttpSlotEndpoints(client).SwitchAsync(Unreachable, "b", Cancellation));
        Assert.IsType<HttpRequestException>(unreachable.InnerException);
    }

    [Fact]
    public async Task A_gateway_that_does_not_answer_in_time_reads_as_no_active_slot()
    {
        await using var host = await SlotHost.StartAsync("b");
        Assert.Equal("a", await host.Endpoints.ActiveAsync(GatewayAddress, Cancellation));

        host.Answers.ActiveDelay = TimeSpan.FromSeconds(30);
        using var impatient = host.NewClient();
        // The real client waits 60 s; a promotion that read an exception here would fail instead of retry.
        impatient.Timeout = TimeSpan.FromMilliseconds(200);
        Assert.Null(await new HttpSlotEndpoints(impatient).ActiveAsync(GatewayAddress, Cancellation));

        host.Answers.ActiveDelay = TimeSpan.Zero;
        host.Answers.Active = "not json at all";
        Assert.Null(await host.Endpoints.ActiveAsync(GatewayAddress, Cancellation));
    }

    [Fact]
    public async Task The_lease_read_reaches_a_gated_silo_only_with_the_owner_credential()
    {
        await using var host = await SlotHost.StartAsync("b", "owner", "s3cret");

        // The gate exempts /health, so a credential-less probe reads healthy and still learns nothing.
        Assert.True(await host.Endpoints.HealthyAsync(SlotAddress, Cancellation));
        Assert.False(await host.Endpoints.HoldsLeaseAsync(SlotAddress, "b", Cancellation));

        using var owner = host.NewClient();
        owner.DefaultRequestHeaders.Authorization = SlotProbeCredential.Of(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SlotProbeCredential.UsernameKey] = "owner",
                [SlotProbeCredential.PasswordKey] = "s3cret",
            })
            .Build());

        Assert.True(await new HttpSlotEndpoints(owner).HoldsLeaseAsync(SlotAddress, "b", Cancellation));
    }

    // The answers a kernel and a gateway give, mutable per fact.
    private sealed class SlotAnswers
    {
        public int Health { get; set; } = StatusCodes.Status200OK;

        public int Switch { get; set; } = StatusCodes.Status200OK;

        public string? RetryAfter { get; set; }

        public string Active { get; set; } = """{"active":"a"}""";

        public TimeSpan ActiveDelay { get; set; }
    }

    private sealed class SlotHost : IAsyncDisposable
    {
        public const string SmokePath = "/chats/slot-smoke/brain";

        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private SlotHost(WebApplication app, SlotAnswers answers)
        {
            _app = app;
            Answers = answers;
            _client = app.GetTestServer().CreateClient();
            Endpoints = new HttpSlotEndpoints(_client);
        }

        public SlotAnswers Answers { get; }

        public HttpSlotEndpoints Endpoints { get; }

        public static Task<SlotHost> StartAsync(string leaseSlot, string? username = null, string? password = null)
            => StartAsync(new InMemoryActiveSlotLease(leaseSlot), username, password);

        public static async Task<SlotHost> StartAsync(IActiveSlotLease lease, string? username = null, string? password = null)
        {
            var answers = new SlotAnswers();
            // CreateSlimBuilder + TestServer is how the other kernel HTTP facts host the real endpoints.
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            if (username is not null && password is not null)
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [BasicAuthGate.UsernameConfigurationKey] = username,
                    [BasicAuthGate.PasswordConfigurationKey] = password,
                });
            }

            builder.Services.AddSingleton(lease);
            var app = builder.Build();
            try
            {
                app.UseBasicAuthGate();
                app.MapSlotEndpoints();
                app.MapGet("/health", () => Results.StatusCode(answers.Health));
                app.MapGet(SmokePath, static () => Results.Ok(new { phase = "smoke" }));
                app.MapPost("/switch/{slot}", (string slot, HttpResponse response) =>
                {
                    if (answers.RetryAfter is { } retry)
                    {
                        response.Headers.RetryAfter = retry;
                    }

                    return Results.StatusCode(answers.Switch);
                });
                app.MapGet("/active", async (CancellationToken cancellationToken) =>
                {
                    if (answers.ActiveDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(answers.ActiveDelay, cancellationToken);
                    }

                    return Results.Text(answers.Active, "application/json");
                });
                await app.StartAsync();
                return new SlotHost(app, answers);
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        public HttpClient NewClient() => _app.GetTestServer().CreateClient();

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.DisposeAsync();
        }
    }
}
