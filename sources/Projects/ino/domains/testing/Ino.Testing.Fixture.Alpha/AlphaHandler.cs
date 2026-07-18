using Ino.Core;
using Ino.Core.Hosting;
using Ino.Testing.Fixtures.AlphaContracts;
using Ino.Testing.Fixtures.BetaContracts;
using Orleans;

namespace Ino.Testing.Fixture;

public sealed class AlphaHandler : Grain, INeuron<PingAlpha>
{
    public async Task<NeuronResult> HandleAsync(PingAlpha synapse, NeuronContext ctx, CancellationToken ct)
    {
        var betaResult = await ctx.Fire(new PingBeta(synapse.Message), ct);

        var betaMessage = betaResult.TryGetPayload<PingResponse>(out var pong)
            ? pong!.Text
            : "(beta unreachable)";

        var aggregated = $"alpha heard '{synapse.Message}' + {betaMessage}";
        return NeuronResult.Ok().With(new PingAlphaResponse(aggregated));
    }
}
