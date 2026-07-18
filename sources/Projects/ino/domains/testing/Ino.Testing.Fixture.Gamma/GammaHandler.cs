using Ino.Core;
using Ino.Core.Hosting;
using Ino.Testing.Fixtures.GammaContracts;
using Orleans;

namespace Ino.Testing.Fixture;

public sealed class GammaHandler : Grain, INeuron<PingGamma>
{
    public Task<NeuronResult> HandleAsync(PingGamma synapse, NeuronContext ctx, CancellationToken ct)
    {
        return Task.FromResult(NeuronResult.Ok());
    }
}
