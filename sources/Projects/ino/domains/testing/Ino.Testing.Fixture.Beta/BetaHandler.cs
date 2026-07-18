using Ino.Core;
using Ino.Core.Hosting;
using Ino.Testing.Fixtures.BetaContracts;
using Orleans;

namespace Ino.Testing.Fixture;

public sealed class BetaHandler : Grain, INeuron<PingBeta>
{
    public Task<NeuronResult> HandleAsync(PingBeta synapse, NeuronContext ctx, CancellationToken ct)
    {
        return Task.FromResult(NeuronResult.Ok().With(new PingResponse($"pong from beta: {synapse.Message}")));
    }
}
