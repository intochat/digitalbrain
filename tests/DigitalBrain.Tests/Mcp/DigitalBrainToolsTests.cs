extern alias McpProject;
using DigitalBrain.Core;
using DigitalBrain.TestKit;
using DigitalBrain.Tests.TestSupport;
using McpProject::DigitalBrain.Mcp;

namespace DigitalBrain.Tests.Mcp;

// The MCP tools are co-hosted in the silo and resolve grains via an in-process IGrainFactory.
// These tests exercise that exact path without an HTTP transport.
public class DigitalBrainToolsTests : NeuronTestBase
{
    [Fact]
    public void Ping_Works_Standalone()
        => Assert.Contains("connected", DigitalBrainReadTools.PingDigitalBrain(), StringComparison.OrdinalIgnoreCase);

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

        await mutationTools.DefineReaction(
            id: "mcp-auto-approve",
            when: "NeuronActivated",
            target: "personal-assistant",
            scriptCode: "return new[] { new Signal(\"DailyBriefGenerated\", null) };");

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

    [Fact]
    public async Task CreateAutomationFromDescription_Stages_Automation_Proposal()
    {
        var factory = new TestGrainFactory(this);
        var mutationTools = new DigitalBrainMutationTools(factory);

        var result = await mutationTools.CreateAutomationFromDescription(
            "when poll for new leads from Salesforce then emit LeadCreated signals with name",
            "sf-chat-example");

        Assert.Contains("Staged automation", result);
        Assert.Contains("sf-chat-example", result);

        var automation = Grain<IAutomationNeuron>("automation-main");
        Assert.Contains(
            (await automation.GetOutgoingTimelineAsync()).OfType<AutomationDefinitionStaged>(),
            staged => staged.Reaction.Id == "sf-chat-example");
    }

    [Fact]
    public async Task GetCausalLineage_Returns_ReadOnly_Structured_Journal_Data()
    {
        var ino = Grain<IInoNeuron>("ino-main");
        await ino.FireAsync(new InoRequest("what can you do?", "mcp-lineage-client"));

        var response = Assert.Single((await ino.GetOutgoingTimelineAsync()).OfType<InoResponse>());
        var correlationId = response.CorrelationId ?? response.SynapseId;

        var factory = new TestGrainFactory(this);
        var readTools = new DigitalBrainReadTools(factory);
        var json = await readTools.GetCausalLineage("ino-main", correlationId);

        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("ino-main", document.RootElement.GetProperty("neuronId").GetString());
        Assert.Equal(correlationId, document.RootElement.GetProperty("correlationId").GetString());
        Assert.True(document.RootElement.GetProperty("count").GetInt32() > 0);

        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToList();
        Assert.Contains(entries, entry => entry.GetProperty("type").GetString() == nameof(InoRequest));
        Assert.Contains(entries, entry => entry.GetProperty("type").GetString() == nameof(InoResponse));
    }
}

