using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

internal static class OrchestrationParticipants
{
    internal static Participant[] Snapshot(
        NeuronId orchestration,
        IReadOnlyList<Participant>? participants)
    {
        if (participants is null)
        {
            throw new InvalidOperationException("Participants returned null.");
        }

        var snapshot = participants.ToArray();

        if (snapshot.Length == 0)
        {
            throw new InvalidOperationException(
                $"AI orchestration '{orchestration}' requires at least one participant.");
        }

        if (snapshot.Any(participant => participant is null))
        {
            throw new InvalidOperationException(
                $"AI orchestration '{orchestration}' has a null participant.");
        }

        if (snapshot.Any(participant => participant.Id.Owner != orchestration.Owner))
        {
            throw new InvalidOperationException(
                $"Every participant in AI orchestration '{orchestration}' must belong to its owner.");
        }

        return snapshot;
    }
}
