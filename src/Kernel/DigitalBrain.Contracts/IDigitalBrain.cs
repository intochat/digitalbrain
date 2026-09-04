using System.ComponentModel;
using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Abstractions;

// The owner's typed handle on the graph. Address neurons with Get, entities with GetEntity.
// Trigger work with NeuronReference.SendAsync: it only compiles when the neuron IHandle<T>s
// that signal. GetGrainProxy is an escape hatch for grain RPC (chat), not the programming model.
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
