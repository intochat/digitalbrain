using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;
using Google.Protobuf;
using Grpc.Core;

namespace DigitalBrain.Tests.E2E;

// Reproduces the desktop client transport: native gRPC (not gRPC-Web) over the proxied "grpc" endpoint,
// with WatchHomeFeed and an InoRequest Send on the SAME channel.
// Asserts a current server-driven UiKit gallery surface is delivered back to the streaming client.
[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class NativeGrpcGalleryDeliveryE2ETests(DigitalBrainAppHostFixture fixture)
{
    private readonly DigitalBrainAppHostFixture _fx = fixture;

    [SkippableFact]
    public async Task Ino_uikit_gallery_surface_is_delivered_over_native_grpc()
    {
        Skip.IfNot(_fx.Ready, E2EPrerequisites.SkipReason);

        var clientId = "e2e-gallery-" + Guid.NewGuid().ToString("N")[..8];
        using var channel = _fx.CreateGatewayGrpcChannel();
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var feed = client.WatchHomeFeed(new WatchHomeFeedRequest { ClientId = clientId }, cancellationToken: cts.Token);

        var delivered = ReadForGallerySurfaceAsync(feed.ResponseStream, cts.Token);

        // Subscribe-before-emit: give the stream a moment to register, then fire start on the SAME channel.
        await Task.Delay(750, cts.Token);
        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = "native-grpc-uikit-gallery",
            TypeName = nameof(InoRequest),
            Payload = ByteString.CopyFromUtf8(JsonSerializer.Serialize(new
            {
                prompt = "uikit gallery",
                clientId,
                workspaceId = WorkspaceIds.Default
            })),
        }, cancellationToken: cts.Token);

        Assert.True(await delivered, "UiKit gallery surface was not delivered to the native-gRPC WatchHomeFeed stream");
    }

    private static async Task<bool> ReadForGallerySurfaceAsync(IAsyncStreamReader<RfwCardEnvelope> stream, CancellationToken ct)
    {
        try
        {
            while (await stream.MoveNext(ct))
            {
                var json = stream.Current.DataJson;
                if (string.IsNullOrEmpty(json))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("surfaceId", out var surfaceId) &&
                    surfaceId.GetString() == "surface.uikit.gallery")
                {
                    return true;
                }
            }
        }
        catch (RpcException) { }
        catch (OperationCanceledException) { }
        return false;
    }
}
