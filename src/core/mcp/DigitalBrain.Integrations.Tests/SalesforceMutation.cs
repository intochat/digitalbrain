using DigitalBrain.Abstractions;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Salesforce;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class SalesforceMutation(IntegrationsFixture fixture)
{
    private const string UnprovenProviderDescription = "provider description does not match";

    [Fact(DisplayName =
        "SalesforceRequest propose returns AwaitingApproval without opening MCP")]
    public async Task ProposeReturnsAwaitingApprovalWithoutMcp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var commandId = CommandId.New();
        var description = IntegrationsFixture.SampleEnrichmentDescription;

        var proposed = await ProposeAsync(test, commandId, description, cancellationToken);

        Assert.True(proposed.Succeeded);
        Assert.Equal(commandId, proposed.Mutation!.CommandId);
        Assert.Equal(IntegrationsFixture.SampleAccountId, proposed.Mutation.AccountId);
        Assert.Equal(description, proposed.Mutation.Description);
        Assert.False(string.IsNullOrWhiteSpace(proposed.Mutation.Fingerprint));
        Assert.Equal(SalesforceMutationState.AwaitingApproval, proposed.Mutation.State);
        Assert.Equal(0, test.Mcp().SessionCount);

        var again = await ProposeAsync(test, commandId, description, cancellationToken);
        Assert.Equal(proposed.Mutation, again.Mutation);
        Assert.Equal(0, test.Mcp().SessionCount);
    }

    [Fact(DisplayName =
        "SalesforceRequest propose rejects CommandId reuse with different content")]
    public async Task ProposeRejectsCommandIdReuseWithDifferentContent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var commandId = CommandId.New();
        var description = IntegrationsFixture.SampleEnrichmentDescription;
        var proposed = await ProposeAsync(test, commandId, description, cancellationToken);

        var failure = await ProposeAsync(test, commandId, description + "\n(amended)", cancellationToken);

        Assert.False(failure.Succeeded);
        Assert.Contains("fingerprint", failure.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, test.Mcp().SessionCount);
        Assert.Equal(proposed.Mutation, (await ProposeAsync(test, commandId, description, cancellationToken)).Mutation);
    }

    [Fact(DisplayName =
        "ApproveSalesforceMutation rejects fingerprint that does not match the stored proposal before MCP")]
    public async Task ApproveRejectsMismatchedFingerprintWithoutMcp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var commandId = CommandId.New();
        var description = IntegrationsFixture.SampleEnrichmentDescription;
        var proposed = await ProposeAsync(test, commandId, description, cancellationToken);

        var wrongFingerprint = IntegrationsFixture.Approval(test, commandId, proposed.Mutation!.Fingerprint + "-tampered");
        var failure = await SalesforceHelpers.ApproveAsync(test, wrongFingerprint, cancellationToken);

        Assert.False(failure.Succeeded);
        Assert.Contains("fingerprint", failure.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, test.Mcp().SessionCount);
        Assert.Equal(
            SalesforceMutationState.AwaitingApproval,
            (await ProposeAsync(test, commandId, description, cancellationToken)).Mutation!.State);
    }

    [Fact(DisplayName =
        "ApproveSalesforceMutation completes after admitted MCP update on the scripted edge")]
    public async Task ApproveCompletesThroughScriptedMcpEdge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var description = IntegrationsFixture.SampleEnrichmentDescription;
        var commandId = CommandId.New();
        var proposed = await ProposeAsync(test, commandId, description, cancellationToken);

        CatalogSalesforceWrite(test, description);
        var approval = IntegrationsFixture.Approval(test, commandId, proposed.Mutation!.Fingerprint);

        var completed = await SalesforceHelpers.ApproveAsync(test, approval, cancellationToken);

        Assert.True(completed.Succeeded);
        Assert.Equal(SalesforceMutationState.Completed, completed.Mutation!.State);
        Assert.Equal(commandId, completed.Mutation.CommandId);
        Assert.Equal(IntegrationsFixture.SampleAccountId, completed.Mutation.AccountId);
        Assert.Equal(description, completed.Mutation.Description);
        Assert.Equal(proposed.Mutation.Fingerprint, completed.Mutation.Fingerprint);
        Assert.True(test.Mcp().SessionCount >= 1);

        var again = await SalesforceHelpers.ApproveAsync(test, approval, cancellationToken);
        Assert.Equal(completed.Mutation, again.Mutation);
    }

    [Fact(DisplayName =
        "ApproveSalesforceMutation returns OutcomeUncertain when reconciliation cannot prove the write")]
    public async Task ApproveReturnsOutcomeUncertainWhenReconciliationCannotProveWrite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var description = IntegrationsFixture.SampleEnrichmentDescription;
        var commandId = CommandId.New();
        var proposed = await ProposeAsync(test, commandId, description, cancellationToken);

        test.Mcp().Catalog(
            IntegrationsFixture.SalesforceServerKey,
            AdmittedMcpTools.SalesforceUpdateAccount(success: false),
            AdmittedMcpTools.SalesforceSoqlQuery(IntegrationsFixture.SampleAccountId, UnprovenProviderDescription));

        var approval = IntegrationsFixture.Approval(test, commandId, proposed.Mutation!.Fingerprint);
        var uncertain = await SalesforceHelpers.ApproveAsync(test, approval, cancellationToken);

        Assert.True(uncertain.Succeeded);
        Assert.Equal(SalesforceMutationState.OutcomeUncertain, uncertain.Mutation!.State);
        Assert.Equal(commandId, uncertain.Mutation.CommandId);
        Assert.Equal(IntegrationsFixture.SampleAccountId, uncertain.Mutation.AccountId);
        Assert.Equal(description, uncertain.Mutation.Description);
        Assert.Equal(proposed.Mutation.Fingerprint, uncertain.Mutation.Fingerprint);
        Assert.True(test.Mcp().SessionCount >= 1);

        var again = await SalesforceHelpers.ApproveAsync(test, approval, cancellationToken);
        Assert.Equal(SalesforceMutationState.OutcomeUncertain, again.Mutation!.State);
        Assert.Equal(uncertain.Mutation, again.Mutation);
    }

    private static Task<SalesforceResponse> ProposeAsync(
        TestBrain test,
        CommandId commandId,
        string description,
        CancellationToken cancellationToken)
        => SalesforceHelpers.ProposeAsync(
            test,
            commandId,
            IntegrationsFixture.SampleAccountId,
            description,
            cancellationToken);

    private static void CatalogSalesforceWrite(TestBrain test, string description)
        => test.Mcp().Catalog(
            IntegrationsFixture.SalesforceServerKey,
            AdmittedMcpTools.SalesforceUpdateAccount(),
            AdmittedMcpTools.SalesforceSoqlQuery(IntegrationsFixture.SampleAccountId, description));
}
