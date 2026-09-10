using System.Text.Json;

namespace DigitalBrain.Mcp;

public sealed record DescribeRequest(string? Neuron = null, string? Interface = null, string? Method = null);

public sealed record CallRequest(string Neuron, string Interface, string Method, JsonElement Arguments);

public sealed record FireRequest(string Type, string Body, string? To = null, string? Correlation = null);

public sealed record FireResult(string SignalId, string Correlation, int Delivered, int Busy);

public sealed record CancelRequest(string Neuron, string Signal);

public sealed record ConnectRequest(string From, string To, string Type);

public sealed record ReadRequest(string Neuron, string? What = null, long After = 0, int TimeoutSeconds = 0);

public sealed record StateEntry(string Type, string Body, string From, DateTimeOffset At);

public sealed record SynapseEntry(string From, string To, string Type);

public sealed record JournalEntryView(long Sequence, string Type, string Body, string From, string SignalId, string Correlation, DateTimeOffset At);

public sealed record JournalView(long ResumeSequence, IReadOnlyList<JournalEntryView> Entries, long TotalRecorded);

public sealed record ReadResult(string Neuron, IReadOnlyList<StateEntry>? State, IReadOnlyList<SynapseEntry>? Synapses, JournalView? Incoming, JournalView? Outgoing, CommandsView? Commands);

public sealed record CommandEntryView(long Sequence, string Command, int Incarnation, string Interface,
    string Method, string Phase, string Caller, string? Error, DateTimeOffset At);

public sealed record CommandsView(long ResumeSequence, long EarliestRetained, bool Gap,
    IReadOnlyList<CommandEntryView> Entries);
