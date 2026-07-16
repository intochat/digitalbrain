using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;

var owner = new BrainOwnerId("local-owner");
var actor = new ActorId("flutter-ui");
var createdAt = DateTimeOffset.Parse("2026-07-14T20:25:33.1764648+00:00");
var state = SurfaceFeedTransitions.Initialize(SurfaceFeedState.Empty(), 0, new SurfaceFeedIdentity(owner, actor));
var conversationId = "ino-" + RequestScope.Id(owner, actor);
state = SurfaceFeedTransitions.EnsureHomeSurface(state, state.Revision, new HomeSurfaceBootstrap("bootstrap-stale", conversationId, Guid.NewGuid().ToString("N"), createdAt));
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
Console.WriteLine($"bindings={state.ActionBindings.Length} rev={state.CurrentSurfaces.Single().SurfaceRevision}");
foreach (var b in state.ActionBindings)
    Console.WriteLine($"{b.BindingId} uses={b.Uses} exp={b.ExpiresAt:o} last={b.LastIdempotencyKey}");
if (state.ActionBindings is not [{ BindingId: "ino.send", Uses: 0 }])
    Environment.Exit(2);
Console.WriteLine("PASS");
