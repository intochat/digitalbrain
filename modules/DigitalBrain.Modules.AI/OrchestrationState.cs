using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

[GenerateSerializer]
[Alias("ai.orchestration-participant")]
internal sealed record OrchestrationParticipant(
    [property: Id(0)] string Contract,
    [property: Id(1)] NeuronId NeuronId,
    [property: Id(2)] string AgentId,
    [property: Id(3)] string AgentName);

[GenerateSerializer]
[Alias("ai.orchestration-definition")]
internal sealed record OrchestrationDefinition(
    [property: Id(0)] int FormatVersion,
    [property: Id(1)] string MafVersion,
    [property: Id(2)] string Fingerprint,
    [property: Id(3)] OrchestrationParticipant[] Participants,
    [property: Id(4)] string HostId,
    [property: Id(5)] string HostName);

internal sealed record OrchestrationState(
    int FormatVersion,
    string MafVersion,
    string Fingerprint,
    IReadOnlyList<OrchestrationParticipant> Participants,
    byte[] ProtectedSession);
