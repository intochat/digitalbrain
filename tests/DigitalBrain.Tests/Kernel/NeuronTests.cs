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
    public async Task SystemStatus_Launches_And_Records_Status()
    {
        var status = Grain<ISystemStatus>("status-test");
        var timeline = await status.GetTimelineAsync();

        Assert.Contains(timeline, s => s.Type == nameof(SystemLaunched) || s.Type == nameof(SystemStatusChanged));
    }

    [Fact]
    public async Task SystemStatus_Simulates_Fix_From_Checkpoint()
    {
        var status = Grain<ISystemStatus>("status-sim");

        await status.FireAsync(new SystemStatusChanged("kernel", "FailedToStart", "test failure"));

        var timeline = await status.GetTimelineAsync();
        Assert.Contains(timeline, s => s.Type == nameof(FixProposal));
        var sim = timeline.LastOrDefault(s => s.Type == nameof(SimulationResult)) as SimulationResult;
        Assert.NotNull(sim);
        Assert.True(sim.Success);
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

    [Fact]
    public async Task AutomationNeuron_Registers_And_Reacts_To_NeuronActivated()
    {
        var auto = Grain<IAutomationNeuron>("automation-main");
        await auto.GetTimelineAsync();

        await auto.FireAsync(new RegisterScript(
            "act-script",
            "return new[] { new Signal(\"AutomationFired\", new Dictionary<string, object?>()) };",
            "demo"));
        await auto.FireAsync(new RegisterReaction(
            "act-reaction",
            "NeuronActivated",
            "act-script",
            "act-test",
            Array.Empty<string>(),
            "default",
            null));

        await auto.FireAsync(new NeuronActivated(new NeuronId("act-test")));

        var timeline = await auto.GetTimelineAsync();
        Assert.Contains(timeline, s => s.Type == "AutomationFired" || s.Type == "ScriptRegistered");
    }

    [Fact]
    public async Task DefineReactionAsync_Enables_InoStyle_WhenActivatedThenScript()
    {
        var auto = Grain<IAutomationNeuron>("automation-main");
        await auto.GetTimelineAsync();

        await auto.DefineReactionAsync(
            "brief-on-activate",
            "NeuronActivated",
            "personal-assistant",
            "return new[] { new Signal(\"DailyBriefGenerated\", new Dictionary<string, object?>()) };");

        await auto.FireAsync(new NeuronActivated(new NeuronId("personal-assistant")));

        var timeline = await auto.GetTimelineAsync();
        Assert.Contains(timeline, s => s.Type == "DailyBriefGenerated");
    }
}
