using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class MultitoolTurnApprovalGateTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<MultitoolAssistant>()
            .AddModule<ApprovalTray>()
            .AddModule<ToolLedger>()
            .AddModule<MultitoolSpeechLedger>();

    [Fact(DisplayName = "Multi-tool turn: tools complete only after UserApproved; Cause chain holds")]
    public async Task ToolsGateOnApprovalThenBothComplete()
    {
        var ct = Cancellation;
        var context = "acme-renewal";
        var session = Brain.Session(context);
        var assistantId = new NeuronId("multitoolassistant", context);
        var trayId = new NeuronId("approvaltray", context);
        var toolLedgerId = new NeuronId("toolledger", context);
        var speechId = new NeuronId("multitoolspeechledger", context);
        var goal = "prep Acme renewal: pull account and draft discount email";

        await session.EmitAsync(new MultitoolUserMessaged(goal), ct);

        var assistantAfterPlan = await WaitForJournalAsync(
            assistantId,
            reading => reading.AllSaid<CapabilityToolSelected>().Count == 2
                && reading.AllSaid<ApprovalRequired>().Count == 1,
            "two CapabilityToolSelected and one ApprovalRequired",
            ct);

        // Without approval, side-effect completions must not exist.
        Assert.Empty(assistantAfterPlan.AllSaid<ToolCompleted>());
        Assert.Empty(assistantAfterPlan.AllSaid<MultitoolAssistantSaid>());
        var toolLedgerBefore = await ReadAsync(toolLedgerId, ct);
        Assert.Empty(toolLedgerBefore.AllHeard<ToolCompleted>());

        var sessionReading = await ReadAsync(session.Id, ct);
        var userSaid = sessionReading.SaidSingle<MultitoolUserMessaged>();
        Assert.Equal("declared", userSaid.DeliveryTo(assistantId).Via);

        var userHeard = assistantAfterPlan.HeardSingle<MultitoolUserMessaged>();
        Assert.Equal(session.Id, userHeard.Metadata.Source);
        Assert.Equal(userSaid.Position, userHeard.Metadata.Sequence);

        var selected = assistantAfterPlan.AllSaid<CapabilityToolSelected>();
        Assert.All(selected, said =>
        {
            Assert.Equal(new SynapseRef(session.Id, userSaid.Position), said.Cause);
            Assert.Equal("declared", said.DeliveryTo(trayId).Via);
        });
        var toolNames = selected
            .Select(said => Assert.IsType<CapabilityToolSelected>(said.Body).ToolName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [MultitoolAssistant.ToolAccountPull, MultitoolAssistant.ToolEmailDraft],
            toolNames);

        var approvalSaid = assistantAfterPlan.SaidSingle<ApprovalRequired>();
        Assert.Equal(new SynapseRef(session.Id, userSaid.Position), approvalSaid.Cause);
        Assert.Equal("declared", approvalSaid.DeliveryTo(trayId).Via);
        var approval = Assert.IsType<ApprovalRequired>(approvalSaid.Body);
        Assert.Equal(2, approval.Tools.Count);
        Assert.Contains(MultitoolAssistant.ToolAccountPull, approval.Tools);
        Assert.Contains(MultitoolAssistant.ToolEmailDraft, approval.Tools);

        await session.EmitAsync(
            new UserApproved(approval.BundleId, [MultitoolAssistant.ToolAccountPull, MultitoolAssistant.ToolEmailDraft]),
            ct);

        var assistantDone = await WaitForJournalAsync(
            assistantId,
            reading => reading.AllSaid<ToolCompleted>().Count == 2
                && reading.AllSaid<MultitoolAssistantSaid>().Count == 1,
            "both ToolCompleted and MultitoolAssistantSaid after approval",
            ct);

        var toolLedger = await WaitForJournalAsync(
            toolLedgerId,
            reading => reading.AllHeard<ToolCompleted>().Count == 2,
            "ToolLedger heard both completions",
            ct);

        var sessionAfter = await ReadAsync(session.Id, ct);
        var approvedSaid = sessionAfter.SaidSingle<UserApproved>();
        Assert.Equal("declared", approvedSaid.DeliveryTo(assistantId).Via);
        Assert.Equal(approval.BundleId, Assert.IsType<UserApproved>(approvedSaid.Body).BundleId);

        var approvedHeard = assistantDone.HeardSingle<UserApproved>();
        Assert.Equal(session.Id, approvedHeard.Metadata.Source);
        Assert.Equal(approvedSaid.Position, approvedHeard.Metadata.Sequence);

        var completed = assistantDone.AllSaid<ToolCompleted>();
        Assert.Equal(2, completed.Count);
        Assert.All(completed, said =>
        {
            Assert.Equal(new SynapseRef(session.Id, approvedSaid.Position), said.Cause);
            Assert.Equal("declared", said.DeliveryTo(toolLedgerId).Via);
            Assert.Equal(approval.BundleId, Assert.IsType<ToolCompleted>(said.Body).BundleId);
        });
        var completedNames = completed
            .Select(said => Assert.IsType<ToolCompleted>(said.Body).ToolName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [MultitoolAssistant.ToolAccountPull, MultitoolAssistant.ToolEmailDraft],
            completedNames);

        foreach (var said in completed)
        {
            var heard = toolLedger.AllHeard<ToolCompleted>()
                .Single(entry => entry.Metadata.Sequence == said.Position);
            Assert.Equal(assistantId, heard.Metadata.Source);
            Assert.Equal(said.Position, heard.Metadata.Sequence);
        }

        var speechSaid = assistantDone.SaidSingle<MultitoolAssistantSaid>();
        Assert.Equal(new SynapseRef(session.Id, approvedSaid.Position), speechSaid.Cause);
        Assert.Equal("declared", speechSaid.DeliveryTo(speechId).Via);
        Assert.Contains(
            "after approval",
            Assert.IsType<MultitoolAssistantSaid>(speechSaid.Body).Text,
            StringComparison.Ordinal);

        // Still exactly two tools — no pre-approval completions sneaked in.
        Assert.Equal(2, assistantDone.AllSaid<ToolCompleted>().Count);
        Assert.Equal(2, assistantDone.AllSaid<CapabilityToolSelected>().Count);
        Assert.Single(assistantDone.AllSaid<ApprovalRequired>());
    }
}
