using System.Collections.Concurrent;
using System.Reflection;

namespace DigitalBrain;

internal static class SynapseDispatch
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<Type, HandlerInvoker>> HandlersByNeuronType = new();

    internal delegate Task HandlerInvoker(object neuron, Synapse synapse, CancellationToken cancellationToken);

    internal static IReadOnlyDictionary<Type, HandlerInvoker> HandlersFor(Type neuronType)
        => HandlersByNeuronType.GetOrAdd(neuronType, static type => Build(type));

    private static Dictionary<Type, HandlerInvoker> Build(Type neuronType)
    {
        var handlers = new Dictionary<Type, HandlerInvoker>();

        foreach (var handled in neuronType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IHandle<>)))
        {
            var synapseType = handled.GetGenericArguments()[0];
            var handleMethod = handled.GetMethod(nameof(IHandle<Synapse>.HandleAsync))
                ?? throw new MissingMethodException(handled.FullName, nameof(IHandle<Synapse>.HandleAsync));

            handlers[synapseType] = (neuron, synapse, cancellationToken)
                => (Task)handleMethod.Invoke(neuron, [synapse, cancellationToken])!;
        }

        return handlers;
    }
}
