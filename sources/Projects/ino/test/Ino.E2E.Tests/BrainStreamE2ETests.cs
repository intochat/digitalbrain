using System.Threading.Channels;
using Grpc.Core;
using Grpc.Net.Client;
using Ino.Grpc;
using Ino.Testing;
using Xunit;

namespace Ino.E2E.Tests;

/// <summary>
/// Boots the real Aspire AppHost and verifies the full brain trace wire:
/// <c>AskIno</c> → <c>BrainTraceFilter</c> emits → Orleans stream →
/// <c>WatchBrainActivity</c> gRPC subscription → client receives pulse.
///
/// Uses <see cref="InoE2ECollection"/> (the same shared AppHost that
/// <see cref="InstallFlowTests"/> and <see cref="DiscoveryTableEndpointTests"/>
/// use) so the cluster starts once for the whole collection.
/// </summary>
[Collection(nameof(InoE2ECollection))]
public sealed class BrainStreamE2ETests(InoTestAppHost<Projects.Ino_AppHost> fixture)
{
    [Fact]
    public async Task WatchBrainActivity_emits_pulse_for_AskIno_call()
    {
        // Derive the kernel HTTPS base URL from the shared HttpClient the fixture
        // already knows how to produce — avoids calling GetEndpoint directly.
        using var http = fixture.CreateKernelHttpClient();
        var kernelUrl = http.BaseAddress!.ToString().TrimEnd('/');

        using var channel = GrpcChannel.ForAddress(kernelUrl, new GrpcChannelOptions
        {
            HttpHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            },
        });

        var ino = new global::Ino.Grpc.Ino.InoClient(channel);

        // Open the stream BEFORE driving traffic so we don't race the silo.
        var pulses = Channel.CreateUnbounded<BrainPulseProto>();
        var watchCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var watch = ino.WatchBrainActivity(
            new BrainWatchRequest { UserIdFilter = "alice", SessionIdFilter = "default" },
            cancellationToken: watchCts.Token);

        var pump = Task.Run(async () =>
        {
            try
            {
                var reader = watch.ResponseStream;
                while (await reader.MoveNext(watchCts.Token))
                {
                    pulses.Writer.TryWrite(reader.Current);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { /* stream closed */ }
            catch { /* stream cancelled at end of test */ }
        }, TestContext.Current.CancellationToken);

        // Brief warm-up so the silo's Orleans stream subscription is alive.
        await Task.Delay(500, TestContext.Current.CancellationToken);

        var ask = await ino.AskInoAsync(
            new AskInoRequest { Prompt = "ping", UserId = "alice", SessionId = "default" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(ask);

        // Drain pulses until we see one with a non-empty payload, or the deadline passes.
        // The first pulse(s) may come from internal grain calls with no payload.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        var pulsesSeen = 0;
        var sawPayload = false;
        BrainPulseProto? lastPulse = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var oneCts = CancellationTokenSource.CreateLinkedTokenSource(watchCts.Token);
            oneCts.CancelAfter(TimeSpan.FromMilliseconds(500));
            BrainPulseProto pulse;
            try { pulse = await pulses.Reader.ReadAsync(oneCts.Token); }
            catch (OperationCanceledException) { continue; }
            pulsesSeen++;
            lastPulse = pulse;
            if (!string.IsNullOrEmpty(pulse.PayloadJson))
            {
                Assert.Equal("alice", pulse.UserId);
                Assert.Equal("default", pulse.InoInstanceId);
                sawPayload = true;
                break;
            }
        }
        Assert.True(sawPayload,
            $"expected at least one BrainPulseProto.PayloadJson to be populated; saw {pulsesSeen} empty pulses");

        watchCts.Cancel();
        pulses.Writer.TryComplete();
        await pump;
    }
}
