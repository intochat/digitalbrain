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

    private static string ExtractGrantsHash(string outputJson) =>
        JsonElement.Parse(outputJson).GetProperty("grantsHash").GetString()!;

    private static string GrantsHashOf(params (string Address, string Contract)[] grants)
    {
        var canonical = grants
            .Select(g => new { address = g.Address, contract = g.Contract })
            .OrderBy(g => g.address, StringComparer.Ordinal)
            .ThenBy(g => g.contract, StringComparer.Ordinal)
            .ToArray();
        var json = JsonSerializer.Serialize(canonical, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Sha256Hex(json);
    }

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

        var proposeReceipt = await behavior.InvokeAsync(new(
            "behavior.propose.v1", ProposeInput(source, sourceHash, true, grants), "cmd-approve-propose", OwnerSession));
        var grantsHash = ExtractGrantsHash(proposeReceipt.OutputJson);

        var approveReceipt = await behavior.InvokeAsync(new(
            "behavior.approve.v1", JsonSerializer.Serialize(new { sourceHash, grantsHash }), "cmd-approve", OwnerSession));

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
        var approveInput = JsonSerializer.Serialize(new
        {
            sourceHash = "hash-that-was-never-proposed",
            grantsHash = GrantsHashOf()
        });

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            behavior.InvokeAsync(new("behavior.approve.v1", approveInput, "cmd-approve-never-proposed", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
    }

    [Fact]
    public async Task Approve_rejects_grants_hash_that_no_longer_matches_the_reproposed_grant_set()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        const string source = "escalation guard source";
        var sourceHash = Sha256Hex(source);

        var grantsA = new[] { new { address = AddressKey("chat", "main"), contract = "chat.post.v1" } };
        var proposeA = await behavior.InvokeAsync(new(
            "behavior.propose.v1", ProposeInput(source, sourceHash, true, grantsA), "cmd-escalate-propose-a", OwnerSession));
        var grantsHashA = ExtractGrantsHash(proposeA.OutputJson);

        var grantsB = new[]
        {
            new { address = AddressKey("chat", "main"), contract = "chat.post.v1" },
            new { address = AddressKey("chat", "other"), contract = "chat.post.v1" }
        };
        var proposeB = await behavior.InvokeAsync(new(
            "behavior.propose.v1", ProposeInput(source, sourceHash, true, grantsB), "cmd-escalate-propose-b", OwnerSession));
        var grantsHashB = ExtractGrantsHash(proposeB.OutputJson);

        Assert.NotEqual(grantsHashA, grantsHashB);

        var staleApprove = await Assert.ThrowsAsync<BrainException>(() =>
            behavior.InvokeAsync(new(
                "behavior.approve.v1",
                JsonSerializer.Serialize(new { sourceHash, grantsHash = grantsHashA }),
                "cmd-escalate-approve-stale",
                OwnerSession)));
        Assert.Equal("input.invalid", staleApprove.Code);
        Assert.DoesNotContain((await behavior.ReadEventsAsync(0, 10)).Events, e => e.Kind == "behavior.enabled");

        var currentApprove = await behavior.InvokeAsync(new(
            "behavior.approve.v1",
            JsonSerializer.Serialize(new { sourceHash, grantsHash = grantsHashB }),
            "cmd-escalate-approve-current",
            OwnerSession));
        Assert.Contains("\"status\":\"enabled\"", currentApprove.OutputJson);
    }

    [Fact]
    public async Task Propose_with_grant_targeting_a_behavior_governance_contract_fails_closed()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        const string source = "self approval escalation source";
        var sourceHash = Sha256Hex(source);
        var grants = new[] { new { address = AddressKey("behavior", "some-other"), contract = "behavior.approve.v1" } };

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            behavior.InvokeAsync(new(
                "behavior.propose.v1", ProposeInput(source, sourceHash, true, grants), "cmd-propose-governance-grant", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
        Assert.Empty((await behavior.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Propose_with_grant_targeting_neuron_grant_contract_fails_closed()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        const string source = "grant issuance escalation source";
        var sourceHash = Sha256Hex(source);
        var grants = new[] { new { address = AddressKey("chat", "main"), contract = "neuron.grant.v1" } };

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            behavior.InvokeAsync(new(
                "behavior.propose.v1", ProposeInput(source, sourceHash, true, grants), "cmd-propose-neuron-grant", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
        Assert.Empty((await behavior.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Propose_with_grant_targeting_neuron_revoke_contract_fails_closed()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        const string source = "revoke issuance escalation source";
        var sourceHash = Sha256Hex(source);
        var grants = new[] { new { address = AddressKey("chat", "main"), contract = "neuron.revoke.v1" } };

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            behavior.InvokeAsync(new(
                "behavior.propose.v1", ProposeInput(source, sourceHash, true, grants), "cmd-propose-neuron-revoke", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
        Assert.Empty((await behavior.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Propose_with_grant_targeting_effect_lifecycle_contract_fails_closed()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        const string source = "effect lifecycle escalation source";
        var sourceHash = Sha256Hex(source);
        var grants = new[] { new { address = AddressKey("effect", "some-cmd-id"), contract = "effect.approve.v1" } };

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            behavior.InvokeAsync(new(
                "behavior.propose.v1", ProposeInput(source, sourceHash, true, grants), "cmd-propose-effect-approve", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
        Assert.Empty((await behavior.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Propose_with_grant_targeting_service_propose_effect_contract_succeeds()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        const string source = "service propose effect source";
        var sourceHash = Sha256Hex(source);
        var grants = new[] { new { address = AddressKey("gmail", "main"), contract = "gmail.propose-send.v1" } };

        var receipt = await behavior.InvokeAsync(new(
            "behavior.propose.v1", ProposeInput(source, sourceHash, true, grants), "cmd-propose-service-effect", OwnerSession));

        Assert.Contains("\"status\":\"proposed\"", receipt.OutputJson);
        Assert.Contains($"\"sourceHash\":\"{sourceHash}\"", receipt.OutputJson);
    }

    [Fact]
    public async Task Propose_with_malformed_grant_address_fails_closed()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        const string source = "malformed grant address source";
        var sourceHash = Sha256Hex(source);
        var grants = new[] { new { address = "not-a-valid-grain-key", contract = "chat.post.v1" } };

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            behavior.InvokeAsync(new(
                "behavior.propose.v1", ProposeInput(source, sourceHash, true, grants), "cmd-propose-malformed-address", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
        Assert.Empty((await behavior.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Decline_of_hash_never_proposed_fails_closed()
    {
        var behavior = Neuron("behavior", Guid.NewGuid().ToString("N"));
        var declineInput = JsonSerializer.Serialize(new { sourceHash = "hash-that-was-never-proposed", reason = "nope" });

        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            behavior.InvokeAsync(new("behavior.decline.v1", declineInput, "cmd-decline-never-proposed", OwnerSession)));
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
        var proposeA = await behavior.InvokeAsync(new(
            "behavior.propose.v1", ProposeInput(sourceA, hashA, true, grantsA), "cmd-rollback-propose-a", OwnerSession));
        await behavior.InvokeAsync(new(
            "behavior.approve.v1",
            JsonSerializer.Serialize(new { sourceHash = hashA, grantsHash = ExtractGrantsHash(proposeA.OutputJson) }),
            "cmd-rollback-approve-a",
            OwnerSession));

        const string sourceB = "behavior source revision b";
        var hashB = Sha256Hex(sourceB);
        var grantsB = new[] { new { address = AddressKey("chat", "rollback-target"), contract = "chat.post.v1" } };
        var proposeB = await behavior.InvokeAsync(new(
            "behavior.propose.v1", ProposeInput(sourceB, hashB, true, grantsB), "cmd-rollback-propose-b", OwnerSession));
        await behavior.InvokeAsync(new(
            "behavior.approve.v1",
            JsonSerializer.Serialize(new { sourceHash = hashB, grantsHash = ExtractGrantsHash(proposeB.OutputJson) }),
            "cmd-rollback-approve-b",
            OwnerSession));

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
