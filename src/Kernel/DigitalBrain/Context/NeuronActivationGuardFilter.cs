namespace DigitalBrain.Core;

internal sealed class NeuronActivationGuardFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is Neuron neuron)
        {
            await neuron.GuardActivationAsync().ConfigureAwait(true);
        }

        await context.Invoke().ConfigureAwait(true);
    }
}
