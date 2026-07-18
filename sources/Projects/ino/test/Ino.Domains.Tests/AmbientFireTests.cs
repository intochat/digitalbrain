using Ino.Core;
using Ino.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ino.Domains.Tests;

public class AmbientFireTests
{
    public sealed record TestSynapse : ISynapse;

    private sealed class RecordingFirePort : IFirePort
    {
        public NeuronContext? LastContext { get; private set; }

        public Task<NeuronResult> Fire<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse
        {
            LastContext = caller;
            return Task.FromResult(NeuronResult.Ok());
        }

        public Task FireBroadcast<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse
        {
            LastContext = caller;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FireAsync_builds_context_with_Ambient_caller_and_silo()
    {
        var firePort = new RecordingFirePort();
        var ambient = new AmbientFire(firePort, DomainId.From("domains"), NullLogger<AmbientFire>.Instance);

        await ambient.FireAsync(new TestSynapse(), ct: TestContext.Current.CancellationToken);

        var caller = Assert.IsType<Caller.Ambient>(firePort.LastContext!.Source);
        Assert.Equal(DomainId.From("domains"), caller.Domain);
    }

    [Fact]
    public async Task FireAsync_generates_fresh_correlation_when_none_supplied()
    {
        var firePort = new RecordingFirePort();
        var ambient = new AmbientFire(firePort, DomainId.From("kernel"), NullLogger<AmbientFire>.Instance);

        await ambient.FireAsync(new TestSynapse(), ct: TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrEmpty(firePort.LastContext!.CorrelationId.Value));
    }

    [Fact]
    public async Task FireAsync_preserves_supplied_correlation_id()
    {
        var firePort = new RecordingFirePort();
        var ambient = new AmbientFire(firePort, DomainId.From("kernel"), NullLogger<AmbientFire>.Instance);
        var supplied = CorrelationId.New();

        await ambient.FireAsync(new TestSynapse(), correlationId: supplied, ct: TestContext.Current.CancellationToken);

        Assert.Equal(supplied, firePort.LastContext!.CorrelationId);
    }
}
