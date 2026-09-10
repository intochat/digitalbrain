using DigitalBrain.Abstractions.Signals;
using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed record NeuronActivationComponents(
    TimeProvider Clock,
    NeuronOptions Options,
    NeuronJournals Journals,
    CommandJournal Commands,
    CommandDedup Dedup,
    CommandExecution Execution,
    NeuronSynapses Synapses,
    IDurableDictionary<string, SignalDelivery> Latest,
    PendingWork Pending);
