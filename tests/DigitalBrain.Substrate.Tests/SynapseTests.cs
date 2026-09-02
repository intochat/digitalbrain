using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Synapses;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class SynapseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan HalfLife = TimeSpan.FromDays(14);
    private static readonly NeuronId Chat = new("chat", new OwnerId("owner"), "main");
    private static readonly NeuronId Greeter = new("greeter", new OwnerId("owner"), "default");

    private static Synapse Learned(double weight = 0.50, DateTimeOffset? at = null)
        => new(Chat, Greeter, "UserMessageReceived", weight, at ?? T0, SynapseKind.Learned);

    [Fact]
    public void Potentiate_MovesWeightTowardOneByTheRate()
    {
        var potentiated = Learned().Potentiate(T0, HalfLife, rate: 0.30);

        Assert.Equal(0.65, potentiated.Weight, precision: 10);
        Assert.Equal(1, potentiated.FireCount);
    }

    [Fact]
    public void Potentiate_TwiceMatchesTheConsoleProof()
    {
        var twice = Learned()
            .Potentiate(T0, HalfLife, 0.30)
            .Potentiate(T0, HalfLife, 0.30);

        Assert.Equal(0.755, twice.Weight, precision: 10);
        Assert.Equal(2, twice.FireCount);
    }

    [Fact]
    public void Potentiate_NeverExceedsOne()
    {
        var synapse = Learned(weight: 0.99);

        for (var i = 0; i < 200; i++)
        {
            synapse = synapse.Potentiate(T0, HalfLife, 0.30);
        }

        Assert.True(synapse.Weight <= 1.0);
    }

    [Fact]
    public void Potentiate_StampsTheFiringInstant()
    {
        var later = T0.AddDays(3);

        Assert.Equal(later, Learned().Potentiate(later, HalfLife, 0.30).LastFiredAt);
    }

    [Fact]
    public void Potentiate_InnateSynapseDoesNotDecayAndRecordsTheFiring()
    {
        var later = T0.AddDays(3650);
        var innate = new Synapse(
            Chat,
            Greeter,
            "UserMessageReceived",
            0.50,
            T0,
            SynapseKind.Innate,
            fireCount: 4,
            isBlocking: true);

        var potentiated = innate.Potentiate(later, HalfLife, 0.30);

        Assert.Equal(0.65, potentiated.Weight, precision: 10);
        Assert.Equal(later, potentiated.LastFiredAt);
        Assert.Equal(5, potentiated.FireCount);
        Assert.True(potentiated.IsBlocking);
    }

    [Fact]
    public void WeightAt_HalvesEveryHalfLife()
    {
        var synapse = Learned(weight: 0.80);
        var halfLife = TimeSpan.FromDays(14);

        Assert.Equal(0.80, synapse.WeightAt(T0, halfLife), precision: 10);
        Assert.Equal(0.40, synapse.WeightAt(T0.AddDays(14), halfLife), precision: 10);
        Assert.Equal(0.20, synapse.WeightAt(T0.AddDays(28), halfLife), precision: 10);
    }

    [Fact]
    public void WeightAt_LeavesInnateSynapsesUntouched()
    {
        var innate = new Synapse(Chat, Greeter, "UserMessageReceived", 1.0, T0, SynapseKind.Innate);

        Assert.Equal(1.0, innate.WeightAt(T0.AddDays(3650), TimeSpan.FromDays(14)), precision: 10);
    }

    [Fact]
    public void IsPrunedAt_IsTrueOnlyOnceDecayCrossesTheFloor()
    {
        var synapse = Learned(weight: 0.50);
        var halfLife = TimeSpan.FromDays(14);

        Assert.False(synapse.IsPrunedAt(T0.AddDays(28), halfLife, floor: 0.05));  // 0.125
        Assert.True(synapse.IsPrunedAt(T0.AddDays(70), halfLife, floor: 0.05));   // ~0.0156
    }

    [Fact]
    public void IsPrunedAt_NeverPrunesAnInnateSynapse()
    {
        var innate = new Synapse(Chat, Greeter, "UserMessageReceived", 1.0, T0, SynapseKind.Innate);

        Assert.False(innate.IsPrunedAt(T0.AddDays(3650), TimeSpan.FromDays(14), floor: 0.05));
    }

    [Fact]
    public void Construction_RefusesABlockingSynapseThatIsNotInnate()
    {
        Assert.Throws<ArgumentException>(() =>
            new Synapse(Chat, Greeter, "UserMessageReceived", 0.5, T0, SynapseKind.Discovered, isBlocking: true));
    }

    [Fact]
    public void Construction_RefusesAnEmptySignalType()
    {
        Assert.Throws<ArgumentException>(() =>
            new Synapse(Chat, Greeter, "  ", 0.5, T0, SynapseKind.Learned));
    }
}
