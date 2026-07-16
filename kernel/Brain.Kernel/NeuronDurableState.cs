using Brain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace Brain.Kernel;

public sealed class NeuronDurableState(
    [FromKeyedServices("neuron-journal")] IDurableList<NeuronEvent> journal,
    [FromKeyedServices("neuron-receipts")] IDurableDictionary<string, NeuronReceipt> receipts,
    [FromKeyedServices("neuron-synapses")] IDurableList<SynapseRecord> synapses)
{
    public IDurableList<NeuronEvent> Journal { get; } = journal;
    public IDurableDictionary<string, NeuronReceipt> Receipts { get; } = receipts;
    public IDurableList<SynapseRecord> Synapses { get; } = synapses;
}
