using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

internal sealed class ParticipantInvocations(IReadOnlyList<NeuronId> participants)
{
    private readonly Lock _gate = new();
    private readonly List<NeuronId> _failed = [];
    private int _invoked;
    private Exception? _firstFailure;

    internal void RecordInvocation()
    {
        lock (_gate)
        {
            _invoked++;
        }
    }

    internal void RecordFailure(NeuronId participant, Exception failure)
    {
        lock (_gate)
        {
            _failed.Add(participant);
            _firstFailure ??= failure;
        }
    }

    internal void RequireAnyInvoked(NeuronId orchestration)
    {
        lock (_gate)
        {
            if (_invoked > 0)
            {
                return;
            }

            throw new InvalidOperationException(WhyNothingRan(orchestration), _firstFailure);
        }
    }

    private string WhyNothingRan(NeuronId orchestration)
    {
        List<string> reported =
        [
            $"AI orchestration '{orchestration}' answered nothing because none of its participants ran.",
        ];

        if (_failed.Count > 0)
        {
            reported.Add($"Failed to run: {Label(_failed)}. Why they failed is this exception's InnerException.");
        }

        var neverReached = participants.Where(participant => !_failed.Contains(participant)).ToArray();

        if (neverReached.Length > 0)
        {
            reported.Add(
                $"Never given a turn: {Label(neverReached)}. Nothing is known to be wrong with these, so do not report them as broken.");
        }

        reported.Add(
            "No session was persisted, so this orchestration can be pointed at participants that can run and retried.");

        return string.Join(" ", reported);
    }

    private static string Label(IEnumerable<NeuronId> subjects)
        => string.Join(", ", subjects.Select(ModelContracts.LabelFor));
}
