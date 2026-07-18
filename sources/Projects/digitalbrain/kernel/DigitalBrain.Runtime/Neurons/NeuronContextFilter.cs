using Orleans.Streams;

namespace DigitalBrain.Runtime.Neurons;

/// <summary>
/// Intercepts incoming grain calls to Neuron grains targeting IAsyncObserver&lt;Synapse&gt;.OnNextAsync
/// and ambiently manages the NeuronContext.Value (Synapse context) for downstream logic.
/// </summary>
public sealed class NeuronContextFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is Neuron &&
            context.InterfaceMethod.Name == nameof(IAsyncObserver<Synapse>.OnNextAsync) &&
            context.Request.GetArgumentCount() > 0 &&
            context.Request.GetArgument(0) is Synapse synapse)
        {
            try
            {
                NeuronContext.Value = synapse;
                await context.Invoke();
            }
            finally
            {
                NeuronContext.Value = null;
            }
        }
        else
        {
            await context.Invoke();
        }
    }
}
