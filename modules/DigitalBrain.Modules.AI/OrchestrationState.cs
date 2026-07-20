namespace DigitalBrain.AI;

internal sealed record OrchestrationParticipant(
    string Contract,
    string NeuronId,
    string AgentId,
    string AgentName);

internal sealed record OrchestrationDefinition(
    int FormatVersion,
    string MafVersion,
    string Fingerprint,
    IReadOnlyList<OrchestrationParticipant> Participants,
    string HostId,
    string HostName);

internal sealed record OrchestrationState(
    int FormatVersion,
    string MafVersion,
    string Fingerprint,
    IReadOnlyList<OrchestrationParticipant> Participants,
    byte[] ProtectedSession);
