using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Client;

public interface IDigitalBrain
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

    Task FireAsync<TNeuron>(string name, Synapse synapse, CancellationToken cancellationToken = default)
        where TNeuron : INeuron;

    Task FireAsync(NeuronId receiver, Synapse synapse, CancellationToken cancellationToken = default);

    Task FireAsync(Synapse synapse, CancellationToken cancellationToken = default);

    Task<JournalRead> ReadJournalAsync(
        NeuronId subject,
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<JournalRead> WatchJournalAsync(
        NeuronId subject,
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default);
}
