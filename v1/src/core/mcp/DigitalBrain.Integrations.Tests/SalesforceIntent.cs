using DigitalBrain.Abstractions;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Salesforce;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class SalesforceIntent(IntegrationsFixture fixture)
{
    [Fact(DisplayName = "ISalesforce is a marker INeuron with no declared operation members")]
    public void MarkerIsInNeuronWithNoDeclaredMembers()
    {
        Assert.True(typeof(INeuron).IsAssignableFrom(typeof(ISalesforce)));
        Assert.DoesNotContain(
            typeof(ISalesforce).GetMethods(),
            static method => method.DeclaringType == typeof(ISalesforce));
        Assert.DoesNotContain(
            typeof(ISalesforce).GetProperties(),
            static property => property.DeclaringType == typeof(ISalesforce));
    }

    [Fact(DisplayName = "ApproveSalesforceMutation completes after admitted MCP update")]
    public async Task ApproveCompletesThroughScriptedMcp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var commandId = CommandId.New();
        var description = IntegrationsFixture.SampleEnrichmentDescription;
        var proposed = await SalesforceHelpers.ProposeAsync(
            test, commandId, IntegrationsFixture.SampleAccountId, description, cancellationToken);
        Assert.True(proposed.Succeeded);

        CatalogWrite(test, description);
        var approval = IntegrationsFixture.Approval(test, commandId, proposed.Mutation!.Fingerprint);

        var approved = await SalesforceHelpers.ApproveAsync(test, approval, cancellationToken);

        Assert.True(approved.Succeeded);
        Assert.Equal(SalesforceMutationState.Completed, approved.Mutation!.State);
        Assert.True(test.Mcp().SessionCount >= 1);

        var again = await SalesforceHelpers.ApproveAsync(test, approval, cancellationToken);
        Assert.Equal(approved.Mutation, again.Mutation);
    }

    [Fact(DisplayName = "ApproveSalesforceMutation rejects fingerprint that does not match the proposal")]
    public async Task ApproveRejectsMismatchedFingerprint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var commandId = CommandId.New();
        var description = IntegrationsFixture.SampleEnrichmentDescription;
        var proposed = await SalesforceHelpers.ProposeAsync(
            test, commandId, IntegrationsFixture.SampleAccountId, description, cancellationToken);

        var approval = IntegrationsFixture.Approval(test, commandId, proposed.Mutation!.Fingerprint + "-tampered");

        var response = await SalesforceHelpers.ApproveAsync(test, approval, cancellationToken);

        Assert.False(response.Succeeded);
        Assert.Contains("fingerprint", response.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, test.Mcp().SessionCount);
    }

    [Fact(DisplayName = "Unapproved mutation cannot complete without ApproveSalesforceMutation")]
    public async Task UnapprovedMutationStaysAwaitingApproval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var commandId = CommandId.New();
        var description = IntegrationsFixture.SampleEnrichmentDescription;
        CatalogWrite(test, description);

        var proposed = await SalesforceHelpers.ProposeAsync(
            test, commandId, IntegrationsFixture.SampleAccountId, description, cancellationToken);
        Assert.Equal(SalesforceMutationState.AwaitingApproval, proposed.Mutation!.State);
        Assert.Equal(0, test.Mcp().SessionCount);
    }

    private static void CatalogWrite(TestBrain test, string description)
        => test.Mcp().Catalog(
            IntegrationsFixture.SalesforceServerKey,
            AdmittedMcpTools.SalesforceUpdateAccount(),
            AdmittedMcpTools.SalesforceSoqlQuery(IntegrationsFixture.SampleAccountId, description));
}
