using DigitalBrain.Units;

namespace DigitalBrain.Tests.Unit.Synapses;

[State("dash")]
public sealed record Dash(int Count = 0)
{
    public Dash() : this(0) { }
}

public static class Dashboard
{
    [Synapse("x.polled", "dash")]
    public static Dash Count(SignalEnvelope s, Dash d) => d with { Count = s.Payload.GetProperty("count").GetInt32() };
}

// Unattributed: the fact passes the attribute so the assembly-wide scan does not trip on the refused shape.
public static class TypedOnAlias
{
    public static Dash Count(Charging s, Dash d) => d;
}

public sealed class AliasReactionFacts
{
    private static readonly Dictionary<string, Type> States = new() { ["dash"] = typeof(Dash) };

    [Fact]
    public void AnAliasReactionReadsAsAReactionOnTheEnvelope()
    {
        var d = SynapseShape.Of(typeof(Dashboard).GetMethod(nameof(Dashboard.Count))!, States);
        Assert.Equal(SynapseKind.Reaction, d.Kind);
        Assert.Equal("x.polled", d.SignalAlias);
        Assert.Equal(typeof(SignalEnvelope), d.Signal);
    }

    [Fact]
    public void EnvelopeReactionWithTypedParameterIsRefused()
    {
        var e = Assert.Throws<SynapseShapeException>(() => SynapseShape.Of(typeof(TypedOnAlias).GetMethod(nameof(TypedOnAlias.Count))!, States, new SynapseAttribute("x.polled", "dash")));
        Assert.Contains(SynapseReasons.AliasNeedsEnvelope, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NeedsSentenceNamesTheAlias()
    {
        var needs = SynapseCatalog.Scan([typeof(Dash), typeof(Dashboard)]).NeedsOf("dash");
        Assert.Equal(["alias:x.polled"], needs.Reactions);
        Assert.Equal("dash wants to: react to x.polled.", needs.Sentence());
    }

    [Fact]
    public void TheAttributeKeepsTheSignalAliasApartFromTheState()
    {
        var a = new SynapseAttribute("x.polled", "dash/k");
        Assert.Equal("x.polled", a.SignalAlias);
        Assert.Equal("dash", a.State);
        Assert.Equal("k", a.Key);
        Assert.Null(a.Signal);
        Assert.Null(new SynapseAttribute("dash").SignalAlias);
    }
}
