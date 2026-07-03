using DigitalBrain.Core;
using DigitalBrain.Mcp.Tools;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Mcp;

// The MCP tools are co-hosted in the silo and resolve grains via an in-process IGrainFactory.
// These tests exercise that exact path (TestCluster grain factory) without an HTTP transport.
public class DigitalBrainToolsTests : NeuronTestBase
{
    [Fact]
    public void Ping_Works_Standalone()
        => Assert.Contains("connected", DigitalBrainReadTools.PingDigitalBrain(), System.StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task Publish_Then_List_Through_InProcess_GrainFactory()
    {
        var factory = new TestGrainFactory(this);
        var mutationTools = new DigitalBrainMutationTools(factory);
        var readTools = new DigitalBrainReadTools(factory);

        await mutationTools.PublishToMarketplace("McpPack", "1.0", "public class P {}", "mcp-user", false, 0.15);
        var listing = await readTools.ListMarketplace();

        Assert.Contains("McpPack@1.0", listing);
    }

    [Fact]
    public async Task SimulateXPost_broadcasts_XPostReceived_signal()
    {
        var factory = new TestGrainFactory(this);
        var mutationTools = new DigitalBrainMutationTools(factory);

        await mutationTools.SimulateXPost("elon", "big news", 7);

        var ingress = Grain<IIngressNeuron>("ingress-main");
        Signal? signal = null;
        for (var attempt = 0; attempt < 20 && signal is null; attempt++)
        {
            await Task.Delay(50);
            var timeline = await ingress.GetOutgoingTimelineAsync();
            signal = timeline.OfType<Signal>().FirstOrDefault(s => s.Name == "XPostReceived");
        }

        Assert.NotNull(signal);
        Assert.Equal("elon", signal!.Props["author"]);
        Assert.Equal("big news", signal.Props["text"]);
        Assert.Equal(7L, signal.Props["chatId"]);
    }
}
