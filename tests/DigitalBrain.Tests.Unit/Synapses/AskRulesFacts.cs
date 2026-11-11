using DigitalBrain.Units;
using System.Text.Json;

namespace DigitalBrain.Tests.Unit.Synapses;

public sealed class AskRulesFacts
{
    private static readonly IReadOnlyList<SynapseDescriptor> Declared =
        SynapseCatalog.Scan([typeof(NeedState), typeof(NeedAsking)]).Synapses;

    private static Ask A(string key, string slot = "feed", string args = "{}") =>
        new(slot, "Get", JsonElement.Parse(args), key);

    public static TheoryData<Ask[], string> Refused()
    {
        var maxArgs = "{\"p\":\"" + new string('x', AskLimits.MaxArgumentsBytes - 8) + "\"}";
        var overArgs = "{\"p\":\"" + new string('x', AskLimits.MaxArgumentsBytes - 7) + "\"}";
        return new()
        {
            { [.. Enumerable.Range(0, AskLimits.MaxAsksPerReaction + 1).Select(i => A($"k{i}"))], "1001 asks exceed 1000 per reaction" },
            { [A("")], "an ask has no key" },
            { [A(new string('k', AskLimits.MaxKeyLength + 1))], "an ask key is longer than 128 characters" },
            { [A("k") with { Operation = "" }], "ask 'k' names no operation" },
            { [A("k", slot: "other")], "ask 'k' names slot 'other', which the unit does not declare as an Asks<> slot" },
            { [A("k", args: "[]")], "ask 'k': arguments must be a JSON object" },
            { [new Ask("feed", "Get", default, "k")], "ask 'k': arguments must be a JSON object" },
            { [A("k", args: overArgs)], "ask 'k': arguments of 16385 bytes exceed 16384" },
            { [A("k") with { Timeout = TimeSpan.Zero }], "ask 'k': the timeout must be positive" },
            { [A("k"), A("k")], "two asks share the key 'k'" },
            { [.. Enumerable.Range(0, 70).Select(i => A($"k{i}", args: maxArgs))], "asks of 1147570 bytes exceed 1048576 per reaction" },
        };
    }

    [Fact]
    public void AListAtEveryLimitPasses()
    {
        var args = "{\"p\":\"" + new string('x', AskLimits.MaxArgumentsBytes - 8) + "\"}";     // exactly 16384 bytes
        var asks = Enumerable.Range(0, 60).Select(i => A($"k{i}", args: args)).ToList();          // 60 x 16 KiB is under 1 MiB
        Assert.Same(asks, AskRules.Checked(asks, Declared));
        Assert.Equal(1000, AskRules.Checked([.. Enumerable.Range(0, AskLimits.MaxAsksPerReaction).Select(i => A($"k{i}"))], Declared).Count);
        Assert.Single(AskRules.Checked([A(new string('k', AskLimits.MaxKeyLength)) with { Timeout = TimeSpan.FromMilliseconds(1) }], Declared));
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void ARefusedListThrowsWithItsReason(Ask[] asks, string reason) =>
        Assert.Contains(reason, Assert.Throws<InvalidOperationException>(() => AskRules.Checked(asks, Declared)).Message, StringComparison.Ordinal);

    [Fact]
    public void NegativeTimeoutAndCallingSlotsAreRefused()
    {
        var neg = Assert.Throws<InvalidOperationException>(() => AskRules.Checked([A("k") with { Timeout = TimeSpan.FromSeconds(-1) }], Declared));
        Assert.Contains("ask 'k': the timeout must be positive", neg.Message, StringComparison.Ordinal);

        var calling = SynapseCatalog.Scan([typeof(Slotted), typeof(SlotFunctions)]).Synapses;
        var nonDeferred = Assert.Throws<InvalidOperationException>(() => AskRules.Checked([A("k", slot: "db")], calling));
        Assert.Contains("ask 'k' names slot 'db', which the unit does not declare as an Asks<> slot", nonDeferred.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NeuronIdsAndNoResultHaveTheExpectedShapes()
    {
        var brain = new BrainId("alice");
        var asksId = AskRules.AsksOf(new NeuronId(brain, "watch", "default"));
        Assert.Equal(new NeuronId(brain, "unit.asks", "watch"), asksId);

        var batchId = AskRules.BatchOf(asksId, "b1");
        Assert.Equal(new NeuronId(brain, "unit.ask.batch", "watch~b1"), batchId);
        Assert.Equal("watch~b1", batchId.Key);

        Assert.Equal(("unit.asks", "unit.ask.batch", "unit.answered"), (AskRules.AsksNeuronType, AskRules.BatchNeuronType, AskRules.AnsweredAlias));
        Assert.Equal(JsonValueKind.Null, AskRules.NoResult.ValueKind);
        Assert.Equal("null", AskRules.NoResult.GetRawText());
    }
}
