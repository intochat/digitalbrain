using System.Reflection;
using DigitalBrain.Rules;

namespace DigitalBrain.Tests.Unit.Synapses;

[Signal("test.added")]
internal sealed record Added(int N) : Signal;

public sealed class SynapseVocabularyFacts
{
    [Fact]
    public void GateAttributeNamesSignalStateAndDefaultKey()
    {
        var a = new SynapseAttribute(typeof(Added), "week");
        Assert.Equal(typeof(Added), a.Signal);
        Assert.Equal("week", a.State);
        Assert.Equal("default", a.Key);
    }

    [Fact]
    public void IntoWithSlashSplitsAliasAndKey()
    {
        var a = new SynapseAttribute(typeof(Added), "week/2026-45");
        Assert.Equal("week", a.State);
        Assert.Equal("2026-45", a.Key);
    }

    [Fact]
    public void ViewAttributeHasNoSignal()
    {
        var a = new SynapseAttribute("week");
        Assert.Null(a.Signal);
        Assert.Equal("week", a.State);
    }

    [Fact]
    public void EmptyAliasThrows() =>
        Assert.Throws<ArgumentException>(() => new SynapseAttribute(typeof(Added), "/2026-45"));

    [Fact]
    public void RejectWithReasonIsOneRuleNamedByTheCaller()
    {
        var c = Assert.IsType<Check.Refused>(Check.Reject("weekly cap of $300"));
        Assert.Single(c.Broken);
        Assert.Equal("gate", c.Broken[0].Name);
        Assert.Equal("weekly cap of $300", c.Broken[0].Message);
        Assert.True(c.Broken[0].Broken);
    }

    [Fact]
    public void AllowCarriesNextState() =>
        Assert.Equal(5, Assert.IsType<Check.Allowed>(Check.Allow(5)).Next);

    [Fact]
    public void SynapseSignalsAreEphemeralWithAliases()
    {
        Assert.Equal("synapse.view-changed", typeof(ViewChanged).GetCustomAttribute<SignalAttribute>()!.Alias);
        Assert.True(typeof(SynapseStarted).IsDefined(typeof(EphemeralSignalAttribute), false));
        Assert.True(typeof(SynapseFinished).IsDefined(typeof(EphemeralSignalAttribute), false));
        Assert.Equal("synapse.failed", typeof(SynapseFailed).GetCustomAttribute<SignalAttribute>()!.Alias);
        Assert.False(typeof(SynapseFailed).IsDefined(typeof(EphemeralSignalAttribute), false));
    }

    [Fact]
    public void ReactionOfCollectsSignals()
    {
        var r = Reaction<int>.Of(3, new Added(1), new Added(2));
        Assert.Equal(3, r.Next);
        Assert.Equal(2, r.Signals.Count);
    }
}
