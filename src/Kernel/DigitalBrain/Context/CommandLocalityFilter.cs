namespace DigitalBrain.Core;

internal sealed class CommandLocalityFilter : IOutgoingGrainCallFilter
{
    public Task Invoke(IOutgoingGrainCallContext context)
    {
        if (context.SourceContext?.GrainInstance is Neuron { IsExecutingCommand: true } neuron)
        {
            throw new InvalidOperationException(
                $"Neuron '{neuron.Id}' cannot call '{context.InterfaceName}.{context.MethodName}': commands are local. Schedule work and call from the reaction.");
        }

        return context.Invoke();
    }
}
