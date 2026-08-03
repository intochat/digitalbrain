using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.TestingTests;

[GenerateSerializer]
[Alias("db.testing.capability-ping")]
[Description("Capability probe ping")]
public sealed record CapabilityPing : Synapse;

[GenerateSerializer]
[Alias("db.testing.capability-ping-retracted")]
[Description("Capability probe ping whose first turn throws after its capability request")]
public sealed record CapabilityPingRetractedOnce : Synapse;

[GenerateSerializer]
[Alias("db.testing.capability-ping-settled")]
[Description("Capability probe ping whose turn answers with a settled failure after its capability request")]
public sealed record CapabilityPingSettled : Synapse;

[GenerateSerializer]
[Alias("db.testing.settled-probe-failure")]
[SettledDeliveryFailure]
public sealed class SettledProbeFailureException : Exception
{
    public SettledProbeFailureException()
        : this("The capability probe answers its delivery with a settled failure.")
    {
    }

    public SettledProbeFailureException(string message)
        : base(message)
    {
    }

    public SettledProbeFailureException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

[Alias("testing.capability-caller")]
[Description("Capability probe caller neuron")]
public partial interface ICapabilityCaller : INeuron;

[Alias("testing.capability-target")]
[Description("Capability probe target neuron")]
public partial interface ICapabilityTarget : INeuron
{
    [Alias(nameof(Poke))]
    Task Poke();

    [Alias(nameof(Settle))]
    Task Settle();
}

internal sealed class CapabilityCaller :
    Neuron,
    ICapabilityCaller,
    IHandle<CapabilityPing>,
    IHandle<CapabilityPingRetractedOnce>,
    IHandle<CapabilityPingSettled>
{
    private int _retractionsPending = 1;

    public Task HandleAsync(CapabilityPing synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        return Target().Poke();
    }

    // The kernel commits this delivery's inbound cause before it journals the capability request,
    // so everything after Poke runs in a turn whose cause is already durable. Throwing there is the
    // window a retraction has to unwind; Settle is reached only by a turn that ran to the end.
    public async Task HandleAsync(CapabilityPingRetractedOnce synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        await Target().Poke();

        if (Interlocked.Exchange(ref _retractionsPending, 0) == 1)
        {
            throw new InvalidOperationException("The capability probe fails its first turn once.");
        }

        await Target().Settle();
    }

    // The same window, answered instead of failed: the delivery is consumed and never retried.
    public async Task HandleAsync(CapabilityPingSettled synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        await Target().Poke();

        throw new SettledProbeFailureException();
    }

    private ICapabilityTarget Target()
        => GrainFactory.GetGrain<ICapabilityTarget>(
            NeuronId.For<ICapabilityTarget>(Id.Owner, TestingScenario.CapabilityTarget).ToGrainId());
}

internal sealed class CapabilityTarget : Neuron, ICapabilityTarget
{
    public Task Poke() => Task.CompletedTask;

    public Task Settle() => Task.CompletedTask;
}
