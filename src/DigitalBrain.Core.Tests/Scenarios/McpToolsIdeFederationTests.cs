using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class McpToolsIdeFederationTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain
            .AddModule<McpGateway>()
            .AddModule<IntrospectionCatalog>()
            .AddModule<McpAuditLedger>();

    [Fact(DisplayName =
        "MCP IDE federation: McpToolInvoked → ActiveNeuronsAsked/Answered → McpToolCompleted; Cause chain holds")]
    public async Task ListActiveNeuronsToolCompletesWithJournaledAnswer()
    {
        var ct = Cancellation;
        var context = "owner-ide";
        var session = Brain.Session(context);
        var gatewayId = new NeuronId("mcpgateway", context);
        var introId = new NeuronId("introspectioncatalog", context);
        var auditId = new NeuronId("mcpauditledger", context);

        await session.EmitAsync(
            new McpToolInvokeRequested(
                Tool: McpGateway.ToolListActive,
                ArgsHash: "args-empty",
                ClientId: "cursor-1",
                OwnerBound: context,
                Mutating: false),
            ct);

        var gatewayDone = await WaitForJournalAsync(
            gatewayId,
            reading => reading.AllSaid<McpToolInvoked>().Count == 1
                && reading.AllSaid<ActiveNeuronsAsked>().Count == 1
                && reading.AllHeard<ActiveNeuronsAnswered>().Count == 1
                && reading.AllSaid<McpToolCompleted>().Count == 1,
            "gateway completed list_active_neurons",
            ct);

        var auditDone = await WaitForJournalAsync(
            auditId,
            reading => reading.AllHeard<McpToolInvoked>().Count == 1
                && reading.AllHeard<McpToolCompleted>().Count == 1,
            "audit heard invoke + complete",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var invokeReq = sessionReading.SaidSingle<McpToolInvokeRequested>();
        Assert.Equal("declared", invokeReq.DeliveryTo(gatewayId).Via);

        var invoked = gatewayDone.SaidSingle<McpToolInvoked>();
        Assert.Equal(new SynapseRef(session.Id, invokeReq.Position), invoked.Cause);
        Assert.Equal("declared", invoked.DeliveryTo(auditId).Via);
        Assert.Equal(McpGateway.ToolListActive, Assert.IsType<McpToolInvoked>(invoked.Body).Tool);

        var asked = gatewayDone.SaidSingle<ActiveNeuronsAsked>();
        Assert.Equal("ask", asked.DeliveryTo(introId).Via);
        Assert.Equal(context, Assert.IsType<ActiveNeuronsAsked>(asked.Body).OwnerBound);

        var introReading = await ReadAsync(introId, ct);
        var answered = introReading.SaidSingle<ActiveNeuronsAnswered>();
        Assert.Equal(new SynapseRef(gatewayId, asked.Position), answered.Answers);
        Assert.Equal(2, Assert.IsType<ActiveNeuronsAnswered>(answered.Body).Neurons.Length);

        var completed = gatewayDone.SaidSingle<McpToolCompleted>();
        Assert.Equal(new SynapseRef(introId, answered.Position), completed.Cause);
        Assert.Equal("declared", completed.DeliveryTo(auditId).Via);
        var done = Assert.IsType<McpToolCompleted>(completed.Body);
        Assert.True(done.Ok);
        Assert.Equal(2, done.ResultCount);
        Assert.Equal(gatewayId, auditDone.HeardSingle<McpToolCompleted>().Metadata.Source);
    }

    [Fact(DisplayName =
        "MCP IDE federation: mutating tool journals McpApprovalRequired with zero complete until McpUserApproved")]
    public async Task MutatingToolGatesOnApproval()
    {
        var ct = Cancellation;
        var context = "owner-ide-mutate";
        var session = Brain.Session(context);
        var gatewayId = new NeuronId("mcpgateway", context);
        var auditId = new NeuronId("mcpauditledger", context);

        await session.EmitAsync(
            new McpToolInvokeRequested(
                Tool: McpGateway.ToolActivateBehavior,
                ArgsHash: "behavior-probe",
                ClientId: "cursor-2",
                OwnerBound: context,
                Mutating: true),
            ct);

        var before = await WaitForJournalAsync(
            gatewayId,
            reading => reading.AllSaid<McpToolInvoked>().Count == 1
                && reading.AllSaid<McpApprovalRequired>().Count == 1,
            "invoke + approval required",
            ct);

        Assert.Empty(before.AllSaid<McpToolCompleted>());
        Assert.Empty(before.AllSaid<BehaviorActivateCompleted>());

        var approval = Assert.IsType<McpApprovalRequired>(before.SaidSingle<McpApprovalRequired>().Body);
        await session.EmitAsync(new McpUserApproved(approval.BundleId, approval.Tool), ct);

        var after = await WaitForJournalAsync(
            gatewayId,
            reading => reading.AllSaid<McpToolCompleted>().Count == 1
                && reading.AllSaid<BehaviorActivateCompleted>().Count == 1,
            "complete after approval",
            ct);

        await WaitForJournalAsync(
            auditId,
            reading => reading.AllHeard<McpToolCompleted>().Count == 1
                && reading.AllHeard<BehaviorActivateCompleted>().Count == 1,
            "audit heard post-approval facts",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var approvedSaid = sessionReading.SaidSingle<McpUserApproved>();
        var completed = after.SaidSingle<McpToolCompleted>();
        Assert.Equal(new SynapseRef(session.Id, approvedSaid.Position), completed.Cause);
        Assert.True(Assert.IsType<McpToolCompleted>(completed.Body).Ok);
    }
}
