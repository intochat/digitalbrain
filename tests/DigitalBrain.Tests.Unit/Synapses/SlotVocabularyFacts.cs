using DigitalBrain.Capabilities;
using DigitalBrain.Confirmation;
using DigitalBrain.Time;

namespace DigitalBrain.Tests.Unit.Synapses;

public sealed class SlotVocabularyFacts
{
    [Fact]
    public void NeedsSentenceNamesEveryNeedAndHashIsStable()
    {
        var n = new SynapseNeeds("stats", [new("Db", new CapabilityId("sql-read", 1), "read-only SQL")], [Schedule.Parse("07:00")], [], ["Refresh"], HasView: true);
        Assert.Equal("stats wants to: read-only SQL through Db; run daily at 07:00 UTC; react to 1 signal; show a view.", n.Sentence());
        Assert.Equal(n.Hash(), (n with { }).Hash());
        Assert.NotEqual(n.Hash(), (n with { HasView = false }).Hash());
    }

    [Fact]
    public void GateSentenceSaysStop()
    {
        var n = new SynapseNeeds("budget", [], [], ["payments.charging"], [], HasView: true);
        Assert.Equal("budget wants to: stop payments.charging before it happens; show a view.", n.Sentence());
    }

    [Fact]
    public void WeeklyAndIntervalSentences()
    {
        var n = new SynapseNeeds("a", [], [Schedule.Parse("Mon 09:30"), Schedule.Parse("every 5m")], [], ["X", "Y"], HasView: false);
        Assert.Equal("a wants to: run weekly on Mon at 09:30 UTC; run every 5m; react to 2 signals.", n.Sentence());
    }

    [Fact]
    public void HashIgnoresOrderWithinAList()
    {
        var a = new SynapseNeeds("a", [], [], ["g1", "g2"], [], HasView: false);
        var b = new SynapseNeeds("a", [], [], ["g2", "g1"], [], HasView: false);
        Assert.Equal(a.Hash(), b.Hash());
        Assert.Matches("^[0-9a-f]{64}$", a.Hash());
    }

    [Fact]
    public void EveryIsAPlainInitProperty()
    {
        var a = new SynapseAttribute(typeof(Tick), "stats") { Every = "07:00" };
        Assert.Equal("07:00", a.Every);
        Assert.Null(new SynapseAttribute("stats").Every);
    }

    [Fact]
    public void SlotNameDefaultsToNull()
    {
        Assert.Null(new SlotAttribute().Name);
        Assert.Equal("Db", new SlotAttribute { Name = "Db" }.Name);
    }

    [Fact]
    public void RequiresPersonAsksUnderDestructiveOnlyButNotUnderNever()
    {
        var plan = new ConfirmationPlan("p", ConfirmationAction.Other, Destructive: false, "t", "b", "ok", "no", "x") { RequiresPerson = true };
        Assert.True(ConfirmationRules.Asks(ConfirmationPolicy.DestructiveOnly, plan));
        Assert.True(ConfirmationRules.Asks(ConfirmationPolicy.Always, plan));
        Assert.False(ConfirmationRules.Asks(ConfirmationPolicy.Never, plan));
    }
}
