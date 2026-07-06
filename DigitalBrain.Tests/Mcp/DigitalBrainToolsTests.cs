using DigitalBrain.Core;
using DigitalBrain.Mcp;
using DigitalBrain.Tests.TestSupport;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Mcp;

// The MCP tools are co-hosted in the silo and resolve grains via an in-process IGrainFactory.
// These tests exercise that exact path without an HTTP transport.
public class DigitalBrainToolsTests : NeuronTestBase
{
    [Fact]
    public void Ping_Works_Standalone()
        => Assert.Contains("connected", DigitalBrainReadTools.PingDigitalBrain(), StringComparison.OrdinalIgnoreCase);

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

    [Fact]
    public async Task DefineReaction_Stages_Approval_Without_Registering_Script_Or_Reaction()
    {
        var factory = new TestGrainFactory(this);
        var mutationTools = new DigitalBrainMutationTools(factory);

        var result = await mutationTools.DefineReaction(
            "mcp-auto-stage",
            "NeuronActivated",
            "personal-assistant",
            "return new[] { new Signal(\"McpAutomationFired\", null) };");

        Assert.Contains("Staged reaction", result);

        var automation = Grain<IAutomationNeuron>("automation-main");
        var automationTimeline = await automation.GetOutgoingTimelineAsync();
        var staged = Assert.Single(automationTimeline.OfType<AutomationDefinitionStaged>(), item => item.Reaction.Id == "mcp-auto-stage");
        Assert.DoesNotContain(automationTimeline.OfType<RegisterScript>(), script => script.Id == "mcp-auto-stage-script");
        Assert.DoesNotContain(automationTimeline.OfType<RegisterReaction>(), reaction => reaction.Id == "mcp-auto-stage");

        var approval = Grain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        var approvalTimeline = await approval.GetOutgoingTimelineAsync();
        Assert.Contains(approvalTimeline.OfType<SelfEvolutionProposalPending>(), pending =>
            pending.ProposalId == staged.ProposalId
            && pending.ApplyVia == SelfEvolutionApplyVia.AutomationDefineReaction
            && pending.Risk == SelfEvolutionRisk.InProcessCode);
    }

    [Fact]
    public async Task Rejected_DefineReaction_Proposal_Does_Not_Register_Automation()
    {
        var factory = new TestGrainFactory(this);
        var mutationTools = new DigitalBrainMutationTools(factory);

        await mutationTools.DefineReaction(
            "mcp-auto-reject",
            "NeuronActivated",
            null,
            "return new[] { new Signal(\"RejectedAutomationFired\", null) };");

        var automation = Grain<IAutomationNeuron>("automation-main");
        var staged = Assert.Single((await automation.GetOutgoingTimelineAsync()).OfType<AutomationDefinitionStaged>(), item => item.Reaction.Id == "mcp-auto-reject");

        var approval = Grain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        await approval.DeliverAsync(new SelfEvolutionDecision(staged.ProposalId, Approved: false, DecidedBy: "user:owner", Reason: "deny"));

        var automationTimeline = await automation.GetOutgoingTimelineAsync();
        Assert.DoesNotContain(automationTimeline.OfType<RegisterScript>(), script => script.Id == "mcp-auto-reject-script");
        Assert.DoesNotContain(automationTimeline.OfType<RegisterReaction>(), reaction => reaction.Id == "mcp-auto-reject");
    }

    [Fact]
    public async Task Approved_DefineReaction_Proposal_Registers_Script_And_Reaction()
    {
        var factory = new TestGrainFactory(this);
        var mutationTools = new DigitalBrainMutationTools(factory);

        await mutationTools.CreateAutomationFromDescription(
            "when personal-assistant activates emit DailyBriefGenerated",
            "mcp-auto-approve");

        var automation = Grain<IAutomationNeuron>("automation-main");
        var staged = Assert.Single((await automation.GetOutgoingTimelineAsync()).OfType<AutomationDefinitionStaged>(), item => item.Reaction.Id == "mcp-auto-approve");

        var approval = Grain<ISelfEvolutionNeuron>(SelfEvolutionNeuronIds.Main);
        await approval.DeliverAsync(new SelfEvolutionDecision(staged.ProposalId, Approved: true, DecidedBy: "user:owner"));

        var automationTimeline = await automation.GetOutgoingTimelineAsync();
        Assert.Contains(automationTimeline.OfType<RegisterScript>(), script => script.Id == "mcp-auto-approve-script");
        Assert.Contains(automationTimeline.OfType<RegisterReaction>(), reaction => reaction.Id == "mcp-auto-approve");

        var approvalTimeline = await approval.GetOutgoingTimelineAsync();
        Assert.Contains(approvalTimeline.OfType<SelfEvolutionApplyResult>(), result =>
            result.ProposalId == staged.ProposalId && result.Succeeded);
    }
}

