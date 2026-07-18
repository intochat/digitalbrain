using Ino.Core.Capabilities;

namespace Ino.Core;

// Marker for the InoNeuron's journal event union. v0.1 carries routing
// outcomes so the activation can replay its history; future slices add
// CreatedNeuron, ToolCalled etc. once Creator and the LlmNeuron rewrite land.
[GenerateSerializer]
public abstract record InoJournalEvent : ISynapse;

[GenerateSerializer]
public sealed record InoAsked(
    [property: Id(0)] string Prompt,
    [property: Id(1)] string SessionId,
    [property: Id(2)] DateTimeOffset At) : InoJournalEvent;

[GenerateSerializer]
public sealed record InoRouted(
    [property: Id(0)] string Prompt,
    [property: Id(1)] string NeuronId,
    [property: Id(2)] RoutingSource Source,
    [property: Id(3)] DateTimeOffset At) : InoJournalEvent;
