using System.ComponentModel;
using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Abstractions;

public interface IDigitalBrain : IAsyncDisposable
{
    OwnerId Owner { get; }

    Task ActivateAsync(CancellationToken cancellationToken = default);

    NeuronReference<TNeuron> Get<TNeuron>(string name = "default")
        where TNeuron : INeuron;

    TEntity GetEntity<TEntity>(string name = "default")
        where TEntity : class, IEntity;

    [EditorBrowsable(EditorBrowsableState.Never)]
    TNeuron GetGrainProxy<TNeuron>(string name = "default")
        where TNeuron : class, INeuron;

    Task<DeliveryOutcome> SendAsync<TNeuron>(
        string name,
        Signal signal,
        CancellationToken cancellationToken = default)
        where TNeuron : INeuron;

    Task<JournalRead> ReadJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<JournalRead> WatchJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
        CancellationToken cancellationToken = default);
}
