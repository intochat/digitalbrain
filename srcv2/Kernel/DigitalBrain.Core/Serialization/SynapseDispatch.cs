using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

internal static class SynapseDispatch
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<Type, HandlerInvoker>> HandlersByNeuronType = new();
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<string>> AliasesByNeuronType = new();

    internal delegate Task HandlerInvoker(object neuron, Synapse synapse, CancellationToken cancellationToken);

    internal static IReadOnlyDictionary<Type, HandlerInvoker> HandlersFor(Type neuronType)
        => HandlersByNeuronType.GetOrAdd(neuronType, static type => Build(type));

    // Named in a refusal so the sender learns what this neuron would have accepted.
    internal static IReadOnlyList<string> HandledAliases(Type neuronType)
        => AliasesByNeuronType.GetOrAdd(
            neuronType,
            static type => [
                .. HandlersFor(type).Keys
                    .Select(static synapse => SynapseAlias.Of(synapse) ?? synapse.Name)
                    .OrderBy(static alias => alias, StringComparer.Ordinal)]);

    private static Dictionary<Type, HandlerInvoker> Build(Type neuronType)
    {
        var handlers = new Dictionary<Type, HandlerInvoker>();

        foreach (var handled in neuronType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IHandle<>)))
        {
            var synapseType = handled.GetGenericArguments()[0];
            var handleMethod = handled.GetMethod(nameof(IHandle<>.HandleAsync))
                ?? throw new MissingMethodException(handled.FullName, nameof(IHandle<>.HandleAsync));

            handlers[synapseType] = (neuron, synapse, cancellationToken) => (Task)handleMethod.Invoke(
                neuron,
                BindingFlags.DoNotWrapExceptions,
                binder: null,
                [synapse, cancellationToken],
                culture: null)!;
        }

        return handlers;
    }
}
