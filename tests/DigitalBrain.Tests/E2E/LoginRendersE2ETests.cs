using System.Net;
using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Tests.TestSupport;
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
    private readonly DigitalBrainAppHostFixture _fx = fixture;

    [SkippableFact]
    public async Task Login_over_grpc_web_send_broadcasts_signed_in_session()
    {
        E2EPrerequisites.RequireRealStackE2E();

        var clientId = "e2e-login-" + Guid.NewGuid().ToString("N")[..8];
        var httpHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        // gRPC-Web is an HTTP/1.1-compatible transport (that's the point — browsers don't get to
        // negotiate HTTP/2 for it either), but Grpc.Net.Client stamps every request's Version/
        // VersionPolicy for HTTP/2 before it reaches any handler, overriding an HttpClient-level
        // default. This endpoint answers a stamped-HTTP/2 gRPC-Web request with HTTP_1_1_REQUIRED
        // (see the "web" endpoint's own doc comment on DigitalBrainAppHostFixture.GatewayHttpsUrl),
        // so ForceHttp11Handler re-pins the version on the way out, after Grpc.Net.Client/GrpcWebHandler
        // have already built the request but before it reaches the socket.
        // GrpcWebMode.GrpcWebText (base64-framed), not the raw GrpcWeb mode: WatchHomeFeed below is a
        // server-streaming RPC, and the Grpc.Net.Client.Web docs call out base64 framing as required
        // for server streaming over gRPC-Web.
        var grpcWebHandler = new GrpcWebHandler(GrpcWebMode.GrpcWebText, new ForceHttp11Handler(httpHandler));
        using var channel = GrpcChannel.ForAddress(_fx.GatewayHttpsUrl, new GrpcChannelOptions { HttpHandler = grpcWebHandler });
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var feed = client.WatchHomeFeed(new WatchHomeFeedRequest { ClientId = clientId }, cancellationToken: cts.Token);
        var streamReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = ReadForSignedInAsync(feed.ResponseStream, clientId, streamReady, cts.Token);
        await AsyncTestWait.WaitUntilAsync(
            () => streamReady.Task.IsCompleted,
            "WatchHomeFeed initial login card",
            timeout: TimeSpan.FromSeconds(10),
            cancellationToken: cts.Token);
        await streamReady.Task;

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

    private static async Task<bool> ReadForSignedInAsync(IAsyncStreamReader<RfwCardEnvelope> stream, string clientId, TaskCompletionSource<bool> streamReady, CancellationToken ct)
    {
        try
        {
            while (await stream.MoveNext(ct))
            {
                streamReady.TrySetResult(true);
                var json = stream.Current.DataJson;
                if (string.IsNullOrEmpty(json))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("status", out var status) && status.GetString() == "signed-in" &&
                    doc.RootElement.TryGetProperty("clientId", out var cid) && cid.GetString() == clientId)
                {
                    return true;
                }
            }
        }
        catch (RpcException ex) { streamReady.TrySetException(ex); }
        catch (OperationCanceledException ex) { streamReady.TrySetCanceled(ex.CancellationToken); }
        return false;
    }

    private sealed class ForceHttp11Handler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
