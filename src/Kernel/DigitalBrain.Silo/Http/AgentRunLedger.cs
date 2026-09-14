using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Kernel;

// What a run already answered, so the shell's one reconnect costs no second model call and appends no
// second copy of the turn to the session. In-process and bounded on purpose: after a promotion the chat
// lands on a silo that never saw this run, and there the resume is simply a fresh run.
internal sealed class AgentRunLedger
{
    private const int Capacity = 16;
    private const int MaxAnswer = 64 * 1024;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    public bool TryGetAnswer(string runId, [NotNullWhen(true)] out string? answer)
    {
        lock (_gate)
        {
            return _answers.TryGetValue(runId, out answer);
        }
    }

    public void Record(string runId, string answer)
    {
        if (runId.Length == 0 || answer.Length == 0 || answer.Length > MaxAnswer)
        {
            return;
        }

        lock (_gate)
        {
            if (!_answers.TryAdd(runId, answer))
            {
                return;
            }

            _order.Enqueue(runId);
            while (_order.Count > Capacity)
            {
                _answers.Remove(_order.Dequeue());
            }
        }
    }
}
