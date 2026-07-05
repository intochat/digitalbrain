using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;

namespace DigitalBrain.Tests.E2E;

// Regression guard for the gRPC-Web action-dispatch fix: the browser uses gRPC-Web (no client/bidi
// streaming), so kit/form actions must travel over the UNARY Send RPC, not the bidirectional
// EngageUiSession. This drives a real login over a real GrpcWebHandler-wrapped channel (the same
// transport wrapper a browser's gRPC-Web fetch implementation uses) against the real Aspire-hosted
// kernel, and asserts the server-side signed-in broadcast reaches WatchHomeFeed.
//
// Narrower than before: this no longer exercises Flutter's own dispatch code, so it can't catch a
// regression where the Flutter login button starts calling EngageUiSession again — only Flutter's own
// widget/unit tests could catch that, and those are intentionally scoped to ui_kit only (see
// docs/superpowers/specs/2026-07-05-e2e-testing-without-playwright-design.md). What this still proves:
// the server's Send/LoginRequest path works end-to-end over the gRPC-Web transport.
[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class LoginRendersE2ETests(DigitalBrainAppHostFixture fixture)
{
    readonly DigitalBrainAppHostFixture _fx = fixture;

    [SkippableFact]
    public async Task Login_over_grpc_web_send_broadcasts_signed_in_session()
    {
        E2EPrerequisites.RequireRealStackE2E();

        var clientId = "e2e-login-" + Guid.NewGuid().ToString("N")[..8];
        var httpHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        var grpcWebHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, httpHandler);
        using var channel = GrpcChannel.ForAddress(_fx.GatewayHttpsUrl, new GrpcChannelOptions { HttpHandler = grpcWebHandler });
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var feed = client.WatchHomeFeed(new WatchHomeFeedRequest { ClientId = clientId }, cancellationToken: cts.Token);
        var delivered = ReadForSignedInAsync(feed.ResponseStream, clientId, cts.Token);
        await Task.Delay(750, cts.Token);

        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = "e2e-login",
            TypeName = nameof(LoginRequest),
            Payload = ByteString.CopyFromUtf8(JsonSerializer.Serialize(new
            {
                username = "e2e-admin",
                password = "e2e-password",
                clientId,
            })),
        }, cancellationToken: cts.Token);

        Assert.True(await delivered, "Signed-in session broadcast was not delivered to WatchHomeFeed");
    }

    static async Task<bool> ReadForSignedInAsync(IAsyncStreamReader<RfwCardEnvelope> stream, string clientId, CancellationToken ct)
    {
        try
        {
            while (await stream.MoveNext(ct))
            {
                var json = stream.Current.DataJson;
                if (string.IsNullOrEmpty(json)) continue;
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("status", out var status) && status.GetString() == "signed-in" &&
                    doc.RootElement.TryGetProperty("clientId", out var cid) && cid.GetString() == clientId)
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
