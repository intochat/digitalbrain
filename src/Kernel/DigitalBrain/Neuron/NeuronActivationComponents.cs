using DigitalBrain.Abstractions.Signals;
using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed record NeuronActivationComponents(
    TimeProvider Clock,
    NeuronJournals Journals,
    NeuronSynapses Synapses,
    IDurableDictionary<string, SignalDelivery> Latest,
    PendingWork Pending);
