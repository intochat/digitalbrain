using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Tests.Authoring;
using Grpc.Core;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class StarterBundleRendersE2ETests(DigitalBrainAppHostFixture fixture)
{
    readonly DigitalBrainAppHostFixture _fx = fixture;

    [SkippableFact]
    public async Task Starter_asks_then_echoes_over_the_real_wire()
    {
        E2EPrerequisites.RequireRealStackE2E();

        await _fx.PublishPackAsync(StarterBundleSource.Pack, "1.0", code: StarterBundleSource.Code,
            description: "Starter bundle");
        await _fx.InstallPackAsync(StarterBundleSource.Pack, "1.0", buyer: "e2e-starter");

        using var channel = _fx.CreateGatewayGrpcChannel();
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var feed = client.WatchHomeFeed(new WatchHomeFeedRequest(), cancellationToken: cts.Token);
        await Task.Delay(750, cts.Token);

        var askDelivered = ReadForSurfaceIdAsync(feed.ResponseStream, StarterBundleSource.Hops.Ask, cts.Token);
        await _fx.SendExperienceStepAsync(StarterBundleSource.Pack, StarterBundleSource.ExperienceId, "start");
        Assert.True(await askDelivered, $"'{StarterBundleSource.Hops.Ask}' hop was not delivered over WatchHomeFeed");

        var resultDelivered = ReadForSurfaceIdAsync(feed.ResponseStream, StarterBundleSource.Hops.Result, cts.Token);
        await _fx.SendExperienceStepAsync(StarterBundleSource.Pack, StarterBundleSource.ExperienceId,
            StarterBundleSource.Hops.Result, new Dictionary<string, string> { ["message"] = "ping" });
        Assert.True(await resultDelivered, $"'{StarterBundleSource.Hops.Result}' hop was not delivered over WatchHomeFeed");
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
