using DigitalBrain.Abstractions;
using DigitalBrain.Salesforce;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class SalesforceContracts(ModuleFixture fixture)
{
    [Fact]
    public async Task ProposalIsDurableAndAwaitsExactApproval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var driver = test.Neuron<IModuleDriver>("salesforce-driver");
        var proposed = driver.Outgoing.NextAsync<SalesforceProposed>(
            cancellationToken);
        var command = CommandId.New();

        await test.Client.SendAsync<IModuleDriver>(
            "salesforce-driver",
            new ProposeSalesforce(
                command,
                "001000000000042AAA",
                "Approved description"));
        var mutation = (await proposed).Synapse.Mutation;

        Assert.Equal(command, mutation.CommandId);
        Assert.Equal(SalesforceMutationState.AwaitingApproval, mutation.State);
        Assert.False(string.IsNullOrWhiteSpace(mutation.Fingerprint));
        Assert.Empty(test.Mcp().Calls);
    }

    [Fact]
    public async Task ExactCommittedApprovalAppliesTheProposalOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var driver = test.Neuron<IModuleDriver>("salesforce-driver");
        var proposed = await Propose(test, driver, cancellationToken);
        var mutation = proposed.Mutation;
        var approval = new SalesforceMutationApproval(
            Guid.NewGuid(),
            mutation.CommandId,
            mutation.Fingerprint,
            proposed.Approver,
            test.Clock.UtcNow);
        var approved = driver.Outgoing.NextAsync<SalesforceApproved>(
            cancellationToken);

        await test.Client.SendAsync<IModuleDriver>(
            "salesforce-driver",
            approval);
        var result = await approved;

        Assert.Null(result.Synapse.Failure);
        Assert.Equal(SalesforceMutationState.Completed, result.Synapse.State);
        var update = Assert.Single(
            test.Mcp().Calls,
            call => call.Tool == "updateSobjectRecord");
        Assert.Equal(
            "Approved description",
            update.Arguments
                .GetProperty("body")
                .GetProperty("Description")
                .GetString());
        Assert.Contains(
            await driver.Incoming.ReadAsync<SalesforceMutationApproval>(
                cancellationToken: cancellationToken),
            entry => entry.Synapse == approval);
    }

    [Fact]
    public async Task AmbiguousMutationReconcilesToOutcomeUncertainWithoutRepeating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        test.Mcp().FailUpdate = true;
        var driver = test.Neuron<IModuleDriver>("salesforce-driver");
        var proposed = await Propose(test, driver, cancellationToken);
        var mutation = proposed.Mutation;
        var approval = new SalesforceMutationApproval(
            Guid.NewGuid(),
            mutation.CommandId,
            mutation.Fingerprint,
            proposed.Approver,
            test.Clock.UtcNow);
        var approved = driver.Outgoing.NextAsync<SalesforceApproved>(
            cancellationToken);

        await test.Client.SendAsync<IModuleDriver>(
            "salesforce-driver",
            approval);
        var result = await approved;

        Assert.Null(result.Synapse.Failure);
        Assert.Equal(
            SalesforceMutationState.OutcomeUncertain,
            result.Synapse.State);
        Assert.Single(
            test.Mcp().Calls,
            call => call.Tool == "updateSobjectRecord");
        Assert.Single(
            test.Mcp().Calls,
            call => call.Tool == "soqlQuery");
    }

    private static async Task<(
        SalesforceAccountDescriptionMutation Mutation,
        NeuronId Approver)> Propose(
        TestBrain test,
        TestNeuron<IModuleDriver> driver,
        CancellationToken cancellationToken)
    {
        var proposed = driver.Outgoing.NextAsync<SalesforceProposed>(
            cancellationToken);
        await test.Client.SendAsync<IModuleDriver>(
            "salesforce-driver",
            new ProposeSalesforce(
                CommandId.New(),
                "001000000000042AAA",
                "Approved description"));
        var mutation = (await proposed).Synapse.Mutation;
        var request = Assert.Single(
            await driver.Incoming.ReadAsync<ProposeSalesforce>(
                cancellationToken: cancellationToken));
        return (mutation, request.Caller);
    }
}
