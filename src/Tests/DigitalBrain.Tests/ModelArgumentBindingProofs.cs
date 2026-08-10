using DigitalBrain.AI;
using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ModelArgumentBindingProofs
{
    [Fact]
    public void NeuronIdStringsBindAsIdentities()
    {
        var owner = new OwnerId("dev");
        Dictionary<string, object?> arguments = new()
        {
            ["connectionId"] = "timer_to_chat",
            ["source"] = "timer:dev/default",
            ["synapseAlias"] = "time.timer-elapsed",
            ["target"] = "chat:main",
            ["transform"] = "to:ui.note{Text=Note}",
        };

        var bound = (Connect)SynapseCapabilityTool.BindModelArguments(
            typeof(Connect), "db.connect", arguments, owner);

        Assert.Equal(new NeuronId("timer", owner, "default"), bound.Source);
        Assert.Equal(new NeuronId("chat", owner, "main"), bound.Target);
    }

    [Fact]
    public void NeuronIdStringWithoutAnInstanceIsRefusedWithTheAcceptedForms()
    {
        var owner = new OwnerId("dev");
        Dictionary<string, object?> arguments = new()
        {
            ["connectionId"] = "half-an-identity",
            ["source"] = "timer",
            ["synapseAlias"] = "time.timer-elapsed",
            ["target"] = "chat:main",
        };

        var refused = Assert.Throws<InvalidOperationException>(
            () => SynapseCapabilityTool.BindModelArguments(typeof(Connect), "db.connect", arguments, owner));

        Assert.Contains("type:name", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StableNameBindsToADeterministicConnectionId()
    {
        var owner = new OwnerId("dev");
        Dictionary<string, object?> arguments = new()
        {
            ["connectionId"] = "chat_to_dashboard_sync",
            ["source"] = new NeuronId("chat", owner, "main"),
            ["synapseAlias"] = "chat.responded",
            ["target"] = new NeuronId("chart", owner, "dashboard"),
        };

        var first = (Connect)SynapseCapabilityTool.BindModelArguments(typeof(Connect), "db.connect", arguments);
        var again = (Connect)SynapseCapabilityTool.BindModelArguments(typeof(Connect), "db.connect", arguments);

        Assert.NotEqual(Guid.Empty, first.ConnectionId);
        Assert.Equal(first.ConnectionId, again.ConnectionId);
        Assert.Equal("chat.responded", first.SynapseAlias);
    }

    [Fact]
    public void RealGuidsStillBindAsThemselves()
    {
        var owner = new OwnerId("dev");
        var connectionId = Guid.NewGuid();
        Dictionary<string, object?> arguments = new()
        {
            ["connectionId"] = connectionId.ToString("D"),
            ["source"] = new NeuronId("chat", owner, "main"),
            ["synapseAlias"] = "chat.responded",
            ["target"] = new NeuronId("chart", owner, "dashboard"),
        };

        var bound = (Connect)SynapseCapabilityTool.BindModelArguments(typeof(Connect), "db.connect", arguments);

        Assert.Equal(connectionId, bound.ConnectionId);
    }
}
