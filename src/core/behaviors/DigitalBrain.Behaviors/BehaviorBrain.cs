using DigitalBrain.Abstractions;

namespace DigitalBrain.Behaviors;

public sealed class BehaviorBrain<TTrigger> : IAsyncDisposable
{
    private readonly CancellationToken _attemptCancellation;

    internal BehaviorBrain(BehaviorTrigger<TTrigger> trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        Trigger = trigger.Value;
        _attemptCancellation = trigger.AttemptCancellation;
    }

    public TTrigger Trigger { get; }

    public BehaviorNeuronReference<TNeuron> Get<TNeuron>(string name = "default")
        where TNeuron : INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new BehaviorNeuronReference<TNeuron>(name, _attemptCancellation);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public readonly struct BehaviorNeuronReference<TNeuron> : IEquatable<BehaviorNeuronReference<TNeuron>>
    where TNeuron : INeuron
{
    private readonly CancellationToken _attemptCancellation;
    private readonly string _name;

    internal BehaviorNeuronReference(string name, CancellationToken attemptCancellation)
    {
        _name = name;
        _attemptCancellation = attemptCancellation;
    }

    public string Name => _name;

    public Task SendAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        using var linked = BehaviorOperationCancellation.Link(_attemptCancellation, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();
        return Task.FromException(
            new InvalidOperationException(
                "Behavior synapse delivery is supplied by the isolated worker broker."));
    }

    public Task<TResponse> SendAsync<TResponse>(
        RequestSynapse<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : Synapse
    {
        ArgumentNullException.ThrowIfNull(request);
        using var linked = BehaviorOperationCancellation.Link(_attemptCancellation, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();
        return Task.FromException<TResponse>(
            new InvalidOperationException(
                "Behavior synapse delivery is supplied by the isolated worker broker."));
    }

    public bool Equals(BehaviorNeuronReference<TNeuron> other)
        => string.Equals(_name, other._name, StringComparison.Ordinal)
            && _attemptCancellation.Equals(other._attemptCancellation);

    public override bool Equals(object? obj)
        => obj is BehaviorNeuronReference<TNeuron> other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(_name, _attemptCancellation);

    public static bool operator ==(BehaviorNeuronReference<TNeuron> left, BehaviorNeuronReference<TNeuron> right)
        => left.Equals(right);

    public static bool operator !=(BehaviorNeuronReference<TNeuron> left, BehaviorNeuronReference<TNeuron> right)
        => !left.Equals(right);
}
