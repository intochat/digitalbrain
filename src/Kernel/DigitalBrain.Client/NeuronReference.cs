using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Messaging;
namespace DigitalBrain.Client;

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

    public Task FireAsync(Synapse synapse, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        return _client.SendToAsync(Id, synapse, cancellationToken);
    }

    public Task<TResponse> FireAsync<TResponse>(
        RequestSynapse<TResponse> request,
        CancellationToken cancellationToken = default)
        where TResponse : Synapse
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return _client.SendRequestAsync<TResponse>(Id, request, cancellationToken);
    }

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
