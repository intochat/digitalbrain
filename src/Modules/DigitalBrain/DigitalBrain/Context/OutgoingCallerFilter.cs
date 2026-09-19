using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.Core;

internal sealed class OutgoingCallerFilter : IOutgoingGrainCallFilter
{
    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        if (context.SourceContext?.GrainInstance is not Neuron || context.SourceId is not { } sourceId)
        {
            await context.Invoke().ConfigureAwait(true);
            return;
        }

        var caller = NeuronId.FromGrainId(sourceId);
        using var _ = CallerScope.For(caller);
        await context.Invoke().ConfigureAwait(true);
    }
}
