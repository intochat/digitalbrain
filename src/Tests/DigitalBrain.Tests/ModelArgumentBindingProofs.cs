using DigitalBrain.AI;
using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ModelArgumentBindingProofs
{
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
