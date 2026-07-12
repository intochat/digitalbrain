using System.Security.Cryptography;
using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Tests.Runtime;

public sealed class EffectPhaseProjectionTests
{
    [Fact]
    public void Approved_applying_and_terminal_effect_phases_are_retained_in_order()
    {
        var now = DateTimeOffset.Parse("2026-07-12T12:00:00Z");
        var conversationId = "ino-" + new string('a', 64);
        var state = SurfaceFeedTransitions.Initialize(SurfaceFeedState.Empty(), 0, new(
            new TenantId("tenant"),
            new WorkspaceId("workspace"),
            new PrincipalRef("principal", PrincipalKind.User)));
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
