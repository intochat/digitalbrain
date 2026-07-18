using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Ino.Grpc;
using Ino.Testing.E2E;
using Xunit;

namespace Ino.Domains.Travel.Tests;

/// <summary>
/// End-to-end coverage for the rich six-hop Plan Trip flow under BDD mocks.
/// Boots the real Aspire AppHost (with <c>INO_TEST_MODE=true</c> so silos
/// route via <c>BddMockChatClientFactory</c>) and drives the kernel's
/// gateway gRPC service directly — no Flutter UI in the loop.
///
/// Why no browser: the Plan Trip neuron needs to walk five user
/// selections in sequence (flight → hotel → event → activity → confirmation),
/// and Flutter's CanvasKit renderer makes UI clicking unreliable. Driving
/// <see cref="global::Ino.Grpc.Ino.InoClient.RfwEvent"/> via gRPC exercises the same wire
/// path the Flutter client takes when the user taps a Select button — same
/// gateway, same Cortex, same plan grain — without the browser overhead.
/// Browser-level rendering of the new card libraries is verified manually
/// per <c>docs/neuron-anatomy.md</c>'s verification target.
/// </summary>
[Collection(nameof(TripPlanningCollection))]
[Trait("Neuron", "PlanTrip")]
public sealed class RichTripPlanningE2ETests(InoBrowserFixture<Projects.Ino_AppHost> fx)
{
    const int InitialTimeoutMs = 30_000;

    [Fact]
    public async Task Plan_trip_to_bali_walks_full_six_hop_flow_under_bdd_mocks()
    {
        using var channel = CreateChannel(fx.KernelSiloUrl);
        var client = new global::Ino.Grpc.Ino.InoClient(channel);
        var userId = $"rich-bali-{Guid.NewGuid():N}";

        // Hop 1 — initial chat. The plan grain emits the trip-intro RFW
        // (weather summary + flight cards) on its first response.
        var (intro, correlationId) = await SendChatAsync(client, userId, "plan a trip to Bali next month");
        Assert.False(string.IsNullOrEmpty(correlationId));
        var introBody = DecodeRfw(intro);
        Assert.Contains("ino.travel.intro", intro.ContentType);
        Assert.Contains("import ino.weather",  introBody);
        Assert.Contains("import ino.flights",  introBody);
        Assert.Contains("WeatherSummaryCard",  introBody);
        Assert.Contains("FlightCard",          introBody);

        // Hop 2 — flight selected → hotel cards.
        var hotels = await FireRfwEventAsync(client, correlationId,
            "flight.selected", new() { ["flightId"] = "FL-001" });
        Assert.Contains("ino.travel.hotels", hotels.ContentType);
        Assert.Contains("HotelCard", DecodeRfw(hotels));

        // Hop 3 — hotel selected → event cards with skip affordance.
        var events = await FireRfwEventAsync(client, correlationId,
            "hotel.selected", new() { ["hotelId"] = "H-001" });
        Assert.Contains("ino.travel.events", events.ContentType);
        var eventsBody = DecodeRfw(events);
        Assert.Contains("EventCard",       eventsBody);
        Assert.Contains("EventSkipButton", eventsBody);

        // Hop 4 — event selected → activity cards (weather-aware badges).
        var activities = await FireRfwEventAsync(client, correlationId,
            "event.selected", new() { ["eventId"] = "EV-001" });
        Assert.Contains("ino.travel.activities", activities.ContentType);
        var activitiesBody = DecodeRfw(activities);
        Assert.Contains("ActivityCard", activitiesBody);
        // Mock corpus emits "Sunny day pick"/"Rainy day pick"/etc — confirm the
        // weather-aware ranking actually decorated the cards.
        var activitiesData = Encoding.UTF8.GetString(activities.RfwData.ToByteArray());
        Assert.Contains("weatherBadge", activitiesData);

        // Hop 5 — activity selected → trip summary card; flow closes.
        var summary = await FireRfwEventAsync(client, correlationId,
            "activity.selected", new() { ["activityId"] = "AC-001" });
        Assert.Contains("ino.travel.summary", summary.ContentType);
        var summaryData = Encoding.UTF8.GetString(summary.RfwData.ToByteArray());
        Assert.Contains("Bali",               summaryData);
        Assert.Contains("Singapore Airlines", summaryData); // FL-001's airline
    }

    /// <summary>
    /// Walks the events-skipped variant of hop 4 — proves the alternate
    /// transition (no event selected) lands on the same activities slate.
    /// </summary>
    [Fact]
    public async Task Plan_trip_can_skip_events_and_still_reach_activities()
    {
        using var channel = CreateChannel(fx.KernelSiloUrl);
        var client = new global::Ino.Grpc.Ino.InoClient(channel);
        var userId = $"rich-skip-{Guid.NewGuid():N}";

        var (_, correlationId) = await SendChatAsync(client, userId, "plan a trip to Bali next month");
        await FireRfwEventAsync(client, correlationId,
            "flight.selected", new() { ["flightId"] = "FL-001" });
        await FireRfwEventAsync(client, correlationId,
            "hotel.selected", new() { ["hotelId"] = "H-001" });

        var activities = await FireRfwEventAsync(client, correlationId,
            "events.skipped", new());
        Assert.Contains("ino.travel.activities", activities.ContentType);
        Assert.Contains("ActivityCard", DecodeRfw(activities));
    }

    static GrpcChannel CreateChannel(string baseUrl)
    {
        // Aspire dev silos serve HTTP/2 over a self-signed HTTPS cert. The
        // browser-side flutter client takes gRPC-Web through the same
        // endpoint; here we connect server-to-server via plain HTTP/2 gRPC
        // — fewer moving parts and the request still flows through Cortex
        // → plan grain → gateway exactly the same way.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        return GrpcChannel.ForAddress(baseUrl, new GrpcChannelOptions
        {
            HttpHandler = handler,
        });
    }

    static async Task<(ChatResponse Final, string CorrelationId)> SendChatAsync(
        global::Ino.Grpc.Ino.InoClient client, string userId, string message)
    {
        var call = client.Chat(new ChatRequest
        {
            Message = message,
            UserId = userId,
        });

        ChatResponse? final = null;
        var correlationId = string.Empty;
        // Drain the stream to its end. Skeleton frames precede the final
        // RFW; we keep the latest non-skeleton frame.
        await foreach (var frame in call.ResponseStream.ReadAllAsync().WithCancellation(new CancellationTokenSource(InitialTimeoutMs).Token))
        {
            if (!string.IsNullOrEmpty(frame.CorrelationId))
                correlationId = frame.CorrelationId;
            if (!frame.IsSkeleton && frame.RfwDescription.Length > 0)
                final = frame;
        }

        Assert.NotNull(final);
        return (final!, correlationId);
    }

    static async Task<RfwEventResponse> FireRfwEventAsync(
        global::Ino.Grpc.Ino.InoClient client,
        string correlationId,
        string eventName,
        Dictionary<string, string> args)
    {
        var req = new RfwEventRequest
        {
            CorrelationId = correlationId,
            EventName = eventName,
        };
        foreach (var kv in args) req.Args[kv.Key] = kv.Value;

        var resp = await client.RfwEventAsync(req);
        Assert.True(resp.Accepted,
            $"RfwEvent({eventName}) rejected — reply: {resp.Reply}");
        Assert.True(resp.RfwDescription.Length > 0,
            $"RfwEvent({eventName}) returned no RFW payload — reply: {resp.Reply}");
        return resp;
    }

    static string DecodeRfw(ChatResponse r) =>
        Encoding.UTF8.GetString(r.RfwDescription.ToByteArray());

    static string DecodeRfw(RfwEventResponse r) =>
        Encoding.UTF8.GetString(r.RfwDescription.ToByteArray());
}
