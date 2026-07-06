using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Tests.E2E.Packs;
using Grpc.Core;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class TravelPlanTripRendersE2ETests(DigitalBrainAppHostFixture fixture)
{
    readonly DigitalBrainAppHostFixture _fx = fixture;

    [SkippableFact]
    public async Task PlanTrip_walks_hops_and_each_hop_is_delivered_over_the_real_wire()
    {
        E2EPrerequisites.RequireRealStackE2E();

        await _fx.PublishPackAsync("travel", "1.0", code: TravelPackSource.Read(),
            description: "Travel domain — Plan a trip experience");
        await _fx.InstallPackAsync("travel", "1.0", buyer: "e2e-travel");

        using var channel = _fx.CreateGatewayGrpcChannel();
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var feed = client.WatchHomeFeed(new WatchHomeFeedRequest(), cancellationToken: cts.Token);
        await Task.Delay(750, cts.Token); // subscribe-before-emit, same pattern as NativeGrpcGalleryDeliveryE2ETests

        await AwaitHop(feed.ResponseStream, "start", "travel-intro", cts.Token, ("prompt", "plan a trip to Bali next month"));
        await AwaitHop(feed.ResponseStream, "flight.selected", "travel-hotels", cts.Token, ("flightId", "FL-001"));
        await AwaitHop(feed.ResponseStream, "hotel.selected", "travel-events", cts.Token, ("hotelId", "H-001"));
        await AwaitHop(feed.ResponseStream, "event.selected", "travel-activities", cts.Token, ("eventId", "EV-001"));
        await AwaitHop(feed.ResponseStream, "activity.selected", "travel-summary", cts.Token, ("activityId", "AC-001"));
    }

    async Task AwaitHop(IAsyncStreamReader<RfwCardEnvelope> stream, string eventName, string expectedSurfaceId,
        CancellationToken ct, params (string key, string value)[] args)
    {
        var delivered = ReadForSurfaceIdAsync(stream, expectedSurfaceId, ct);
        await _fx.SendExperienceStepAsync("travel", "plan-trip", eventName, args.ToDictionary(a => a.key, a => a.value));
        Assert.True(await delivered, $"'{expectedSurfaceId}' hop was not delivered over WatchHomeFeed");
    }

    static async Task<bool> ReadForSurfaceIdAsync(IAsyncStreamReader<RfwCardEnvelope> stream, string surfaceId, CancellationToken ct)
    {
        try
        {
            while (await stream.MoveNext(ct))
            {
                if (stream.Current.CorrelationId == surfaceId) return true;
            }
        }
        catch (RpcException) { }
        catch (OperationCanceledException) { }
        return false;
    }
}
