using Orleans.Serialization;

namespace DigitalBrain;

internal sealed class HostingWireTypeFilter : ITypeFilter
{
    private static readonly HashSet<Type> HostingWireTypes =
    [
        typeof(INeuronHost), typeof(IOutboxWakeup),
        typeof(NeuronId),
        typeof(SynapseOrigin),
        typeof(SynapseReference),
        typeof(JournalRecordDirection),
        typeof(JournalRecord),
        typeof(JournalRead),
        typeof(JournalPage),
        typeof(JournalHistoryUnavailable),
    ];

    public bool? IsTypeAllowed(Type type) => HostingWireTypes.Contains(type) ? true : null;
}
