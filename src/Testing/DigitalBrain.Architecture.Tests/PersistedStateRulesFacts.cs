using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Architecture.Tests;

public sealed class PersistedStateRulesFacts
{
    [Fact]
    public void PersistedPolymorphicMembersIncludeTheirConcreteSerializedTypes()
    {
        var states = PersistedStateRules.Discover([typeof(StateOwner), typeof(PolymorphicState), typeof(Content), typeof(TextContent)]);
        Assert.Contains(typeof(PolymorphicState), states);
        Assert.Contains(typeof(Content), states);
        Assert.Contains(typeof(TextContent), states);
    }

    private sealed class StateOwner
    {
        public StateOwner(IPersistentState<PolymorphicState> _) { }
    }

    [GenerateSerializer]
    internal sealed class PolymorphicState
    {
        [Id(0)] public Content[] Items { get; set; } = [];
    }

    [GenerateSerializer]
    internal abstract class Content;

    [GenerateSerializer]
    internal sealed class TextContent : Content
    {
        [Id(0)] public string Text { get; set; } = "";
    }

    [Fact]
    public void DuplicateAndMissingIdsAreRejectedButRetiredIdsStayReserved()
    {
        Assert.NotEmpty(PersistedStateRules.ShapeViolations(typeof(DuplicateIds), []));
        Assert.NotEmpty(PersistedStateRules.ShapeViolations(typeof(GappedIds), []));
        Assert.Empty(PersistedStateRules.ShapeViolations(typeof(GappedIds), [1]));
        Assert.NotEmpty(PersistedStateRules.ShapeViolations(typeof(MissingId), []));
        Assert.NotEmpty(PersistedStateRules.ShapeViolations(typeof(ValidState), [0]));
        Assert.Empty(PersistedStateRules.ShapeViolations(typeof(ValidState), []));
    }

    [Fact]
    public void InterfaceListsAreRejectedInsideNestedCollections()
    {
        Assert.NotEmpty(PersistedStateRules.ShapeViolations(typeof(InterfaceListState), []));
        Assert.NotEmpty(PersistedStateRules.ShapeViolations(typeof(NestedListState), []));
        Assert.Empty(PersistedStateRules.ShapeViolations(typeof(ValidState), []));
    }

    [Fact]
    public void ExistingIdsCannotBeRenumberedRemovedOrReused()
    {
        Dictionary<string, uint> previous = new() { ["Name"] = 0, ["<retired:1>"] = 1 };
        Assert.NotEmpty(PersistedStateRules.CompatibilityViolations(previous, new() { ["Name"] = 2 }));
        Assert.NotEmpty(PersistedStateRules.CompatibilityViolations(previous, new() { ["Other"] = 0 }));
        Assert.NotEmpty(PersistedStateRules.CompatibilityViolations(previous, new() { ["Name"] = 0, ["Other"] = 1 }));
        Assert.Empty(PersistedStateRules.CompatibilityViolations(previous, new() { ["Name"] = 0, ["<retired:1>"] = 1, ["Other"] = 2 }));
    }

    private sealed class DuplicateIds
    {
        [Id(0)] public int First { get; set; }
        [Id(0)] public int Second { get; set; }
    }

    private sealed class GappedIds
    {
        [Id(0)] public int First { get; set; }
        [Id(2)] public int Third { get; set; }
    }

    private sealed class MissingId
    {
        public int Value { get; set; }
    }

    private sealed class ValidState
    {
        [Id(0)] public string[] Values { get; set; } = [];
    }

    private sealed class InterfaceListState
    {
        [Id(0)] public IReadOnlyList<string> Values { get; set; } = [];
    }

    private sealed class NestedListState
    {
        [Id(0)] public Dictionary<string, IReadOnlyList<string>> Values { get; set; } = [];
    }
}
