using System.Text.Json;

namespace DigitalBrain.Poc.Runtime;

internal sealed class RunDocument
{
    public HashSet<string> AcknowledgedReceipts { get; set; } = new(StringComparer.Ordinal);

    public List<JournalEntry> Journal { get; set; } = [];

    public List<OutboxEntry> Outbox { get; set; } = [];

    public List<CandidateModuleBinding> CandidateModuleBindings { get; set; } = [];

    public Dictionary<string, JsonElement> States { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> HandledCounts { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<string>> TrustedInputDeliveries { get; set; } =
        new(StringComparer.Ordinal);
}
