using Orleans.Serialization;

namespace DigitalBrain;

internal sealed class CoreWireTypeFilter : ITypeFilter
{
    private static readonly HashSet<Type> CoreWireTypes =
    [
        typeof(Neuron.ITransport), typeof(Neuron.IDrainEntry),
        typeof(Neuron.ISessionEntry), typeof(IOutboxWakeup),
    ];

    public bool? IsTypeAllowed(Type type) => CoreWireTypes.Contains(type) ? true : null;
}
