using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.Core;

internal sealed class OutgoingCallerFilter : IOutgoingGrainCallFilter
{
    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        if (context.SourceContext?.GrainInstance is not Neuron neuron || context.SourceId is not { } sourceId)
        {
            await context.Invoke().ConfigureAwait(true);
            return;
        }

        if (neuron.ExecutingCommand is not null)
        {
            throw new InvalidOperationException(
                $"Neuron '{neuron.Id}' cannot call '{context.InterfaceName}.{context.MethodName}': commands are local. Schedule work and call from the reaction.");
        }

        var caller = NeuronId.FromGrainId(sourceId);
        using var _ = CallerScope.For(caller);
        await context.Invoke().ConfigureAwait(true);
    }
}
