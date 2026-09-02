using System.ComponentModel;
using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Client;

public sealed class DigitalBrainClient : IDigitalBrain
{
    private readonly DigitalBrainClientTransport _transport;

    private DigitalBrainClient(DigitalBrainClientTransport transport)
        => _transport = transport;

    public OwnerId Owner => _transport.Owner;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DigitalBrainClient Connect(IGrainFactory grains, string owner)
        => new(new DigitalBrainClientTransport(grains, new OwnerId(owner)));

    public Task ActivateAsync(CancellationToken cancellationToken = default)
        => _transport.ActivateAsync(cancellationToken);

    public NeuronReference<TNeuron> Get<TNeuron>(string name = "default")
        where TNeuron : INeuron
        => _transport.GetReference<TNeuron>(this, name);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public TNeuron GetGrainProxy<TNeuron>(string name = "default")
        where TNeuron : class, INeuron
        => _transport.GetGrainProxy<TNeuron>(name);

    public TEntity GetEntity<TEntity>(string name = "default")
        where TEntity : class, IEntity
        => _transport.GetEntity<TEntity>(name);

    public Task<DeliveryOutcome> SendAsync<TNeuron>(
        string name,
        Signal signal,
        CancellationToken cancellationToken = default)
        where TNeuron : INeuron
        => _transport.SendAsync<TNeuron>(name, signal, cancellationToken);

    public Task<JournalRead> ReadJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
        => _transport.ReadJournalAsync(_transport.Root, kind, afterSequence, cancellationToken);

    public IAsyncEnumerable<JournalRead> WatchJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
        => _transport.WatchJournalAsync(_transport.Root, kind, afterSequence, cancellationToken);

    public Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
        CancellationToken cancellationToken = default)
        => _transport.GetSynapsesAsync(_transport.Root, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal Task<DeliveryOutcome> SendAsync(
        NeuronId receiver,
        Signal signal,
        CancellationToken cancellationToken)
        => _transport.SendAsync(receiver, signal, cancellationToken);

    internal Task<TResponse> SendRequestAsync<TResponse>(
        NeuronId receiver,
        Signal request,
        CancellationToken cancellationToken)
        where TResponse : Signal
        => _transport.SendRequestAsync<TResponse>(receiver, request, cancellationToken);

    internal Task<JournalRead> ReadJournalAsync(
        NeuronId subject,
        JournalKind kind,
        long afterSequence,
        CancellationToken cancellationToken)
        => _transport.ReadJournalAsync(subject, kind, afterSequence, cancellationToken);

    internal IAsyncEnumerable<JournalRead> WatchJournalAsync(
        NeuronId subject,
        JournalKind kind,
        long afterSequence,
        CancellationToken cancellationToken)
        => _transport.WatchJournalAsync(subject, kind, afterSequence, cancellationToken);

    internal Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
        NeuronId subject,
        CancellationToken cancellationToken)
        => _transport.GetSynapsesAsync(subject, cancellationToken);
}
