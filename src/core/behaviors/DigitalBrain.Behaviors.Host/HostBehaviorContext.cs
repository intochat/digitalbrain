using System.Collections.Concurrent;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Behaviors;

internal sealed class HostBehaviorContext(
    BehaviorExecutionMetadata execution,
    IBehaviorCapabilityResolver capabilities,
    TimeProvider time,
    CancellationToken attemptCancellation) : IBehaviorContext
{
    private readonly ConcurrentDictionary<string, object?> _state = new(StringComparer.Ordinal);

    public BehaviorExecutionMetadata Execution { get; } = execution;

    public DateTimeOffset UtcNow => time.GetUtcNow();

    public CancellationToken AttemptCancellation { get; } = RequireAttempt(attemptCancellation);

    public string? LastOutcome { get; private set; }

    public CommandId DeterministicCommandId(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        var material =
            $"{Execution.Owner.Value}|{Execution.Behavior.Value}|{Execution.Revision.Value}|{Execution.Execution.Value}|{purpose}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material));
        return new CommandId(new Guid(hash.AsSpan(0, 16)));
    }

    public TContract Get<TContract>(string name = "default")
        where TContract : class, INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return capabilities.Get<TContract>(name);
    }

    public ValueTask<T?> ReadStateAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        using var linked = BehaviorOperationCancellation.Link(AttemptCancellation, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_state.TryGetValue(key, out var value) && value is T typed)
        {
            return ValueTask.FromResult<T?>(typed);
        }

        return ValueTask.FromResult<T?>(default);
    }

    public void SetState<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _state[key] = value;
        if (string.Equals(key, "outcome", StringComparison.Ordinal))
        {
            LastOutcome = value?.ToString();
        }
    }

    private static CancellationToken RequireAttempt(CancellationToken attemptCancellation)
    {
        if (!attemptCancellation.CanBeCanceled)
        {
            throw new ArgumentException(
                "Worker attempt cancellation is required for every behavior operation.",
                nameof(attemptCancellation));
        }

        return attemptCancellation;
    }
}
