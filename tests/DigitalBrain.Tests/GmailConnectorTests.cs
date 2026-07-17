using Brain.Contracts;
using DigitalBrain.Tests;
using Xunit;

namespace Brain.KernelTests;

public class GmailConnectorTests(BrainClusterFixture<ConnectorsKindsConfigurator> fixture)
    : BrainTest<ConnectorsKindsConfigurator>(fixture)
{
    private async Task EnsureConnectedAsync()
    {
        var connection = Neuron("connection", "google-primary");
        await connection.InvokeAsync(new("connection.start-auth.v1", "{}", $"cmd-start-{Guid.NewGuid():N}", OwnerSession));
        await connection.InvokeAsync(new("connection.complete-auth.v1", """{"code":"auth-code"}""", $"cmd-complete-{Guid.NewGuid():N}", OwnerSession));
    }

    [Fact]
    public async Task Propose_approve_execute_sends_exactly_once()
    {
        ConnectorsKindsConfigurator.GmailProvider.Reset();
        await EnsureConnectedAsync();

        var uid = Guid.NewGuid().ToString("N");
        var gmail = Neuron("gmail", $"outbox-{uid}");
        var propose = await gmail.InvokeAsync(new(
            "gmail.propose-send.v1", """{"to":"a@example.com","subject":"hi","body":"hello"}""", $"cmd-propose-{uid}", OwnerSession));
        Assert.NotNull(propose.EffectKey);

        var effect = Cluster.GrainFactory.GetGrain<INeuron>(propose.EffectKey!);
        await effect.InvokeAsync(new("effect.approve.v1", "{}", $"cmd-approve-{uid}", OwnerSession));

        var execute = await gmail.InvokeAsync(new(
            "gmail.execute-send.v1", $$"""{"effectKey":"{{propose.EffectKey}}"}""", $"cmd-execute-{uid}", OwnerSession));
        Assert.Contains("fake-message-id", execute.OutputJson);
        Assert.Equal(1, ConnectorsKindsConfigurator.GmailProvider.SendCalls);
    }

    [Fact]
    public async Task Execute_before_approval_fails_closed_and_provider_uncalled()
    {
        ConnectorsKindsConfigurator.GmailProvider.Reset();
        await EnsureConnectedAsync();

        var uid = Guid.NewGuid().ToString("N");
        var gmail = Neuron("gmail", $"outbox-{uid}");
        var propose = await gmail.InvokeAsync(new(
            "gmail.propose-send.v1", """{"to":"a@example.com","subject":"hi","body":"hello"}""", $"cmd-propose-{uid}", OwnerSession));

        var exception = await Assert.ThrowsAsync<BrainException>(() => gmail.InvokeAsync(new(
            "gmail.execute-send.v1", $$"""{"effectKey":"{{propose.EffectKey}}"}""", $"cmd-execute-{uid}", OwnerSession)));
        Assert.Equal(BrainErrors.EffectNotApproved, exception.Code);
        Assert.Equal(0, ConnectorsKindsConfigurator.GmailProvider.SendCalls);
    }

    [Fact]
    public async Task Execute_twice_fails_closed_on_second_attempt_provider_called_once()
    {
        ConnectorsKindsConfigurator.GmailProvider.Reset();
        await EnsureConnectedAsync();

        var uid = Guid.NewGuid().ToString("N");
        var gmail = Neuron("gmail", $"outbox-{uid}");
        var propose = await gmail.InvokeAsync(new(
            "gmail.propose-send.v1", """{"to":"a@example.com","subject":"hi","body":"hello"}""", $"cmd-propose-{uid}", OwnerSession));
        var effect = Cluster.GrainFactory.GetGrain<INeuron>(propose.EffectKey!);
        await effect.InvokeAsync(new("effect.approve.v1", "{}", $"cmd-approve-{uid}", OwnerSession));

        await gmail.InvokeAsync(new(
            "gmail.execute-send.v1", $$"""{"effectKey":"{{propose.EffectKey}}"}""", $"cmd-execute-1-{uid}", OwnerSession));

        var exception = await Assert.ThrowsAsync<BrainException>(() => gmail.InvokeAsync(new(
            "gmail.execute-send.v1", $$"""{"effectKey":"{{propose.EffectKey}}"}""", $"cmd-execute-2-{uid}", OwnerSession)));
        Assert.Equal(BrainErrors.EffectNotApproved, exception.Code);
        Assert.Equal(1, ConnectorsKindsConfigurator.GmailProvider.SendCalls);
    }

    [Fact]
    public async Task Execute_with_foreign_effect_key_fails_closed_and_owner_can_still_claim()
    {
        ConnectorsKindsConfigurator.GmailProvider.Reset();
        await EnsureConnectedAsync();

        var uid = Guid.NewGuid().ToString("N");
        var gmailA = Neuron("gmail", $"outbox-a-{uid}");
        var gmailB = Neuron("gmail", $"outbox-b-{uid}");
        var proposeA = await gmailA.InvokeAsync(new(
            "gmail.propose-send.v1", """{"to":"a@example.com","subject":"hi","body":"hello"}""", $"cmd-propose-{uid}", OwnerSession));
        var effectA = Cluster.GrainFactory.GetGrain<INeuron>(proposeA.EffectKey!);
        await effectA.InvokeAsync(new("effect.approve.v1", "{}", $"cmd-approve-{uid}", OwnerSession));

        var exception = await Assert.ThrowsAsync<BrainException>(() => gmailB.InvokeAsync(new(
            "gmail.execute-send.v1", $$"""{"effectKey":"{{proposeA.EffectKey}}"}""", $"cmd-execute-b-{uid}", OwnerSession)));
        Assert.Equal(BrainErrors.EffectNotApproved, exception.Code);
        Assert.Equal(0, ConnectorsKindsConfigurator.GmailProvider.SendCalls);

        var execute = await gmailA.InvokeAsync(new(
            "gmail.execute-send.v1", $$"""{"effectKey":"{{proposeA.EffectKey}}"}""", $"cmd-execute-a-{uid}", OwnerSession));
        Assert.Contains("fake-message-id", execute.OutputJson);
        Assert.Equal(1, ConnectorsKindsConfigurator.GmailProvider.SendCalls);
    }

    [Fact]
    public async Task Read_with_connection_returns_messages_and_journals()
    {
        ConnectorsKindsConfigurator.GmailProvider.Reset();
        ConnectorsKindsConfigurator.GmailProvider.ListResult = """{"messages":[{"id":"m1"}]}""";
        await EnsureConnectedAsync();

        var gmail = Neuron("gmail", $"reader-{Guid.NewGuid():N}");
        var read = await gmail.InvokeAsync(new("gmail.read.v1", """{"max":5}""", $"cmd-read-{Guid.NewGuid():N}", OwnerSession));
        Assert.Contains("m1", read.OutputJson);
        Assert.Equal(1, ConnectorsKindsConfigurator.GmailProvider.ListCalls);
    }
}
