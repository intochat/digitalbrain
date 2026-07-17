using Brain.Contracts;
using Brain.Modules.Sdk;
using Xunit;

namespace Brain.KernelTests;

public class SalesforceConnectorTests(BrainClusterFixture<ConnectorsKindsConfigurator> fixture)
    : BrainTest<ConnectorsKindsConfigurator>(fixture)
{
    private async Task EnsureConnectedAsync()
    {
        var connection = Neuron("connection", "salesforce-primary");
        await connection.InvokeAsync(new("connection.start-auth.v1", "{}", $"cmd-start-{Guid.NewGuid():N}", OwnerSession));
        await connection.InvokeAsync(new("connection.complete-auth.v1", """{"code":"auth-code"}""", $"cmd-complete-{Guid.NewGuid():N}", OwnerSession));
    }

    [Fact]
    public async Task Propose_approve_execute_updates_exactly_once()
    {
        ConnectorsKindsConfigurator.SalesforceProvider.Reset();
        await EnsureConnectedAsync();

        var uid = Guid.NewGuid().ToString("N");
        var salesforce = Neuron("salesforce", $"updater-{uid}");
        var propose = await salesforce.InvokeAsync(new(
            "salesforce.propose-update.v1", """{"objectId":"acc-1","fields":{"Name":"Acme"}}""", $"cmd-propose-{uid}", OwnerSession));
        Assert.NotNull(propose.EffectKey);

        var effect = Cluster.GrainFactory.GetGrain<INeuron>(propose.EffectKey!);
        await effect.InvokeAsync(new("effect.approve.v1", "{}", $"cmd-approve-{uid}", OwnerSession));

        var execute = await salesforce.InvokeAsync(new(
            "salesforce.execute-update.v1", $$"""{"effectKey":"{{propose.EffectKey}}"}""", $"cmd-execute-{uid}", OwnerSession));
        Assert.Contains("fake-record-id", execute.OutputJson);
        Assert.Equal(1, ConnectorsKindsConfigurator.SalesforceProvider.UpdateCalls);
    }

    [Fact]
    public async Task Execute_before_approval_fails_closed_and_provider_uncalled()
    {
        ConnectorsKindsConfigurator.SalesforceProvider.Reset();
        await EnsureConnectedAsync();

        var uid = Guid.NewGuid().ToString("N");
        var salesforce = Neuron("salesforce", $"updater-{uid}");
        var propose = await salesforce.InvokeAsync(new(
            "salesforce.propose-update.v1", """{"objectId":"acc-1","fields":{"Name":"Acme"}}""", $"cmd-propose-{uid}", OwnerSession));

        var exception = await Assert.ThrowsAsync<BrainException>(() => salesforce.InvokeAsync(new(
            "salesforce.execute-update.v1", $$"""{"effectKey":"{{propose.EffectKey}}"}""", $"cmd-execute-{uid}", OwnerSession)));
        Assert.Equal(BrainErrors.EffectNotApproved, exception.Code);
        Assert.Equal(0, ConnectorsKindsConfigurator.SalesforceProvider.UpdateCalls);
    }

    [Fact]
    public async Task Execute_twice_fails_closed_on_second_attempt_provider_called_once()
    {
        ConnectorsKindsConfigurator.SalesforceProvider.Reset();
        await EnsureConnectedAsync();

        var uid = Guid.NewGuid().ToString("N");
        var salesforce = Neuron("salesforce", $"updater-{uid}");
        var propose = await salesforce.InvokeAsync(new(
            "salesforce.propose-update.v1", """{"objectId":"acc-1","fields":{"Name":"Acme"}}""", $"cmd-propose-{uid}", OwnerSession));
        var effect = Cluster.GrainFactory.GetGrain<INeuron>(propose.EffectKey!);
        await effect.InvokeAsync(new("effect.approve.v1", "{}", $"cmd-approve-{uid}", OwnerSession));

        await salesforce.InvokeAsync(new(
            "salesforce.execute-update.v1", $$"""{"effectKey":"{{propose.EffectKey}}"}""", $"cmd-execute-1-{uid}", OwnerSession));

        var exception = await Assert.ThrowsAsync<BrainException>(() => salesforce.InvokeAsync(new(
            "salesforce.execute-update.v1", $$"""{"effectKey":"{{propose.EffectKey}}"}""", $"cmd-execute-2-{uid}", OwnerSession)));
        Assert.Equal(BrainErrors.EffectNotApproved, exception.Code);
        Assert.Equal(1, ConnectorsKindsConfigurator.SalesforceProvider.UpdateCalls);
    }

    [Fact]
    public async Task Execute_with_foreign_effect_key_fails_closed_and_owner_can_still_claim()
    {
        ConnectorsKindsConfigurator.SalesforceProvider.Reset();
        await EnsureConnectedAsync();

        var uid = Guid.NewGuid().ToString("N");
        var salesforceA = Neuron("salesforce", $"updater-a-{uid}");
        var salesforceB = Neuron("salesforce", $"updater-b-{uid}");
        var proposeA = await salesforceA.InvokeAsync(new(
            "salesforce.propose-update.v1", """{"objectId":"acc-1","fields":{"Name":"Acme"}}""", $"cmd-propose-{uid}", OwnerSession));
        var effectA = Cluster.GrainFactory.GetGrain<INeuron>(proposeA.EffectKey!);
        await effectA.InvokeAsync(new("effect.approve.v1", "{}", $"cmd-approve-{uid}", OwnerSession));

        var exception = await Assert.ThrowsAsync<BrainException>(() => salesforceB.InvokeAsync(new(
            "salesforce.execute-update.v1", $$"""{"effectKey":"{{proposeA.EffectKey}}"}""", $"cmd-execute-b-{uid}", OwnerSession)));
        Assert.Equal(BrainErrors.EffectNotApproved, exception.Code);
        Assert.Equal(0, ConnectorsKindsConfigurator.SalesforceProvider.UpdateCalls);

        var execute = await salesforceA.InvokeAsync(new(
            "salesforce.execute-update.v1", $$"""{"effectKey":"{{proposeA.EffectKey}}"}""", $"cmd-execute-a-{uid}", OwnerSession));
        Assert.Contains("fake-record-id", execute.OutputJson);
        Assert.Equal(1, ConnectorsKindsConfigurator.SalesforceProvider.UpdateCalls);
    }

    [Fact]
    public async Task Read_with_connection_returns_records_and_journals()
    {
        ConnectorsKindsConfigurator.SalesforceProvider.Reset();
        ConnectorsKindsConfigurator.SalesforceProvider.QueryResult = """{"records":[{"Id":"acc-1"}]}""";
        await EnsureConnectedAsync();

        var salesforce = Neuron("salesforce", $"reader-{Guid.NewGuid():N}");
        var read = await salesforce.InvokeAsync(new(
            "salesforce.read.v1", """{"query":"SELECT Id FROM Account"}""", $"cmd-read-{Guid.NewGuid():N}", OwnerSession));
        Assert.Contains("acc-1", read.OutputJson);
        Assert.Equal(1, ConnectorsKindsConfigurator.SalesforceProvider.QueryCalls);
    }
}
