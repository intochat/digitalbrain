using DigitalBrain.Modules;
using DigitalBrain.Neurons;
using System.Reflection;

namespace DigitalBrain.Capabilities.Connections;

// The neuron types of the hosted connection neurons (06-08): every [Neuron] class of the composed DigitalBrain modules that is a
// connection. Its type is the connector id its keys name. Each carries the capabilities its module's ModuleManifest provides (05-07).
public sealed class ConnectionNeuronTypes
{
    private readonly Dictionary<string, (Type Neuron, IReadOnlyList<CapabilityId> Provides)> _connectors = new(StringComparer.Ordinal);

    public ConnectionNeuronTypes(IEnumerable<Type> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        foreach (var module in modules.Distinct())
        {
            var provides = ModuleManifest.Of(module).Provides;
            foreach (var type in module.Assembly.GetTypes())
            {
                if (type is not { IsClass: true, IsAbstract: false } || !typeof(IConnection).IsAssignableFrom(type)) { continue; }
                if (type.GetCustomAttribute<NeuronAttribute>()?.Type is not { } connector) { continue; }
                // Two modules in one assembly see the same neuron classes: the first module that provides something wins.
                if (!_connectors.TryGetValue(connector, out var known) || (known.Provides.Count == 0 && provides.Count > 0))
                { _connectors[connector] = (type, provides); }
            }
        }
        Types = _connectors.Keys.ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlySet<string> Types { get; }

    // The [Neuron] class serving connector, or null when none is hosted.
    public Type? NeuronOf(string connector) => _connectors.TryGetValue(connector, out var entry) ? entry.Neuron : null;

    // The capabilities the module of connector's neuron provides (its ModuleManifest.Provides); empty when none is hosted.
    public IReadOnlyList<CapabilityId> ProvidesOf(string connector)
        => _connectors.TryGetValue(connector, out var entry) ? entry.Provides : [];
}
