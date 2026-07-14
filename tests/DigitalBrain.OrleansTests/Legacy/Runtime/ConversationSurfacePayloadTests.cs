using System.Text;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;

namespace DigitalBrain.Tests.Runtime;

public sealed class ConversationSurfacePayloadTests
{
    [Theory]
    [InlineData(InoOperationPhase.Approved, "approved", InoConversationStates.Queued)]
    [InlineData(InoOperationPhase.ApplyingEffect, "applying-effect", InoConversationStates.Running)]
    public void Build_preserves_the_canonical_effect_phase_without_changing_its_lifecycle_state(
        InoOperationPhase phase,
        string expectedPhase,
        string expectedState)
    {
        var record = OperationOutboxRecord.Create(
            "effect-phase-" + phase.ToString().ToLowerInvariant(),
            "operation-effect-phase",
            phase,
            3,
            DateTimeOffset.Parse("2026-07-12T12:00:00Z"),
            "ino-" + new string('a', 64),
            7,
            "request-effect-phase",
            "conversation-grain-effect-phase",
            new OperationFeedView("command-effect-phase", string.Empty, false, null, "approval-effect-phase", null, []));

        var payload = ConversationSurfacePayload.Build(record.ToSnapshot());
        var operation = payload.GetProperty("data").GetProperty("operation");

        Assert.Equal(expectedPhase, operation.GetProperty("phase").GetString());
        Assert.Equal(expectedState, operation.GetProperty("state").GetString());
    }

    [Fact]
    public void Build_omits_a_structurally_invalid_persisted_action()
    {
        var conversation = new InoConversationSnapshot(
            "ino-" + new string('a', 64),
            3,
            [new InoConversationTurn("command-1", "assistant", "Connect Salesforce to continue.", InoConversationStates.Succeeded)],
            [new InoConversationOperation(
                "command-1",
                "connect salesforce",
                InoConversationStates.Succeeded,
                null,
                false,
                DateTimeOffset.UtcNow,
                new ToolAction("openUrl", "Connect Salesforce", "https://login.salesforce.com/services/oauth2/authorize?raw=legacy"),
                null,
                null)]);

        var payload = ConversationSurfacePayload.Build(conversation);

        var operation = payload.GetProperty("data").GetProperty("operation");
        Assert.False(operation.TryGetProperty("action", out _));
        Assert.DoesNotContain("login.salesforce.com", payload.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_keeps_a_structurally_valid_internal_action()
    {
        var conversation = new InoConversationSnapshot(
            "ino-" + new string('a', 64),
            3,
            [new InoConversationTurn("command-1", "assistant", "Connect Salesforce to continue.", InoConversationStates.AwaitingAuthorization)],
            [new InoConversationOperation(
                "command-1",
                "connect salesforce",
                InoConversationStates.AwaitingAuthorization,
                null,
                false,
                DateTimeOffset.UtcNow,
                new ToolAction(
                    "openUrl",
                    "Connect Salesforce",
                    OAuthCallbackPaths.CreateInternalStartPath("salesforce", new string('a', 32))),
                null,
                null)]);

        var payload = ConversationSurfacePayload.Build(conversation);

        var action = payload.GetProperty("data").GetProperty("operation").GetProperty("action");
        Assert.Equal("Connect Salesforce", action.GetProperty("label").GetString());
    }

    [Fact]
    public void Build_caps_each_projection_turn_and_the_total_payload_by_utf8_bytes()
    {
        var oversized = string.Concat(Enumerable.Repeat("😀", 3_000));
        var conversation = new InoConversationSnapshot(
            "ino-" + new string('b', 64),
            7,
            Enumerable.Range(0, 24)
                .Select(index => new InoConversationTurn(
                    "command-" + index,
                    index % 2 == 0 ? "user" : "assistant",
                    oversized,
                    InoConversationStates.Running))
                .ToArray(),
            [new InoConversationOperation(
                "operation-1",
                "command-23",
                "bounded payload",
                InoConversationStates.Running,
                null,
                false,
                DateTimeOffset.UtcNow,
                Version: 9)]);

        var payload = ConversationSurfacePayload.Build(conversation);
        var messages = payload.GetProperty("data").GetProperty("messages").EnumerateArray().ToArray();

        Assert.InRange(Encoding.UTF8.GetByteCount(payload.GetRawText()), 1, 64 * 1024);
        Assert.All(messages, message => Assert.InRange(
            Encoding.UTF8.GetByteCount(message.GetProperty("text").GetString()!),
            1,
            2_048));
        var operation = payload.GetProperty("data").GetProperty("operation");
        Assert.Equal("operation-1", operation.GetProperty("operationId").GetString());
        Assert.Equal("running", operation.GetProperty("phase").GetString());
        Assert.Equal(9, operation.GetProperty("version").GetInt64());
    }

    [Fact]
    public void Build_projects_capability_and_proposal_receipts_without_leaking_the_prompt()
    {
        var conversation = new InoConversationSnapshot(
            "ino-" + new string('a', 64),
            3,
            [new InoConversationTurn("command-1", "assistant", "I can help with that.", InoConversationStates.Succeeded)],
            [new InoConversationOperation(
                "operation-1",
                "command-1",
                "read my records",
                InoConversationStates.Succeeded,
                null,
                false,
                DateTimeOffset.UtcNow,
                null,
                null,
                null,
                1,
                null,
                null,
                null,
                new CapabilityResolutionReceipt(
                    CapabilityResolutionKind.Match,
                    "salesforce.record.read.v1",
                    "Read Salesforce records",
                    [],
                    0.92),
                new FeatureDraftReference(
                    "proposal-0123456789abcdef0123456789abcdef",
                    "Open Studio",
                    "/features/proposals/proposal-0123456789abcdef0123456789abcdef"))]);

        var payload = ConversationSurfacePayload.Build(conversation);
        var operation = payload.GetProperty("data").GetProperty("operation");

        Assert.Equal("salesforce.record.read.v1", operation.GetProperty("capability").GetProperty("id").GetString());
        Assert.Equal("Read Salesforce records", operation.GetProperty("capability").GetProperty("name").GetString());
        Assert.Equal("match", operation.GetProperty("capability").GetProperty("kind").GetString());
        Assert.Equal("proposal-0123456789abcdef0123456789abcdef", operation.GetProperty("proposal").GetProperty("id").GetString());
        Assert.Equal(
            "/features/proposals/proposal-0123456789abcdef0123456789abcdef",
            operation.GetProperty("proposal").GetProperty("route").GetString());
        Assert.DoesNotContain("prompt", payload.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_omits_capability_and_proposal_blocks_when_the_operation_carries_neither()
    {
        var conversation = new InoConversationSnapshot(
            "ino-" + new string('a', 64),
            3,
            [],
            [new InoConversationOperation(
                "operation-1",
                "command-1",
                "read my records",
                InoConversationStates.Succeeded,
                null,
                false,
                DateTimeOffset.UtcNow)]);

        var payload = ConversationSurfacePayload.Build(conversation);
        var operation = payload.GetProperty("data").GetProperty("operation");

        Assert.False(operation.TryGetProperty("capability", out _));
        Assert.False(operation.TryGetProperty("proposal", out _));
    }
}
