using DigitalBrain.V2.Catalog;
using DigitalBrain.V2.Core.Runtime;
using Greeter.Contracts;
using Ping.Contracts;

namespace DigitalBrain.V2.Creator;

public sealed class ArchitectNeuron : Neuron, IArchitectNeuron
{
    private const int MaxAttempts = 2;

    public Task HandleAsync(CreateNeuron synapse, CancellationToken ct)
    {
        State["capability"] = synapse.Capability;
        return Ask<ICatalogNeuron>("default", new DescribeConstellation(
        [
            typeof(IPingNeuron).Assembly.GetName().Name!,
            typeof(IGreeterNeuron).Assembly.GetName().Name!,
            typeof(ICatalogNeuron).Assembly.GetName().Name!
        ]));
    }

    public Task HandleAsync(ConstellationDescribed synapse, CancellationToken ct)
    {
        var capability = State.GetValueOrDefault("capability", "Generated.PingEcho");
        var alreadyExists = synapse.Catalog.Entries.Any(entry =>
            entry.Fqn.Contains(capability, StringComparison.OrdinalIgnoreCase));

        return alreadyExists
            ? Emit(new NeuronActivated(capability, "catalog", "existing"))
            : Ask<IImplementerNeuron>("default", new ImplementNeuron(capability, [], Attempt: 1));
    }

    public Task HandleAsync(NeuronAuthored synapse, CancellationToken ct) =>
        Ask<IGateNeuron>("default", new GateNeuronCandidate(synapse.Capability, synapse.InoSource, synapse.Attempt));

    public Task HandleAsync(GateFailed synapse, CancellationToken ct) =>
        synapse.Attempt < MaxAttempts
            ? Ask<IImplementerNeuron>("default", new ImplementNeuron(synapse.Capability, synapse.Diagnostics, synapse.Attempt + 1))
            : Emit(new NeuronActivationFailed(synapse.Capability, synapse.Diagnostics));
}
