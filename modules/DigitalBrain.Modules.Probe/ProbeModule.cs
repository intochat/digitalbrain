using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Modules.Probe.Contracts;

namespace DigitalBrain.Modules.Probe;

public sealed class ProbeModule : IModule
{
    public ModuleDescriptor Descriptor { get; } = new(
        Id: "probe",
        Version: "0.1.0",
        DisplayName: "Probe",
        Configuration: [],
        Secrets: [],
        Capabilities: [new CapabilityDeclaration("probe.echo", "Echo text for diagnostics")],
        Effects: [new EffectDeclaration("probe.pinged", "Records a probe ping")],
        Connections: []);
}

public sealed class ProbeEchoNeuron : Neuron, IProbeEcho, IHandle<ProbePinged>
{
    public Task<string> EchoAsync(string text) => Task.FromResult(text);

    public Task HandleAsync(ProbePinged synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}
