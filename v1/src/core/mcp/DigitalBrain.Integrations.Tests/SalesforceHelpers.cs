using DigitalBrain.Abstractions;
using DigitalBrain.Salesforce;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

internal static class SalesforceHelpers
{
    internal static Task<SalesforceResponse> ProposeAsync(
        TestBrain test,
        CommandId commandId,
        string accountId,
        string description,
        CancellationToken cancellationToken)
        => test.Client.Get<ISalesforce>(IntegrationsFixture.SalesforceServerKey)
            .SendAsync(
                new SalesforceRequest(
                    $"Propose Account Description for {accountId}",
                    commandId,
                    accountId,
                    description),
                cancellationToken);

    internal static Task<SalesforceResponse> ApproveAsync(
        TestBrain test,
        SalesforceMutationApproval approval,
        CancellationToken cancellationToken)
        => test.Client.Get<ISalesforce>(IntegrationsFixture.SalesforceServerKey)
            .SendAsync(new ApproveSalesforceMutation(approval), cancellationToken);

    internal static async Task DeliverApprovalAsync(
        TestBrain test,
        TestNeuron<IIntegrationDriver> driver,
        SalesforceMutationApproval approval,
        CancellationToken cancellationToken)
    {
        var delivered = driver.Incoming.NextAsync<SalesforceMutationApproval>(cancellationToken);
        await test.Client.SendAsync(driver.Id, approval, cancellationToken);
        Assert.Equal(approval, (await delivered).Synapse);
    }
}
