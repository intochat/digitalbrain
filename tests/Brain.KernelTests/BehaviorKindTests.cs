using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Brain.Contracts;
using Brain.Modules.Sdk;
using Xunit;

namespace Brain.KernelTests;

public class BehaviorKindTests(BrainClusterFixture<BehaviorsKindsConfigurator> fixture)
    : BrainTest<BehaviorsKindsConfigurator>(fixture)
{
    private static string Sha256Hex(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static string ProposeInput(string source, string sourceHash, bool bddPassed, object grants) =>
        JsonSerializer.Serialize(new { source, sourceHash, bddPassed, grants });

    [Fact]
    public async Task Propose_with_correct_hash_and_grants_succeeds()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        const string source = "public sealed class InboxBrief { }";
        var sourceHash = Sha256Hex(source);
        var grants = new[] { new { address = AddressKey("chat", "main"), contract = "chat.post.v1" } };

        var receipt = await behavior.InvokeAsync(new(
            "behavior.propose.v1", ProposeInput(source, sourceHash, true, grants), "cmd-propose-ok", OwnerSession));

        Assert.Contains("\"status\":\"proposed\"", receipt.OutputJson);
        Assert.Contains($"\"sourceHash\":\"{sourceHash}\"", receipt.OutputJson);
    }

    [Fact]
    public async Task Propose_with_wrong_source_hash_fails_closed()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        var input = ProposeInput("some source", "not-the-real-hash", true, Array.Empty<object>());

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            behavior.InvokeAsync(new("behavior.propose.v1", input, "cmd-propose-wrong-hash", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
        Assert.Empty((await behavior.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Propose_with_bdd_not_passed_fails_closed()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        const string source = "source pending bdd";
        var input = ProposeInput(source, Sha256Hex(source), false, Array.Empty<object>());

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            behavior.InvokeAsync(new("behavior.propose.v1", input, "cmd-propose-no-bdd", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
        Assert.Empty((await behavior.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Approve_binds_grant_to_behavior_identity_scoped_to_the_granted_target()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        var chatMain = Neuron("chat", "main");
        var chatOther = Neuron("chat", "other");
        const string source = "approve flow source";
        var sourceHash = Sha256Hex(source);
        var grants = new[] { new { address = AddressKey("chat", "main"), contract = "chat.post.v1" } };

        await behavior.InvokeAsync(new(
            "behavior.propose.v1", ProposeInput(source, sourceHash, true, grants), "cmd-approve-propose", OwnerSession));

        var approveReceipt = await behavior.InvokeAsync(new(
            "behavior.approve.v1", JsonSerializer.Serialize(new { sourceHash }), "cmd-approve", OwnerSession));

        var identity = $"owner|behavior/{sourceHash}|behavior/{sourceHash}";
        Assert.Contains("\"status\":\"enabled\"", approveReceipt.OutputJson);
        Assert.Contains(identity, approveReceipt.OutputJson);

        var granted = await chatMain.InvokeAsync(new(
            "chat.post.v1", """{"text":"posted by behavior identity"}""", "cmd-behavior-post-granted", identity));
        Assert.Equal(1, granted.Revision);

        var denied = await Assert.ThrowsAsync<BrainException>(() =>
            chatOther.InvokeAsync(new(
                "chat.post.v1", """{"text":"should be denied"}""", "cmd-behavior-post-ungranted", identity)));
        Assert.Equal(BrainErrors.GrantMissing, denied.Code);
    }

    [Fact]
    public async Task Approve_of_hash_never_proposed_fails_closed()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        var approveInput = JsonSerializer.Serialize(new { sourceHash = "hash-that-was-never-proposed" });

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            behavior.InvokeAsync(new("behavior.approve.v1", approveInput, "cmd-approve-never-proposed", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
    }

    [Fact]
    public async Task Rollback_to_a_previously_enabled_hash_re_enables_its_grants()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        var chatMain = Neuron("chat", "rollback-target");

        const string sourceA = "behavior source revision a";
        var hashA = Sha256Hex(sourceA);
        var grantsA = new[] { new { address = AddressKey("chat", "rollback-target"), contract = "chat.post.v1" } };
        await behavior.InvokeAsync(new(
            "behavior.propose.v1", ProposeInput(sourceA, hashA, true, grantsA), "cmd-rollback-propose-a", OwnerSession));
        await behavior.InvokeAsync(new(
            "behavior.approve.v1", JsonSerializer.Serialize(new { sourceHash = hashA }), "cmd-rollback-approve-a", OwnerSession));

        const string sourceB = "behavior source revision b";
        var hashB = Sha256Hex(sourceB);
        var grantsB = new[] { new { address = AddressKey("chat", "rollback-target"), contract = "chat.post.v1" } };
        await behavior.InvokeAsync(new(
            "behavior.propose.v1", ProposeInput(sourceB, hashB, true, grantsB), "cmd-rollback-propose-b", OwnerSession));
        await behavior.InvokeAsync(new(
            "behavior.approve.v1", JsonSerializer.Serialize(new { sourceHash = hashB }), "cmd-rollback-approve-b", OwnerSession));

        var rollbackReceipt = await behavior.InvokeAsync(new(
            "behavior.rollback.v1", JsonSerializer.Serialize(new { sourceHash = hashA }), "cmd-rollback-a", OwnerSession));

        var identityA = $"owner|behavior/{hashA}|behavior/{hashA}";
        Assert.Contains("\"status\":\"enabled\"", rollbackReceipt.OutputJson);
        Assert.Contains(identityA, rollbackReceipt.OutputJson);

        var postedAsA = await chatMain.InvokeAsync(new(
            "chat.post.v1", """{"text":"a's identity still works after rollback"}""", "cmd-rollback-post", identityA));
        Assert.Equal(1, postedAsA.Revision);
    }
}
