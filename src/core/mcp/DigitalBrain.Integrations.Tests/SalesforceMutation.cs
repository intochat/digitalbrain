using DigitalBrain.Abstractions;
using DigitalBrain.Salesforce;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class SalesforceMutation(IntegrationsFixture fixture)
{
    private const string Driver = "sf-mutation";
    private const string UnprovenProviderDescription = "provider description does not match";

    [Fact(DisplayName =
        "ISalesforce.ProposeAccountDescription returns AwaitingApproval without opening MCP")]
    public async Task ProposeReturnsAwaitingApprovalWithoutMcp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var driver = test.Neuron<IIntegrationDriver>(Driver);
        var commandId = CommandId.New();
        var description = IntegrationsFixture.SampleEnrichmentDescription;

        var proposed = await ProposeAsync(driver, commandId, description, cancellationToken);

        Assert.Equal(commandId, proposed.CommandId);
        Assert.Equal(IntegrationsFixture.SampleAccountId, proposed.AccountId);
        Assert.Equal(description, proposed.Description);
        Assert.False(string.IsNullOrWhiteSpace(proposed.Fingerprint));
        Assert.Equal(SalesforceMutationState.AwaitingApproval, proposed.State);
        Assert.Equal(0, test.Mcp().SessionCount);

        var again = await ProposeAsync(driver, commandId, description, cancellationToken);
        Assert.Equal(proposed, again);
        Assert.Equal(0, test.Mcp().SessionCount);
    }

    [Fact(DisplayName =
        "ISalesforce.ProposeAccountDescription rejects CommandId reuse with different content")]
    public async Task ProposeRejectsCommandIdReuseWithDifferentContent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var driver = test.Neuron<IIntegrationDriver>(Driver);
        var commandId = CommandId.New();
        var description = IntegrationsFixture.SampleEnrichmentDescription;
        var proposed = await ProposeAsync(driver, commandId, description, cancellationToken);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProposeAsync(driver, commandId, description + "\n(amended)", cancellationToken));

        Assert.Contains("fingerprint", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, test.Mcp().SessionCount);
        Assert.Equal(proposed, await ProposeAsync(driver, commandId, description, cancellationToken));
    }

    [Fact(DisplayName =
        "ISalesforce.ApproveAccountDescription rejects mismatched human approval evidence before MCP")]
    public async Task ApproveRejectsMismatchedEvidenceWithoutMcp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var driver = test.Neuron<IIntegrationDriver>(Driver);
        var commandId = CommandId.New();
        var description = IntegrationsFixture.SampleEnrichmentDescription;
        var proposed = await ProposeAsync(driver, commandId, description, cancellationToken);

        var recorded = IntegrationsFixture.Approval(test, commandId, proposed.Fingerprint);
        await DeliverApprovalAsync(test, driver, recorded, cancellationToken);

        var mismatched = recorded with { ApprovalId = Guid.NewGuid() };
        await Assert.ThrowsAsync<NeuronAuthorizationException>(() =>
            driver.Reference.ApproveSalesforceWithMismatchedEvidence(mismatched, recorded, cancellationToken));

        Assert.Equal(0, test.Mcp().SessionCount);
        Assert.Equal(
            SalesforceMutationState.AwaitingApproval,
            (await ProposeAsync(driver, commandId, description, cancellationToken)).State);
    }

    [Fact(DisplayName =
        "ISalesforce.ApproveAccountDescription rejects fingerprint that does not match the stored proposal before MCP")]
    public async Task ApproveRejectsMismatchedFingerprintWithoutMcp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var driver = test.Neuron<IIntegrationDriver>(Driver);
        var commandId = CommandId.New();
        var description = IntegrationsFixture.SampleEnrichmentDescription;
        var proposed = await ProposeAsync(driver, commandId, description, cancellationToken);

        var wrongFingerprint = IntegrationsFixture.Approval(test, commandId, proposed.Fingerprint + "-tampered");
        await DeliverApprovalAsync(test, driver, wrongFingerprint, cancellationToken);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.Reference.ApproveSalesforceWithStoredEvidence(wrongFingerprint, cancellationToken));

        Assert.Contains("fingerprint", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, test.Mcp().SessionCount);
        Assert.Equal(
            SalesforceMutationState.AwaitingApproval,
            (await ProposeAsync(driver, commandId, description, cancellationToken)).State);
    }

    [Fact(DisplayName =
        "ISalesforce.ApproveAccountDescription completes after admitted MCP update on the scripted edge")]
    public async Task ApproveCompletesThroughScriptedMcpEdge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var description = IntegrationsFixture.SampleEnrichmentDescription;
        var driver = test.Neuron<IIntegrationDriver>(Driver);
        var commandId = CommandId.New();
        var proposed = await ProposeAsync(driver, commandId, description, cancellationToken);

        CatalogSalesforceWrite(test, description);
        var approval = IntegrationsFixture.Approval(test, commandId, proposed.Fingerprint);
        await DeliverApprovalAsync(test, driver, approval, cancellationToken);

        var completed = await driver.Reference.ApproveSalesforceWithStoredEvidence(approval, cancellationToken);

        Assert.Equal(SalesforceMutationState.Completed, completed.State);
        Assert.Equal(commandId, completed.CommandId);
        Assert.Equal(IntegrationsFixture.SampleAccountId, completed.AccountId);
        Assert.Equal(description, completed.Description);
        Assert.Equal(proposed.Fingerprint, completed.Fingerprint);
        Assert.True(test.Mcp().SessionCount >= 1);

        var again = await driver.Reference.ApproveSalesforceWithStoredEvidence(approval, cancellationToken);
        Assert.Equal(completed, again);
    }

    [Fact(DisplayName =
        "ISalesforce.ApproveAccountDescription returns OutcomeUncertain when reconciliation cannot prove the write")]
    public async Task ApproveReturnsOutcomeUncertainWhenReconciliationCannotProveWrite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var description = IntegrationsFixture.SampleEnrichmentDescription;
        var driver = test.Neuron<IIntegrationDriver>(Driver);
        var commandId = CommandId.New();
        var proposed = await ProposeAsync(driver, commandId, description, cancellationToken);

        test.Mcp().Catalog(
            IntegrationsFixture.SalesforceServerKey,
            AdmittedMcpTools.SalesforceUpdateAccount(success: false),
            AdmittedMcpTools.SalesforceSoqlQuery(IntegrationsFixture.SampleAccountId, UnprovenProviderDescription));

        var approval = IntegrationsFixture.Approval(test, commandId, proposed.Fingerprint);
        await DeliverApprovalAsync(test, driver, approval, cancellationToken);

        var uncertain = await driver.Reference.ApproveSalesforceWithStoredEvidence(approval, cancellationToken);

        Assert.Equal(SalesforceMutationState.OutcomeUncertain, uncertain.State);
        Assert.Equal(commandId, uncertain.CommandId);
        Assert.Equal(IntegrationsFixture.SampleAccountId, uncertain.AccountId);
        Assert.Equal(description, uncertain.Description);
        Assert.Equal(proposed.Fingerprint, uncertain.Fingerprint);
        Assert.True(test.Mcp().SessionCount >= 1);

        var again = await driver.Reference.ApproveSalesforceWithStoredEvidence(approval, cancellationToken);
        Assert.Equal(SalesforceMutationState.OutcomeUncertain, again.State);
        Assert.Equal(uncertain, again);
    }

    private static Task<SalesforceAccountDescriptionMutation> ProposeAsync(
        TestNeuron<IIntegrationDriver> driver,
        CommandId commandId,
        string description,
        CancellationToken cancellationToken)
        => driver.Reference.ProposeSalesforceAccountDescription(
            commandId,
            IntegrationsFixture.SampleAccountId,
            description,
            cancellationToken);

    private static async Task DeliverApprovalAsync(
        TestBrain test,
        TestNeuron<IIntegrationDriver> driver,
        SalesforceMutationApproval approval,
        CancellationToken cancellationToken)
    {
        var delivered = driver.Incoming.NextAsync<SalesforceMutationApproval>(cancellationToken);
        await test.Client.SendAsync(driver.Id, approval, cancellationToken);
        Assert.Equal(approval, (await delivered).Synapse);
    }

    private static void CatalogSalesforceWrite(TestBrain test, string description)
        => test.Mcp().Catalog(
            IntegrationsFixture.SalesforceServerKey,
            AdmittedMcpTools.SalesforceUpdateAccount(),
            AdmittedMcpTools.SalesforceSoqlQuery(IntegrationsFixture.SampleAccountId, description));
}
