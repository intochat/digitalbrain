using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Abstractions;

public readonly struct NeuronReference<TNeuron> : IEquatable<NeuronReference<TNeuron>>
    where TNeuron : INeuron
{
    private readonly DigitalBrainClient _client;
    private readonly string _name;

    internal NeuronReference(DigitalBrainClient client, string name)
    {
        _client = client;
        _name = name;
    }

    public NeuronId Id => NeuronId.For<TNeuron>(_client.Owner, _name);

    public Task<DeliveryOutcome> SendAsync(
        Signal signal,
        CancellationToken cancellationToken = default)
        => _client.SendAsync(Id, signal, cancellationToken);

    public Task<TResponse> SendAsync<TResponse>(
        Signal<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : Signal
        => _client.SendRequestAsync<TResponse>(Id, request, cancellationToken);

    public Task<JournalRead> ReadJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
        => _client.ReadJournalAsync(Id, kind, afterSequence, cancellationToken);

    public IAsyncEnumerable<JournalRead> WatchJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
        => _client.WatchJournalAsync(Id, kind, afterSequence, cancellationToken);

    public Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
        CancellationToken cancellationToken = default)
        => _client.GetSynapsesAsync(Id, cancellationToken);

    public bool Equals(NeuronReference<TNeuron> other)
        => ReferenceEquals(_client, other._client)
            && string.Equals(_name, other._name, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is NeuronReference<TNeuron> other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(_client, _name);

    public static bool operator ==(NeuronReference<TNeuron> left, NeuronReference<TNeuron> right)
        => left.Equals(right);

    public static bool operator !=(NeuronReference<TNeuron> left, NeuronReference<TNeuron> right)
        => !left.Equals(right);
}
