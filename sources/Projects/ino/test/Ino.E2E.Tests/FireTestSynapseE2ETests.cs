using System.Threading.Channels;
using Grpc.Core;
using Grpc.Net.Client;
using Ino.Grpc;
using Ino.Testing;
using Xunit;

namespace Ino.E2E.Tests;

[Collection(nameof(InoE2ECollection))]
public sealed class FireTestSynapseE2ETests(InoTestAppHost<Projects.Ino_AppHost> fixture)
{
    [Fact(Skip = "Slice C.4 deferred: server-side FireTestSynapse handler NREs because the reflective IFirePort.Fire<T> path is incomplete; tracked for slice C.5.")]
    public async Task FireTestSynapse_emits_a_brain_pulse_for_the_target_synapse()
    {
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

        var pulses = Channel.CreateUnbounded<BrainPulseProto>();
        var watchCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var watch = ino.WatchBrainActivity(new BrainWatchRequest(), cancellationToken: watchCts.Token);
        var pump = Task.Run(async () =>
        {
            try
            {
                while (await watch.ResponseStream.MoveNext(watchCts.Token))
                {
                    pulses.Writer.TryWrite(watch.ResponseStream.Current);
                }
            }
            catch { /* stream cancelled */ }
        }, TestContext.Current.CancellationToken);

        await ino.FireTestSynapseAsync(new FireTestSynapseRequest
        {
            SynapseType = "ChatIntent",
            PayloadJson = "{\"text\":\"hello from test\",\"userId\":\"alice\"}",
        }, cancellationToken: TestContext.Current.CancellationToken);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        var sawIt = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var oneCts = CancellationTokenSource.CreateLinkedTokenSource(watchCts.Token);
            oneCts.CancelAfter(TimeSpan.FromMilliseconds(500));
            try
            {
                var pulse = await pulses.Reader.ReadAsync(oneCts.Token);
                if (pulse.PayloadJson.Contains("hello from test"))
                {
                    sawIt = true;
                    break;
                }
            }
            catch (OperationCanceledException) { /* try again */ }
        }
        Assert.True(sawIt, "expected a brain pulse carrying the test payload to arrive");

        watchCts.Cancel();
        try { await pump; } catch { /* ignored */ }
    }
}
