namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

public interface IBehaviorContext
{
    BehaviorExecutionMetadata Execution { get; }

    DateTimeOffset UtcNow { get; }

    CommandId DeterministicCommandId(string purpose);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Get is the fixed behavior SDK contract for resolving an approved module neuron.")]
    TContract Get<TContract>(string name)
        where TContract : class, INeuron;

    ValueTask<T?> ReadStateAsync<T>(string key, CancellationToken cancellationToken = default);

    void SetState<T>(string key, T value);
}
