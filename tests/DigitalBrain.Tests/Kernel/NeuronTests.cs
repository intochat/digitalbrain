using DigitalBrain.Core;
using DigitalBrain.Kernel.Kernel;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests;

[Trait("Group", "Core")]
public class NeuronTests : NeuronTestBase
{
    [Fact]
    public async Task Neuron_Activates_And_Journals_NeuronActivated()
    {
        var grain = Grain<IProbeNeuron>("demo1");
        var timeline = await grain.GetTimelineAsync();

        Assert.NotEmpty(timeline);
        Assert.Contains(timeline, s => s.Type == nameof(NeuronActivated));
    }

    [Fact]
    public async Task FireAsync_Persists_And_Replayable()
    {
        var grain = Grain<IProbeNeuron>("demo2");
        await grain.FireAsync(new ProbeMessageSynapse("hello from test"));

        var timeline = await grain.GetTimelineAsync();
        Assert.Contains(timeline, s => s.Type == nameof(ProbeMessageSynapse));
    }

    [Fact]
    public async Task Timeline_Returns_CopySafe_Json_Payloads_Without_Raw_Json_Strings()
    {
        var grain = Grain<IProbeNeuron>("json-payloads");
        await grain.FireJsonSignalAsync("JsonPayload", """
            {
              "nested": { "name": "demo", "count": 2 },
              "items": [1, { "flag": true }]
            }
            """);

        var signal = Assert.Single((await grain.GetOutgoingTimelineAsync()).OfType<Signal>(), s => s.Name == "JsonPayload");
        var nested = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(signal.Props["nested"]);
        Assert.Equal("demo", nested["name"]);
        Assert.Equal(2L, Convert.ToInt64(nested["count"]));

        var items = Assert.IsAssignableFrom<IReadOnlyList<object?>>(signal.Props["items"]);
        Assert.Equal(1L, Convert.ToInt64(items[0]));
        var item = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(items[1]);
        Assert.Equal(true, item["flag"]);
    }

    [Fact]
    public void AutomationRecords_Are_Synapses_And_Construct_Correctly()
    {
        var script = new RegisterScript("daily-brief", "return Array.Empty<Synapse>();", "demo script");
        var reaction = new RegisterReaction("on-my-activate", "NeuronActivated", "daily-brief", "MyNeuron", Array.Empty<string>(), "default", null);
        var app = new AutomationApp("my-app", "example app");

        Assert.IsAssignableFrom<Synapse>(script);
        Assert.Equal(nameof(RegisterScript), script.Type);
        Assert.Equal("NeuronActivated", reaction.When);
        Assert.Equal("MyNeuron", reaction.Target);
        Assert.Equal("daily-brief", reaction.ScriptRef);
        Assert.NotNull(app);
    }

}
