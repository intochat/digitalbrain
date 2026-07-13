using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Tests.Runtime;

public sealed class EffectPhaseProjectionTests
{
    [Fact]
    public void TryRead_repairs_only_a_current_legacy_record_with_an_oversized_turn_window()
    {
        var now = DateTimeOffset.Parse("2026-07-12T12:00:00Z");
        var canonical = OperationOutboxRecord.Create(
            "operation:legacy-operation:phase:accepted:v:1",
            "legacy-operation",
            InoOperationPhase.Accepted,
            1,
            now,
            "ino-" + new string('a', 64),
            1,
            "request-legacy-operation",
            "conversation-grain-legacy-operation",
            new OperationFeedView("command-17", string.Empty, false, null, null, null, []));
        var legacyTurns = Enumerable.Range(1, 17)
            .Select(index => new OperationFeedTurn(
                $"command-{index}",
                "user",
                $"turn {index}",
                InoConversationStates.Queued))
            .ToArray();
        var legacy = canonical with { View = canonical.View! with { Turns = legacyTurns } };

        Assert.True(OperationOutboxRecord.TryRead(JsonSerializer.SerializeToUtf8Bytes(legacy), out var repaired));
        Assert.NotNull(repaired);
        Assert.Equal(16, repaired.View!.Turns.Length);
        Assert.Equal("command-2", repaired.View.Turns[0].CommandId);
        Assert.Equal("command-17", repaired.View.Turns[^1].CommandId);

        var opaque = legacy with { EventType = "unknown.event" };
        Assert.False(OperationOutboxRecord.TryRead(JsonSerializer.SerializeToUtf8Bytes(opaque), out _));
    }

    [Fact]
    public void Approved_applying_and_terminal_effect_phases_are_retained_in_order()
    {
        var now = DateTimeOffset.Parse("2026-07-12T12:00:00Z");
        var conversationId = "ino-" + new string('a', 64);
        var state = SurfaceFeedTransitions.Initialize(SurfaceFeedState.Empty(), 0, new(
            new BrainOwnerId("owner"),
            new ActorId("principal")));
        var phases = new[]
        {
            InoOperationPhase.Approved,
            InoOperationPhase.ApplyingEffect,
            InoOperationPhase.Succeeded
        };

        for (var index = 0; index < phases.Length; index++)
        {
            var phase = phases[index];
            var version = index + 2L;
            var record = OperationOutboxRecord.Create(
                $"operation:effect-operation:phase:{phase.ToString().ToLowerInvariant()}:v:{version}",
                "effect-operation",
                phase,
                version,
                now.AddSeconds(index),
                conversationId,
                version,
                "request-effect-operation",
                "conversation-grain-effect-operation",
                new OperationFeedView("command-effect-operation", string.Empty, false, null, "approval-effect-operation", null, []),
                toolId: "salesforce.record.update",
                effectId: "effect-operation",
                approvalId: "approval-effect-operation");
            var payload = record.ToPayloadUtf8();
            state = SurfaceFeedTransitions.ApplyProjection(
                state,
                state.Revision,
                new SurfaceFeedProjection(
                    record.EventId,
                    ConversationSurfacePayload.HomeSurfaceId,
                    index + 1,
                    Convert.ToHexStringLower(SHA256.HashData(payload)),
                    payload,
                    record.OccurredAt,
                    null,
                    []),
                record.OccurredAt);
        }

        var delivered = state.EventHistory.Select(record =>
        {
            Assert.True(OperationOutboxRecord.TryRead(record.PayloadUtf8, out var phase));
            return phase!.Phase;
        }).ToArray();

        Assert.Equal(phases, delivered);
        Assert.Single(state.CurrentSurfaces);
        Assert.True(OperationOutboxRecord.TryRead(state.CurrentSurfaces[0].PayloadUtf8, out var current));
        Assert.Equal(InoOperationPhase.Succeeded, current!.Phase);
    }
}
