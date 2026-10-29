using DigitalBrain.Runtime.Orleans.Synapses;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Synapses;

public sealed class ReactionCursorFacts
{
    [Fact]
    public void NestedSameNeuronReactionAdvancesItsOuterCursor()
    {
        var id = GrainId.Create("neuron.host", "brain/probe/listener");
        using var outer = ReactionCursor.Open(id, () => [], "52");
        using (var inner = ReactionCursor.Open(id, () => [], "52"))
        {
            inner.Cursor.Committed("58");
        }

        Assert.Same(outer.Cursor, ReactionCursor.For(id));
        Assert.Equal("58", outer.Cursor.ETag);
        Assert.True(outer.Cursor.Written);
    }

    [Fact]
    public void NestedOtherNeuronReactionDoesNotAdvanceTheOuterCursor()
    {
        var id = GrainId.Create("neuron.host", "brain/probe/listener");
        using var outer = ReactionCursor.Open(id, () => [], "52");
        using (var inner = ReactionCursor.Open(GrainId.Create("neuron.host", "brain/probe/other"), () => [], "53"))
        {
            inner.Cursor.Committed("58");
        }

        Assert.Equal("52", outer.Cursor.ETag);
        Assert.False(outer.Cursor.Written);
    }
}
