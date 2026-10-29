using DigitalBrain.Runtime.Orleans.Synapses;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Synapses;

[Signal("files.listed")]
public sealed record FilesListed;

public sealed record Unmarked;

// 04 D-06, D-12: one stream per (brain, signal namespace).
public sealed class StreamNamesFacts
{
    [Theory]
    [InlineData("files.listed", "files")]
    [InlineData("chat.message.sent", "chat.message")]
    [InlineData("ping", "ping")]
    public void The_namespace_is_the_alias_up_to_its_last_dot(string alias, string expected)
        => Assert.Equal(expected, StreamNames.SignalNamespace(alias));

    [Fact]
    public void A_signal_type_maps_to_brain_signals_namespace_keyed_by_brain()
    {
        var id = StreamNames.For("b1", typeof(FilesListed));
        Assert.Equal("brain.signals.files", id.GetNamespace());
        Assert.Equal("b1", id.GetKeyAsString());
    }

    [Fact]
    public void A_type_without_a_signal_alias_is_rejected()
        => Assert.Throws<ArgumentException>(() => StreamNames.For("b1", typeof(Unmarked)));
}
