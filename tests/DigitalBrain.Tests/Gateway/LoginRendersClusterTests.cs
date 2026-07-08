using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Tests.TestSupport;
using Google.Protobuf;

namespace DigitalBrain.Tests.Gateway;

// Proves the useful behavior behind the former AppHost E2E test without booting containers:
// Gateway Send(LoginRequest) reaches the real Orleans session grain, and WatchHomeFeed receives the
// signed-in surface on the requesting client's stream.
[Trait("Category", "cluster")]
public sealed class LoginRendersClusterTests : GatewayClusterTestBase
{
    [Fact]
    public async Task Send_LoginRequest_Broadcasts_Signed_In_Session_To_WatchHomeFeed()
    {
        var service = NewGatewayService();
        var clientId = "cluster-login-" + Guid.NewGuid().ToString("N")[..8];

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
                CorrelationId = "cluster-login",
                TypeName = nameof(LoginRequest),
                Payload = ByteString.CopyFromUtf8(JsonSerializer.Serialize(new
                {
                    username = "cluster-login-user",
                    password = "correct horse battery staple",
                    clientId,
                })),
            }, TestContext(cts.Token));

            await AsyncTestWait.WaitUntilAsync(
                () => writer.Messages.Any(message => IsSignedInForClient(message, clientId)),
                "signed-in session card",
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
        var probePrefix = "LoginReadinessProbe-" + Guid.NewGuid().ToString("N");
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

    private static bool IsSignedInForClient(RfwCardEnvelope message, string clientId)
    {
        if (string.IsNullOrWhiteSpace(message.DataJson))
        {
            return false;
        }

        using var doc = JsonDocument.Parse(message.DataJson);
        return doc.RootElement.TryGetProperty("status", out var status) &&
               status.GetString() == "signed-in" &&
               doc.RootElement.TryGetProperty("clientId", out var cid) &&
               cid.GetString() == clientId;
    }
}
