using System.Text.Json;
using Brain.Contracts;
using DigitalBrain.Tests;
using Xunit;

namespace Brain.KernelTests;

public class GrantIssueTests(BrainClusterFixture<WorkspaceKindsConfigurator> fixture)
    : BrainTest<WorkspaceKindsConfigurator>(fixture)
{
    private static string GrantInput(string granteeKey, string contract) =>
        JsonSerializer.Serialize(new { granteeKey, contract });

    [Fact]
    public async Task Owner_grant_unlocks_a_foreign_caller_previously_denied()
    {
        var chat = Neuron("chat", Guid.NewGuid().ToString("N"));
        const string foreignCaller = "other-owner|actor/x|session/1";

        var before = await Assert.ThrowsAsync<BrainException>(() =>
            chat.InvokeAsync(new("chat.post.v1", """{"text":"blocked"}""", "cmd-x-1", foreignCaller)));
        Assert.Equal(BrainErrors.GrantMissing, before.Code);

        var grantReceipt = await chat.InvokeAsync(new("neuron.grant.v1",
            GrantInput(foreignCaller, "chat.post.v1"), "cmd-grant-1", OwnerSession));
        Assert.Equal("accepted", grantReceipt.Status);
        Assert.Contains("\"granted\":true", grantReceipt.OutputJson);

        var after = await chat.InvokeAsync(new("chat.post.v1", """{"text":"hello"}""", "cmd-x-2", foreignCaller));
        Assert.Equal(1, after.Revision);
    }

    [Fact]
    public async Task Behavior_space_caller_is_denied_grant_issuance_with_zero_state_change()
    {
        var chat = Neuron("chat", Guid.NewGuid().ToString("N"));
        const string behaviorCaller = "owner|behavior/abc123|behavior/abc123";
        const string granteeKey = "someone|actor/y|session/1";

        var denied = await Assert.ThrowsAsync<BrainException>(() =>
            chat.InvokeAsync(new("neuron.grant.v1",
                GrantInput(granteeKey, "chat.post.v1"), "cmd-behavior-grant", behaviorCaller)));
        Assert.Equal(BrainErrors.GrantDenied, denied.Code);

        var stillMissing = await Assert.ThrowsAsync<BrainException>(() =>
            chat.InvokeAsync(new("chat.post.v1", """{"text":"nope"}""", "cmd-check", granteeKey)));
        Assert.Equal(BrainErrors.GrantMissing, stillMissing.Code);
    }

    [Fact]
    public async Task Cross_owner_caller_is_denied_grant_issuance()
    {
        var chat = Neuron("chat", Guid.NewGuid().ToString("N"));
        const string crossOwnerCaller = "other-owner|actor/z|session/1";

        var denied = await Assert.ThrowsAsync<BrainException>(() =>
            chat.InvokeAsync(new("neuron.grant.v1",
                GrantInput("someone|actor/y|session/1", "chat.post.v1"), "cmd-cross-owner-grant", crossOwnerCaller)));
        Assert.Equal(BrainErrors.GrantDenied, denied.Code);
    }

    [Fact]
    public async Task Granting_the_same_grantee_and_contract_twice_creates_only_one_synapse()
    {
        var chat = Neuron("chat", Guid.NewGuid().ToString("N"));
        const string granteeKey = "someone|actor/y|session/1";
        var input = GrantInput(granteeKey, "chat.post.v1");

        await chat.InvokeAsync(new("neuron.grant.v1", input, "cmd-grant-a", OwnerSession));
        await chat.InvokeAsync(new("neuron.grant.v1", input, "cmd-grant-b", OwnerSession));

        var revokeReceipt = await chat.InvokeAsync(new("neuron.revoke.v1", input, "cmd-revoke", OwnerSession));
        Assert.Contains("\"revoked\":1", revokeReceipt.OutputJson);
    }

    [Fact]
    public async Task Revoke_removes_access_previously_granted()
    {
        var chat = Neuron("chat", Guid.NewGuid().ToString("N"));
        const string granteeKey = "someone|actor/y|session/1";
        var input = GrantInput(granteeKey, "chat.post.v1");

        await chat.InvokeAsync(new("neuron.grant.v1", input, "cmd-grant", OwnerSession));
        var granted = await chat.InvokeAsync(new("chat.post.v1", """{"text":"hi"}""", "cmd-post-1", granteeKey));
        Assert.Equal(1, granted.Revision);

        await chat.InvokeAsync(new("neuron.revoke.v1", input, "cmd-revoke", OwnerSession));

        var revoked = await Assert.ThrowsAsync<BrainException>(() =>
            chat.InvokeAsync(new("chat.post.v1", """{"text":"blocked"}""", "cmd-post-2", granteeKey)));
        Assert.Equal(BrainErrors.GrantMissing, revoked.Code);
    }

    [Fact]
    public async Task Behavior_space_caller_with_capital_prefix_is_denied_grant_issuance()
    {
        var chat = Neuron("chat", Guid.NewGuid().ToString("N"));
        const string behaviorCallerCapital = "owner|Behavior/abc123|Behavior/abc123";
        const string granteeKey = "someone|actor/y|session/1";

        var denied = await Assert.ThrowsAsync<BrainException>(() =>
            chat.InvokeAsync(new("neuron.grant.v1",
                GrantInput(granteeKey, "chat.post.v1"), "cmd-behavior-grant-capital", behaviorCallerCapital)));
        Assert.Equal(BrainErrors.GrantDenied, denied.Code);

        var stillMissing = await Assert.ThrowsAsync<BrainException>(() =>
            chat.InvokeAsync(new("chat.post.v1", """{"text":"nope"}""", "cmd-check", granteeKey)));
        Assert.Equal(BrainErrors.GrantMissing, stillMissing.Code);
    }

    [Fact]
    public async Task Grant_with_empty_granteeKey_throws_input_invalid()
    {
        var chat = Neuron("chat", Guid.NewGuid().ToString("N"));
        var inputWithEmpty = JsonSerializer.Serialize(new { granteeKey = "", contract = "chat.post.v1" });

        var invalid = await Assert.ThrowsAsync<BrainException>(() =>
            chat.InvokeAsync(new("neuron.grant.v1", inputWithEmpty, "cmd-empty-grantee", OwnerSession)));
        Assert.Equal("input.invalid", invalid.Code);
    }

    [Fact]
    public async Task Grant_on_unregistered_kind_succeeds_with_granted_true()
    {
        var unregistered = Neuron("unknown-kind", Guid.NewGuid().ToString("N"));
        const string granteeKey = "someone|actor/y|session/1";
        var input = GrantInput(granteeKey, "some.contract.v1");

        var grantReceipt = await unregistered.InvokeAsync(new("neuron.grant.v1", input, "cmd-grant-unregistered", OwnerSession));
        Assert.Equal("accepted", grantReceipt.Status);
        Assert.Contains("\"granted\":true", grantReceipt.OutputJson);
    }
}
