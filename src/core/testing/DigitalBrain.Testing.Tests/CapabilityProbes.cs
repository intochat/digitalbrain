using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.TestingTests;

[GenerateSerializer]
[Alias("db.testing.capability-ping")]
[Description("Capability probe ping")]
public sealed record CapabilityPing : Synapse;

[Alias("testing.capability-caller")]
[Description("Capability probe caller neuron")]
public partial interface ICapabilityCaller : INeuron;

[Alias("testing.capability-target")]
[Description("Capability probe target neuron")]
public partial interface ICapabilityTarget : INeuron
{
    [Alias(nameof(Poke))]
    Task Poke();
}

internal sealed class CapabilityCaller :
    Neuron,
    ICapabilityCaller,
    IHandle<CapabilityPing>
{
    public Task HandleAsync(CapabilityPing synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        return GrainFactory
            .GetGrain<ICapabilityTarget>(
                NeuronId.For<ICapabilityTarget>(
                    Id.Owner,
                    TestingScenario.CapabilityTarget).ToGrainId())
            .Poke();
    }
}

internal sealed class CapabilityTarget : Neuron, ICapabilityTarget
{
    public Task Poke() => Task.CompletedTask;
}
