using DigitalBrain.Introspection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class GraphCapabilityManifestProofs
{
    [Fact]
    public void GraphWiringVerbsAreDeclaredCapabilities()
    {
        var graph = Assert.Single(
            IntrospectionModule.Capabilities.Neurons,
            neuron => neuron.ContractId == "db.synapse-graph");

        Assert.Contains(graph.Accepted, synapse => synapse.ContractId == "db.connect");
        Assert.Contains(graph.Accepted, synapse => synapse.ContractId == "db.disconnect");
        Assert.Contains(graph.Emitted, synapse => synapse.ContractId == "db.connected");
        Assert.Contains(graph.Emitted, synapse => synapse.ContractId == "db.disconnected");
    }
}
