using System.Text.Json;
using DigitalBrain.Runtime.Grpc;
using Grpc.Core;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Trait("Group", "Flutter")]
[Trait("Group", "Marketplace")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class PackEmbodimentRendersE2ETests(DigitalBrainAppHostFixture fixture)
{
    private readonly DigitalBrainAppHostFixture _fx = fixture;

    [SkippableFact]
    public async Task InstallsRealPack_EmbodiedCode_DeliversSurfaceOverTheRealWire()
    {
        E2EPrerequisites.RequireRealStackE2E();

        const string packName = "E2ESurfacePack";
        const string version = "1.0";
        const string surfaceId = "pack-surface-e2e";

        await _fx.PublishPackAsync(packName, version,
            code: TestPacks.RenderableSurfacePack(surfaceId),
            description: "E2E pack that emits a renderable surface");
        await _fx.InstallPackAsync(packName, version, buyer: "e2e-ui-watcher");

        using var channel = _fx.CreateGatewayGrpcChannel();
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var feed = client.WatchHomeFeed(new WatchHomeFeedRequest(), cancellationToken: cts.Token);
        var delivered = ReadForSurfaceIdAsync(feed.ResponseStream, surfaceId, cts.Token);
        await Task.Delay(750, cts.Token);

        await _fx.SendSynapseAsync(
            "DigitalBrain.Kernel.SurfaceDemoRequested",
            $"{{\"source\":\"{surfaceId}\"}}",
            correlationId: surfaceId);

        Assert.True(await delivered, $"Surface '{surfaceId}' was not delivered over WatchHomeFeed");
    }

    static async Task<bool> ReadForSurfaceIdAsync(IAsyncStreamReader<RfwCardEnvelope> stream, string surfaceId, CancellationToken ct)
    {
        try
        {
            while (await stream.MoveNext(ct))
            {
                var json = stream.Current.DataJson;
                if (string.IsNullOrEmpty(json)) continue;
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("surfaceId", out var sid) && sid.GetString() == surfaceId)
                    return true;
            }
        }
        catch (RpcException) { }
        catch (OperationCanceledException) { }
        return false;
    }
}
