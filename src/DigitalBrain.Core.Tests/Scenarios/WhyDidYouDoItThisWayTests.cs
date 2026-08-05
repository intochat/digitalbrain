using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed class WhyDidYouDoItThisWayTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<InstructionalAgent>().AddModule<ActionLedger>();

    [Fact(DisplayName = "Why did you do it this way? - cite prior journaled user instruction")]
    public async Task WhyAnswerCitesJournaledInstructionBeforeAction()
    {
        var ct = Cancellation;
        var context = "owner-desk";
        var session = Brain.Session(context);
        var agentId = new NeuronId("instructionalagent", context);
        var ledgerId = new NeuronId("actionledger", context);
        var instructionText = "Always keep outbound sales email under 80 words.";
        var scope = "email.outbound";
        var action = "send-sales-email";

        await session.EmitAsync(new UserInstruction(instructionText, scope), ct);

        var afterInstruction = await WaitForJournalAsync(
            agentId,
            reading => reading.AllHeard<UserInstruction>().Count == 1,
            "UserInstruction heard on instructional agent",
            ct);

        var instructionHeard = afterInstruction.HeardSingle<UserInstruction>();
        Assert.Equal(instructionText, Assert.IsType<UserInstruction>(instructionHeard.Body).Text);
        Assert.Equal(scope, Assert.IsType<UserInstruction>(instructionHeard.Body).Scope);

        await session.EmitAsync(
            new PerformOutboundAction(action, Detail: "Short intro to Acme"),
            ct);

        var ledgerReading = await WaitForJournalAsync(
            ledgerId,
            reading => reading.AllHeard<AgentActionTaken>().Count == 1,
            "AgentActionTaken heard on action ledger",
            ct);

        var actionHeard = ledgerReading.HeardSingle<AgentActionTaken>();
        var taken = Assert.IsType<AgentActionTaken>(actionHeard.Body);
        Assert.Equal(action, taken.Action);
        Assert.Equal(scope, taken.AppliedScope);

        var agentAfterAction = await ReadAsync(agentId, ct);
        var instructionPosition = agentAfterAction.HeardSingle<UserInstruction>().Position;
        var performHeard = agentAfterAction.HeardSingle<PerformOutboundAction>();
        var actionSaid = agentAfterAction.SaidSingle<AgentActionTaken>();
        Assert.True(
            instructionPosition < actionSaid.Position,
            "UserInstruction must be journaled before AgentActionTaken in the same context.");
        Assert.Equal(session.Id, performHeard.Metadata.Source);
        Assert.Equal(new SynapseRef(session.Id, performHeard.Metadata.Sequence), actionSaid.Cause);
        Assert.Equal("declared", actionSaid.DeliveryTo(ledgerId).Via);

        var sessionReading = await ReadAsync(session.Id, ct);
        var instructionSaid = sessionReading.SaidSingle<UserInstruction>();
        var performSaid = sessionReading.SaidSingle<PerformOutboundAction>();
        Assert.True(instructionSaid.Position < performSaid.Position);
        Assert.Equal("declared", instructionSaid.DeliveryTo(agentId).Via);
        Assert.Equal("declared", performSaid.DeliveryTo(agentId).Via);
        Assert.Equal(session.Id, instructionHeard.Metadata.Source);
        Assert.Equal(instructionSaid.Position, instructionHeard.Metadata.Sequence);

        var why = await session.AskAsync<WhyAnswer>(new WhyAsked(action), ct);
        Assert.Equal(instructionText, why.InstructionText);
        Assert.Equal(scope, why.InstructionScope);
        Assert.Equal(action, why.Action);
        Assert.Contains(instructionText, why.Detail, StringComparison.Ordinal);

        // Re-read journals: WhyAnswer content is exactly the earlier journaled instruction body.
        var agentFinal = await ReadAsync(agentId, ct);
        var journaledInstruction = Assert.IsType<UserInstruction>(
            agentFinal.HeardSingle<UserInstruction>().Body);
        Assert.Equal(journaledInstruction.Text, why.InstructionText);
        Assert.Equal(journaledInstruction.Scope, why.InstructionScope);

        var whyAskSaid = (await ReadAsync(session.Id, ct)).SaidSingle<WhyAsked>();
        var whySaid = agentFinal.SaidSingle<WhyAnswer>();
        Assert.Equal(new SynapseRef(session.Id, whyAskSaid.Position), whySaid.Answers);
        Assert.Equal(instructionText, Assert.IsType<WhyAnswer>(whySaid.Body).InstructionText);
    }
}
