using System.Collections.ObjectModel;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using Orleans.Journaling;

namespace DigitalBrain.Os.Infrastructure.Orleans;

// In-memory IDurableList<T> stand-in (exact impl from the custom preview journaling path).
// Trims to MaxJournalEntries on Add for memory safety while preserving recent history for AI replay.
internal sealed class InMemoryDurableList<T> : List<T>, IDurableList<T>
{
    public new void Add(T item)
    {
        base.Add(item);
        if (Count > Neuron.Core.MaxJournalEntries)
        {
            int excess = Count - Neuron.Core.MaxJournalEntries;
            RemoveRange(0, excess);
        }
    }

    public new void AddRange(IEnumerable<T> collection) => base.AddRange(collection);
    public new ReadOnlyCollection<T> AsReadOnly() => base.AsReadOnly();
}

// Per-grain journal store. Provides isolated Incoming/Outgoing IDurableList<Synapse> by NeuronId/Self.
// Registered as singleton; neurons pull via GetOrCreate(Self) on activate so journals survive re-activation for causal history.
public sealed class JournalStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<NeuronId, (IDurableList<Synapse> Incoming, IDurableList<Synapse> Outgoing)> _byNeuron = new();

    public (IDurableList<Synapse> Incoming, IDurableList<Synapse> Outgoing) GetOrCreate(NeuronId id)
    {
        return _byNeuron.GetOrAdd(id, _ =>
        {
            var inc = new InMemoryDurableList<Synapse>();
            var outg = new InMemoryDurableList<Synapse>();
            return (inc, outg);
        });
    }
}
