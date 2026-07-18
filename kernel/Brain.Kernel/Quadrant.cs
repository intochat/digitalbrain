using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Brain.Contracts;

namespace Brain.Kernel;

public sealed class Quadrant
{
    private IReadOnlyDictionary<Type, Type> _neurons =
        new ReadOnlyDictionary<Type, Type>(new Dictionary<Type, Type>());

    private int _loaded;

    public IReadOnlyDictionary<Type, Type> Neurons => _neurons;

    public Type GetImplementation<TNeuron>()
        where TNeuron : INeuron
    {
        if (!_neurons.TryGetValue(typeof(TNeuron), out var implementation))
        {
            throw new InvalidOperationException(
                $"No neuron implementation is registered for contract '{typeof(TNeuron).FullName}'.");
        }

        return implementation;
    }

    public void Load(IEnumerable<NeuronRegistration> registrations)
    {
        if (Interlocked.Exchange(ref _loaded, 1) != 0)
        {
            throw new InvalidOperationException("Quadrant has already been loaded.");
        }

        var map = ImmutableDictionary.CreateBuilder<Type, Type>();
        foreach (var registration in registrations
            .OrderBy(item => item.Contract.FullName, StringComparer.Ordinal))
        {
            map.Add(registration.Contract, registration.Implementation);
        }

        _neurons = map.ToImmutable();
    }
}
