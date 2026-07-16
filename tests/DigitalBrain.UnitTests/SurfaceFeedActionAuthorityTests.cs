using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
using Xunit;

namespace DigitalBrain.UnitTests;

public sealed class SurfaceFeedActionAuthorityTests
{
    [Fact]
    public void Renew_reissues_send_after_used_or_expired_home_bindings()
    {
        var owner = new BrainOwnerId("local-owner");
        var actor = new ActorId("flutter-ui");
        var createdAt = DateTimeOffset.Parse("2026-07-14T20:25:33.1764648+00:00");
        var state = SurfaceFeedTransitions.Initialize(SurfaceFeedState.Empty(), 0, new SurfaceFeedIdentity(owner, actor));
        var conversationId = "ino-" + RequestScope.Id(owner, actor);
        state = SurfaceFeedTransitions.EnsureHomeSurface(
            state,
            state.Revision,
            new HomeSurfaceBootstrap("bootstrap-stale", conversationId, Guid.NewGuid().ToString("N"), createdAt));
        Assert.Contains(state.ActionBindings, binding => binding.BindingId == ConversationSurfacePayload.SendBindingId);

        var consumed = state.ActionBindings.Single();
        state = state with
        {
            ActionBindings =
            [
                consumed with
                {
                    Uses = consumed.MaxUses,
                    LastIdempotencyKey = "used-once",
                    LastOperationId = "runtime-op-dead"
                }
            ]
        };

        var later = createdAt.AddDays(2);
        state = SurfaceFeedTransitions.RenewActionBindings(state, state.Revision, later);
        var send = Assert.Single(state.ActionBindings);
        Assert.Equal(ConversationSurfacePayload.SendBindingId, send.BindingId);
        Assert.Equal(ConversationSurfacePayload.SendActionType, send.ActionType);
        Assert.Equal(0, send.Uses);
        Assert.Null(send.LastIdempotencyKey);
        Assert.True(send.ExpiresAt > later);
        Assert.Equal(2, state.CurrentSurfaces.Single().SurfaceRevision);
    }

    [Fact]
    public void Home_surface_always_projects_ino_send_when_idle()
    {
        var owner = new BrainOwnerId("owner-a");
        var actor = new ActorId("actor-a");
        var now = DateTimeOffset.UtcNow;
        var state = SurfaceFeedTransitions.Initialize(SurfaceFeedState.Empty(), 0, new SurfaceFeedIdentity(owner, actor));
        state = SurfaceFeedTransitions.EnsureHomeSurface(
            state,
            state.Revision,
            new HomeSurfaceBootstrap("bootstrap-fresh", "ino-" + RequestScope.Id(owner, actor), Guid.NewGuid().ToString("N"), now));
        var send = Assert.Single(state.ActionBindings);
        Assert.Equal(ConversationSurfacePayload.SendBindingId, send.BindingId);
        Assert.Equal(ConversationSurfacePayload.SendActionType, send.ActionType);
    }
}
