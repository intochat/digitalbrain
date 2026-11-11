namespace DigitalBrain.Neurons;

/// <summary>One kernel service bound to the neuron it is injected into: that neuron's brain, and that neuron as origin or requester.</summary>
public sealed record NeuronBinding(Type Service, Func<IServiceProvider, NeuronId, object> Bind);

/// <summary>What a neuron's constructor resolves from: the bindings first, each built once per activation, then the host's services.</summary>
public sealed class NeuronServices(IServiceProvider host, NeuronId neuron, IEnumerable<NeuronBinding> bindings) : IServiceProvider
{
    private readonly NeuronBinding[] _bindings = [.. bindings];
    private readonly Dictionary<Type, object> _bound = [];

    /// <inheritdoc />
    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        lock (_bound)
        {
            if (_bound.TryGetValue(serviceType, out var bound)) { return bound; }
            foreach (var binding in _bindings)
            {
                if (binding.Service == serviceType)
                {
                    return _bound[serviceType] = binding.Bind(host, neuron);
                }
            }
        }

        return host.GetService(serviceType);
    }
}
