using Grpc.Net.Client;
using Ino.Grpc;
using Ino.Testing.E2E;
using Xunit;

namespace Ino.Domains.Travel.Tests;

/// <summary>
/// Pins the AskIno routing boundary: gRPC AskIno → InoNeuron.AskAsync →
/// ICortexCapability.RouteAsync must route travel prompts via Regex and
/// unknown prompts as Unrouted. These tests correspond 1:1 to the three
/// scenarios in Features/ino-ask.feature (which also lives in the BDD corpus
/// so the mock client's regex registry covers the travel-tagged prompts).
///
/// Why gRPC, not TestCluster: the test project boots the full Aspire AppHost
/// with INO_TEST_MODE=true (BddMockChatClientFactory), driving the exact same
/// gRPC wire path the Flutter client and MCP server use — no test-only
/// shortcuts that could mask a regression.
///
/// Run scoped with:
///   dotnet test ino.slnx --filter "FullyQualifiedName~AskInoRoutingTests"
/// </summary>
[Collection(nameof(TripPlanningCollection))]
[Trait("Feature", "ino-ask")]
public sealed class AskInoRoutingTests(InoBrowserFixture<Projects.Ino_AppHost> fx)
{
    const int TimeoutMs = 30_000;

    // Scenario: AskIno plans a trip
    // Source=Regex because "plan a trip to Bali next month" matches the
    // @neuron:travel.plan-trip regex in travel-intent.feature.
    [Fact]
    public async Task AskIno_plans_a_trip_routes_via_regex()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeoutMs);

        using var channel = CreateChannel(fx.KernelSiloUrl);
        var client = new global::Ino.Grpc.Ino.InoClient(channel);

        var response = await client.AskInoAsync(
            new AskInoRequest { Prompt = "plan a trip to Bali next month", UserId = UniqueUserId() },
            cancellationToken: cts.Token);

        Assert.Equal("Regex", response.Source);
        Assert.True(response.Success);
        Assert.False(string.IsNullOrWhiteSpace(response.Reply));
    }

    // Scenario: AskIno finds flights
    // "find flights to Tokyo" matches the @neuron:travel.find-flights
    // pattern "flights? to" (from travel-intent.feature line 21).
    [Fact]
    public async Task AskIno_finds_flights_routes_via_regex()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeoutMs);

        using var channel = CreateChannel(fx.KernelSiloUrl);
        var client = new global::Ino.Grpc.Ino.InoClient(channel);

        var response = await client.AskInoAsync(
            new AskInoRequest { Prompt = "find flights to Tokyo", UserId = UniqueUserId() },
            cancellationToken: cts.Token);

        Assert.Equal("Regex", response.Source);
        Assert.True(response.Success);
    }

    // Scenario: AskIno on unknown intent returns Unrouted
    // "asdf qwerty" matches no regex in the corpus and the BDD mock doesn't
    // classify it → CortexCapability falls through to EmitUnroutedAsync.
    [Fact]
    public async Task AskIno_unknown_prompt_returns_unrouted_with_no_specialist_reply()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeoutMs);

        using var channel = CreateChannel(fx.KernelSiloUrl);
        var client = new global::Ino.Grpc.Ino.InoClient(channel);

        var response = await client.AskInoAsync(
            new AskInoRequest { Prompt = "asdf qwerty", UserId = UniqueUserId() },
            cancellationToken: cts.Token);

        Assert.Equal("Unrouted", response.Source);
        Assert.True(response.Success);
        Assert.Contains("No specialist", response.Reply);
    }

    static GrpcChannel CreateChannel(string baseUrl) =>
        GrpcChannel.ForAddress(baseUrl, new GrpcChannelOptions
        {
            HttpHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            },
        });

    // Unique per-test user id prevents grain state from leaking across
    // parallel test runs (same pattern as RichTripPlanningE2ETests).
    static string UniqueUserId() => $"ask-ino-{Guid.NewGuid():N}";
}
