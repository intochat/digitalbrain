using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Tests.TestSupport;
using Google.Protobuf;

namespace DigitalBrain.Tests.Gateway;

// Proves the useful behavior behind the former native-gRPC AppHost E2E test without booting containers:
// Gateway Send(InoRequest) reaches the real Ino grain, which delivers the UiKit gallery surface through
// FlutterUiNeuron/HomeFeedBus to the watching client stream.
[Trait("Category", "cluster")]
public sealed class InoGalleryHomeFeedClusterTests : GatewayClusterTestBase
{
    [Fact]
    public async Task Send_InoRequest_Delivers_Uikit_Gallery_Surface_To_WatchHomeFeed()
    {
        var service = NewGatewayService();
        var clientId = "cluster-gallery-" + Guid.NewGuid().ToString("N")[..8];

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var writer = new CapturingServerStreamWriter<RfwCardEnvelope>();
        var watchTask = service.WatchHomeFeed(
            new WatchHomeFeedRequest { ClientId = clientId },
            writer,
            TestContext(cts.Token));

        try
        {
            await AsyncTestWait.WaitUntilAsync(
                () => writer.Messages.Count > 0,
                "WatchHomeFeed initial login card",
                timeout: TimeSpan.FromSeconds(5),
                cancellationToken: cts.Token);

            await WaitForPersonalSubscriptionAsync(writer, clientId, cts.Token);

            await service.Send(new SynapseEnvelope
            {
                CorrelationId = "cluster-uikit-gallery",
                TypeName = nameof(InoRequest),
                Payload = ByteString.CopyFromUtf8(JsonSerializer.Serialize(new
                {
                    prompt = "uikit gallery",
                    clientId,
                    workspaceId = WorkspaceIds.Default
                })),
            }, TestContext(cts.Token));

            await AsyncTestWait.WaitUntilAsync(
                () => writer.Messages.Any(IsGallerySurface),
                "UiKit gallery surface",
                timeout: TimeSpan.FromSeconds(10),
                cancellationToken: cts.Token);
        }
        finally
        {
            cts.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watchTask);
    }

    private async Task WaitForPersonalSubscriptionAsync(
        CapturingServerStreamWriter<RfwCardEnvelope> writer,
        string clientId,
        CancellationToken cancellationToken)
    {
        var probePrefix = "GalleryReadinessProbe-" + Guid.NewGuid().ToString("N");
        var attempt = 0;

        await AsyncTestWait.WaitUntilAsync(async () =>
        {
            var root = probePrefix + "-" + Interlocked.Increment(ref attempt);
            await HomeFeedBus.BroadcastAsync(new("digitalbrain", root, "{}", clientId), cancellationToken);
            return writer.Messages.Any(message => message.RootWidget.StartsWith(probePrefix, StringComparison.Ordinal));
        },
            "WatchHomeFeed personal subscription readiness",
            timeout: TimeSpan.FromSeconds(10),
            cancellationToken: cancellationToken);
    }

    private static bool IsGallerySurface(RfwCardEnvelope message)
    {
        if (string.IsNullOrWhiteSpace(message.DataJson))
        {
            return false;
        }

        using var doc = JsonDocument.Parse(message.DataJson);
        return doc.RootElement.TryGetProperty("surfaceId", out var surfaceId) &&
               surfaceId.GetString() == "surface.uikit.gallery";
    }
}
