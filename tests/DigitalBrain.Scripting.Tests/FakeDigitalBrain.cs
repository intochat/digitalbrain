using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Scripting.Tests;

internal sealed class FakeDigitalBrain(string owner) : IDigitalBrain
{
    public OwnerId Owner { get; } = new(owner);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task ActivateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public NeuronReference<TNeuron> Get<TNeuron>(string name = "default")
        where TNeuron : INeuron => throw new NotSupportedException();

    public TEntity GetEntity<TEntity>(string name = "default")
        where TEntity : class, IEntity => throw new NotSupportedException();

    public TNeuron GetGrainProxy<TNeuron>(string name = "default")
        where TNeuron : class, INeuron => throw new NotSupportedException();

    public Task<DeliveryOutcome> SendAsync<TNeuron>(
        string name,
        Signal signal,
        CancellationToken cancellationToken = default)
        where TNeuron : INeuron => throw new NotSupportedException();

    public Task<JournalRead> ReadJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IAsyncEnumerable<JournalRead> WatchJournalAsync(
        JournalKind kind,
        long afterSequence = 0,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
