using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.Core;

internal sealed class OutgoingCallerFilter : IOutgoingGrainCallFilter
{
    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        if (context.SourceContext?.GrainInstance is not Neuron neuron)
        {
            await context.Invoke().ConfigureAwait(true);
            return;
        }

        using var _ = CallerScope.For(neuron.Id);
        await context.Invoke().ConfigureAwait(true);
    }
}
