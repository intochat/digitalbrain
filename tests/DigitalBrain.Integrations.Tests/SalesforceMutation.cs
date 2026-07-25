using DigitalBrain.Abstractions;
using DigitalBrain.Salesforce;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class SalesforceMutation(IntegrationsFixture fixture)
{
    private const string AccountId = "001xx000003DGbYAAW";

    [Fact(DisplayName =
        "ISalesforce.ProposeAccountDescription returns AwaitingApproval without opening MCP")]
    public async Task ProposeReturnsAwaitingApprovalWithoutMcp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var driver = test.Neuron<IIntegrationDriver>("sf-propose");
        var commandId = CommandId.New();

        var proposed = await driver.Reference.ProposeSalesforceAccountDescription(
            commandId,
            AccountId,
            "Enrichment from email",
            cancellationToken);

        Assert.Equal(commandId, proposed.CommandId);
        Assert.Equal(AccountId, proposed.AccountId);
        Assert.Equal("Enrichment from email", proposed.Description);
        Assert.False(string.IsNullOrWhiteSpace(proposed.Fingerprint));
        Assert.Equal(SalesforceMutationState.AwaitingApproval, proposed.State);
        Assert.Equal(0, test.Mcp().SessionCount);

        var again = await driver.Reference.ProposeSalesforceAccountDescription(
            commandId,
            AccountId,
            "Enrichment from email",
            cancellationToken);
        Assert.Equal(proposed, again);
        Assert.Equal(0, test.Mcp().SessionCount);
    }

    [Fact(DisplayName =
        "ISalesforce.ApproveAccountDescription rejects mismatched human approval evidence before MCP")]
    public async Task ApproveRejectsMismatchedEvidenceWithoutMcp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var driver = test.Neuron<IIntegrationDriver>("sf-reject");
        var commandId = CommandId.New();
        var proposed = await driver.Reference.ProposeSalesforceAccountDescription(
            commandId,
            AccountId,
            "Needs human approval",
            cancellationToken);

        var approver = SessionOf(test);
        var recorded = new SalesforceMutationApproval(
            Guid.NewGuid(),
            commandId,
            proposed.Fingerprint,
            approver,
            test.Clock.UtcNow);
        var delivered = driver.Incoming.NextAsync<SalesforceMutationApproval>(cancellationToken);
        await test.Client.SendAsync(driver.Id, recorded);
        Assert.Equal(recorded, (await delivered).Synapse);

        var mismatched = recorded with { ApprovalId = Guid.NewGuid() };
        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            driver.Reference.ApproveSalesforceWithMismatchedEvidence(
                mismatched,
                recorded,
                cancellationToken));

        Assert.Contains("approval evidence", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, test.Mcp().SessionCount);
        Assert.Equal(
            SalesforceMutationState.AwaitingApproval,
            (await driver.Reference.ProposeSalesforceAccountDescription(
                commandId,
                AccountId,
                "Needs human approval",
                cancellationToken)).State);
    }

    [Fact(DisplayName =
        "ISalesforce.ApproveAccountDescription completes after admitted MCP update on the scripted edge")]
    public async Task ApproveCompletesThroughScriptedMcpEdge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string description = "Completed enrichment body";
        var driver = test.Neuron<IIntegrationDriver>("sf-complete");
        var commandId = CommandId.New();
        var proposed = await driver.Reference.ProposeSalesforceAccountDescription(
            commandId,
            AccountId,
            description,
            cancellationToken);

        test.Mcp().Catalog(
            "salesforce",
            AdmittedMcpTools.SalesforceUpdateAccount(),
            AdmittedMcpTools.SalesforceSoqlQuery(AccountId, description));

        var approval = new SalesforceMutationApproval(
            Guid.NewGuid(),
            commandId,
            proposed.Fingerprint,
            SessionOf(test),
            test.Clock.UtcNow);
        var delivered = driver.Incoming.NextAsync<SalesforceMutationApproval>(cancellationToken);
        await test.Client.SendAsync(driver.Id, approval);
        Assert.Equal(approval, (await delivered).Synapse);

        var completed = await driver.Reference.ApproveSalesforceWithStoredEvidence(
            approval,
            cancellationToken);

        Assert.Equal(SalesforceMutationState.Completed, completed.State);
        Assert.Equal(commandId, completed.CommandId);
        Assert.Equal(AccountId, completed.AccountId);
        Assert.Equal(description, completed.Description);
        Assert.Equal(proposed.Fingerprint, completed.Fingerprint);
        Assert.True(test.Mcp().SessionCount >= 1);

        var again = await driver.Reference.ApproveSalesforceWithStoredEvidence(
            approval,
            cancellationToken);
        Assert.Equal(SalesforceMutationState.Completed, again.State);
    }

    [Fact(DisplayName =
        "ISalesforce.ApproveAccountDescription returns OutcomeUncertain when SOQL cannot prove the write")]
    public async Task ApproveReturnsOutcomeUncertainWhenSoqlCannotProveWrite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string description = "Approved description under uncertainty";
        var driver = test.Neuron<IIntegrationDriver>("sf-uncertain");
        var commandId = CommandId.New();
        var proposed = await driver.Reference.ProposeSalesforceAccountDescription(
            commandId,
            AccountId,
            description,
            cancellationToken);

        test.Mcp().Catalog(
            "salesforce",
            AdmittedMcpTools.SalesforceUpdateAccount(success: false),
            AdmittedMcpTools.SalesforceSoqlQuery(AccountId, "provider description does not match"));

        var approval = new SalesforceMutationApproval(
            Guid.NewGuid(),
            commandId,
            proposed.Fingerprint,
            SessionOf(test),
            test.Clock.UtcNow);
        var delivered = driver.Incoming.NextAsync<SalesforceMutationApproval>(cancellationToken);
        await test.Client.SendAsync(driver.Id, approval);
        Assert.Equal(approval, (await delivered).Synapse);

        var uncertain = await driver.Reference.ApproveSalesforceWithStoredEvidence(
            approval,
            cancellationToken);

        Assert.Equal(SalesforceMutationState.OutcomeUncertain, uncertain.State);
        Assert.Equal(commandId, uncertain.CommandId);
        Assert.Equal(AccountId, uncertain.AccountId);
        Assert.Equal(description, uncertain.Description);
        Assert.Equal(proposed.Fingerprint, uncertain.Fingerprint);
        Assert.True(test.Mcp().SessionCount >= 1);

        var again = await driver.Reference.ApproveSalesforceWithStoredEvidence(
            approval,
            cancellationToken);
        Assert.Equal(SalesforceMutationState.OutcomeUncertain, again.State);
        Assert.Equal(uncertain, again);
    }

    private static NeuronId SessionOf(TestBrain test)
        => new(ISessionNeuron.GrainTypeName, test.Client.Owner, "session");
}
