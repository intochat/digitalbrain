using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

internal sealed class ParticipantInvocations(IReadOnlyList<NeuronId> participants)
{
    private int _invoked;

    internal void RecordInvocation() => Interlocked.Increment(ref _invoked);

    internal void RequireAnyInvoked(NeuronId orchestration)
    {
        if (Volatile.Read(ref _invoked) > 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"AI orchestration '{orchestration}' reached none of its participants: {string.Join(", ", participants)}. No session was persisted, so this orchestration can still be pointed at participants that can run.");
    }
}
