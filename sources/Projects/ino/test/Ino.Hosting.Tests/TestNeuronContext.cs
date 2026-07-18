using Ino.Core;
using Ino.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ino.Hosting.Tests;

internal static class TestNeuronContext
{
    private sealed class NoOpFire : IFirePort
    {
        public Task<NeuronResult> Fire<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse
            => Task.FromResult(NeuronResult.Ok());

        public Task FireBroadcast<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse
            => Task.CompletedTask;
    }

    public static NeuronContext New() =>
        new(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.Ambient(DomainId.From("kernel")),
            SourceStream: new StreamKey("<test>"))
        {
            FirePort = new NoOpFire(),
            Logger = NullLogger.Instance,
        };
}
