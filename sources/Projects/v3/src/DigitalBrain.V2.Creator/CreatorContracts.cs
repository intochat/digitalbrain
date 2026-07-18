using DigitalBrain.V2.Catalog;
using DigitalBrain.V2.Core.Runtime;
using DigitalBrain.V2.Core.Synapses;

namespace DigitalBrain.V2.Creator;

public interface IArchitectNeuron :
    INeuron,
    IHandle<CreateNeuron>,
    IHandle<ConstellationDescribed>,
    IHandle<NeuronAuthored>,
    IHandle<GateFailed>,
    IEmit<DescribeConstellation>,
    IEmit<ImplementNeuron>,
    IEmit<GateNeuronCandidate>,
    IEmit<NeuronActivated>,
    IEmit<NeuronActivationFailed>;

public interface IImplementerNeuron :
    INeuron,
    IHandle<ImplementNeuron>,
    IEmit<NeuronAuthored>;

public interface IGateNeuron :
    INeuron,
    IHandle<GateNeuronCandidate>,
    IEmit<NeuronActivated>,
    IEmit<GateFailed>,
    IEmit<NeuronActivationFailed>;

[GenerateSerializer]
public sealed record CreateNeuron([property: Id(0)] string Capability) : Synapse;

[GenerateSerializer]
public sealed record ImplementNeuron(
    [property: Id(0)] string Capability,
    [property: Id(1)] string[] Diagnostics,
    [property: Id(2)] int Attempt) : Synapse;

[GenerateSerializer]
public sealed record NeuronAuthored(
    [property: Id(0)] string Capability,
    [property: Id(1)] string InoSource,
    [property: Id(2)] int Attempt) : Synapse;

[GenerateSerializer]
public sealed record GateNeuronCandidate(
    [property: Id(0)] string Capability,
    [property: Id(1)] string InoSource,
    [property: Id(2)] int Attempt) : Synapse;

[GenerateSerializer]
public sealed record GateFailed(
    [property: Id(0)] string Capability,
    [property: Id(1)] string[] Diagnostics,
    [property: Id(2)] int Attempt) : Synapse;

[GenerateSerializer]
public sealed record NeuronActivated(
    [property: Id(0)] string Capability,
    [property: Id(1)] string AssemblyName,
    [property: Id(2)] string SimulationType) : Synapse;

[GenerateSerializer]
public sealed record NeuronActivationFailed(
    [property: Id(0)] string Capability,
    [property: Id(1)] string[] Diagnostics) : Synapse;
