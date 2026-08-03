using DigitalBrain.Abstractions;

namespace DigitalBrain.Behaviors;

public sealed class BehaviorBrain<TTrigger> : IAsyncDisposable
    where TTrigger : Synapse
{
    private readonly CancellationToken _attemptCancellation;
    private readonly IBehaviorSynapseBroker _broker;

    internal BehaviorBrain(BehaviorTrigger<TTrigger> trigger)
        : this(trigger, CompileOnlyBehaviorSynapseBroker.Instance)
    {
    }

    internal BehaviorBrain(BehaviorTrigger<TTrigger> trigger, IBehaviorSynapseBroker broker)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(broker);
        Trigger = trigger.Value;
        _attemptCancellation = trigger.AttemptCancellation;
        _broker = broker;
    }

    internal static BehaviorBrain<TTrigger> Create(
        BehaviorTrigger<TTrigger> trigger,
        IBehaviorSynapseBroker broker)
        => new(trigger, broker);

    public TTrigger Trigger { get; }

    public BehaviorNeuronReference<TNeuron> Get<TNeuron>(string name = "default")
        where TNeuron : INeuron
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new BehaviorNeuronReference<TNeuron>(name, _broker, _attemptCancellation);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public readonly struct BehaviorNeuronReference<TNeuron> : IEquatable<BehaviorNeuronReference<TNeuron>>
    where TNeuron : INeuron
{
    private readonly CancellationToken _attemptCancellation;
    private readonly IBehaviorSynapseBroker _broker;
    private readonly string _name;

    internal BehaviorNeuronReference(
        string name,
        IBehaviorSynapseBroker broker,
        CancellationToken attemptCancellation)
    {
        _name = name;
        _broker = broker;
        _attemptCancellation = attemptCancellation;
    }

    public string Name => _name;

    public async Task SendAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        using var linked = BehaviorOperationCancellation.Link(_attemptCancellation, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();
        await _broker.SendAsync<TNeuron>(_name, synapse, linked.Token).ConfigureAwait(false);
    }

    public async Task<TResponse> SendAsync<TResponse>(
        RequestSynapse<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : Synapse
    {
        ArgumentNullException.ThrowIfNull(request);
        using var linked = BehaviorOperationCancellation.Link(_attemptCancellation, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();
        return await _broker
            .SendAsync<TNeuron, TResponse>(_name, request, linked.Token)
            .ConfigureAwait(false);
    }

    public bool Equals(BehaviorNeuronReference<TNeuron> other)
        => string.Equals(_name, other._name, StringComparison.Ordinal)
            && _attemptCancellation.Equals(other._attemptCancellation)
            && ReferenceEquals(_broker, other._broker);

    public override bool Equals(object? obj)
        => obj is BehaviorNeuronReference<TNeuron> other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(_name, _attemptCancellation, _broker);

    public static bool operator ==(BehaviorNeuronReference<TNeuron> left, BehaviorNeuronReference<TNeuron> right)
        => left.Equals(right);

    public static bool operator !=(BehaviorNeuronReference<TNeuron> left, BehaviorNeuronReference<TNeuron> right)
        => !left.Equals(right);
}
