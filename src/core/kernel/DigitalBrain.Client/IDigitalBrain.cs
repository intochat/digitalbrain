using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Client;

public interface IDigitalBrain
{
    OwnerId Owner { get; }

    Task ActivateAsync(CancellationToken cancellationToken = default);

    NeuronReference<TNeuron> Get<TNeuron>(string name = "default")
        where TNeuron : INeuron;

    [EditorBrowsable(EditorBrowsableState.Never)]
    TNeuron GetGrainProxy<TNeuron>(string name = "default")
        where TNeuron : class, INeuron;

    Task SendAsync<TNeuron>(string name, Synapse synapse, CancellationToken cancellationToken = default)
        where TNeuron : INeuron;

    Task SendAsync(NeuronId receiver, Synapse synapse, CancellationToken cancellationToken = default);

    Task EmitAsync(Synapse synapse, CancellationToken cancellationToken = default);

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
