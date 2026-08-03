using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors.Runtime;

// Process-local attempt CTS registry — never durable, never journaled.
internal static class BehaviorAttemptCancellation
{
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> Sources =
        new(StringComparer.Ordinal);

    public static CancellationTokenSource Rent(NeuronId task, AttemptId attempt)
    {
        var key = Key(task, attempt);
        var created = new CancellationTokenSource();
        if (Sources.TryRemove(key, out var prior))
        {
            prior.Cancel();
            prior.Dispose();
        }

        Sources[key] = created;
        return created;
    }

    public static void Cancel(NeuronId task, AttemptId attempt)
    {
        if (Sources.TryGetValue(Key(task, attempt), out var source))
        {
            source.Cancel();
        }
    }

    public static void Release(NeuronId task, AttemptId attempt)
    {
        if (Sources.TryRemove(Key(task, attempt), out var source))
        {
            source.Dispose();
        }
    }

    public static CancellationToken TokenOrNone(NeuronId task, AttemptId attempt)
        => Sources.TryGetValue(Key(task, attempt), out var source)
            ? source.Token
            : CancellationToken.None;

    private static string Key(NeuronId task, AttemptId attempt)
        => $"{task.Owner.Value}\u001f{task.Type}\u001f{task.Name}\u001f{attempt.Value:N}";
}
