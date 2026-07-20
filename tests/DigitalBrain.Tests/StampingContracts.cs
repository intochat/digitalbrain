using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class StampingContracts
{
    private static readonly NeuronId Caller = new("Greeter", new OwnerId("acme"), "polite");

    [Fact]
    public void DeliveryWithoutCauseStartsANewConversation()
    {
        var synapse = new DeliveryProbe();

        var delivery = SynapseDelivery.Create(synapse, Caller, sequence: 1);

        Assert.Same(synapse, delivery.Synapse);
        Assert.Equal(Caller, delivery.Caller);
        Assert.Equal(1, delivery.Sequence);
        Assert.Null(delivery.CausationId);
        Assert.NotEqual(default, delivery.CorrelationId);
        Assert.NotEqual(default, delivery.SynapseId);
    }

    [Fact]
    public void CausedDeliveryInheritsTheConversationAndPointsAtItsParent()
    {
        var cause = SynapseDelivery.Create(new DeliveryProbe(), Caller, sequence: 1);

        var delivery = SynapseDelivery.Create(new DeliveryProbe(), Caller, sequence: 2, cause);

        Assert.Equal(cause.CorrelationId, delivery.CorrelationId);
        Assert.Equal(cause.SynapseId, delivery.CausationId);
        Assert.NotEqual(cause.SynapseId, delivery.SynapseId);
    }

    [Fact]
    public void CorrelationSurvivesEveryHopWhileCausationTracksTheParent()
    {
        var first = SynapseDelivery.Create(new DeliveryProbe(), Caller, sequence: 1);
        var second = SynapseDelivery.Create(new DeliveryProbe(), Caller, sequence: 2, first);
        var third = SynapseDelivery.Create(new DeliveryProbe(), Caller, sequence: 3, second);

        Assert.Equal(first.CorrelationId, third.CorrelationId);
        Assert.Equal(second.SynapseId, third.CausationId);
    }

    [Fact]
    public void TimestampComesFromTheSuppliedTimeProvider()
    {
        var time = new FixedTime(DateTimeOffset.Parse("2026-07-19T10:30:00Z", System.Globalization.CultureInfo.InvariantCulture));

        var delivery = SynapseDelivery.Create(
            new DeliveryProbe(),
            Caller,
            sequence: 1,
            cause: null,
            timeProvider: time);

        Assert.Equal(time.GetUtcNow(), delivery.Timestamp);
    }

    [Fact]
    public void DeliverySequenceMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SynapseDelivery.Create(new DeliveryProbe(), Caller, sequence: 0));
    }

    [Fact]
    public void SimulationCallerHandsTheKernelOnlyAPlainSynapse()
    {
        var stimulus = typeof(ISimulationNeuron).GetMethod(nameof(ISimulationNeuron.StimulateAsync))!;

        Assert.Equal(typeof(Synapse), stimulus.GetParameters()[1].ParameterType);
    }

    private sealed record DeliveryProbe : Synapse;

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
