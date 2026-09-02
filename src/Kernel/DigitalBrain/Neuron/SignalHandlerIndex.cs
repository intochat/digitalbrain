using System.Collections.Concurrent;
using System.Reflection;

using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

// Tier 1 of routing (spec 5): which neuron GRAIN TYPES declare IHandle<TSignal>. Innate, free,
// and never wrong. Built by reflection for this slice; D9 replaces it with a source generator,
// which is also what removes the assembly scan from startup.
public sealed class SignalHandlerIndex
{
    private readonly ConcurrentDictionary<Type, IReadOnlyList<string>> _receivers = new();

    public IReadOnlyList<string> ReceiversOf(Type signalType)
    {
        ArgumentNullException.ThrowIfNull(signalType);

        return _receivers.GetOrAdd(signalType, static type =>
        {
            var handler = typeof(IHandle<>).MakeGenericType(type);

            return
            [
                .. AppDomain.CurrentDomain.GetAssemblies()
                    .Where(static assembly => !assembly.IsDynamic)
                    .SelectMany(SafeTypes)
                    .Where(candidate =>
                        candidate is { IsClass: true, IsAbstract: false }
                        && typeof(INeuron).IsAssignableFrom(candidate)
                        && handler.IsAssignableFrom(candidate))
                    .Select(NeuronId.GrainTypeNameOf)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
            ];
        });
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            return partial.Types.OfType<Type>();
        }
    }
}
