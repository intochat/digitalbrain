using System.Diagnostics;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Brain;
using DigitalBrain.Abstractions.Identity;
using Xunit;

namespace DigitalBrain.E2E.Tests;

// C3's parked C2 debt item: the Brain is a single serialized hot grain, and no measurement was
// ever taken against the real Default blob provider. This closes it with data, not a threshold --
// the only assertion is a never-flakes ceiling; the ms/op numbers logged below are the deliverable
// (copied into the C4 spec outcome).
[Collection(E2ECollection.Name)]
public sealed class BrainWriteBudgetSmoke(AppHostFixture fixture)
{
    private const int RegisterCount = 100;
    private const int RouteCount = 100;
    private const int ResolveCount = 20;
    private static readonly TimeSpan RunCeiling = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task RegisterRouteAndResolveStayUnderTheRunCeiling()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var grains = await fixture.GrainsAsync();

        // A fresh owner per run: an untouched brain grain, no state accumulated by any other
        // test in the shared fixture's lifetime.
        var owner = new OwnerId($"budget-{Guid.NewGuid():N}"[..12]);
        var grain = grains.GetGrain<IBrain>(
            EntityId.For<IBrain>(owner, DigitalBrainNames.DefaultBrain).ToGrainId());

        var total = Stopwatch.StartNew();

        // 100 distinct registrations, well under BrainState.MaximumNodes (256) -- each is a
        // full-state write, the exact cost this measurement exists to surface.
        var registerElapsed = Stopwatch.StartNew();
        for (var i = 0; i < RegisterCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await grain.Register(new BrainReference(
                BrainReferenceKind.Entity, "counterentity", $"budget-{i:D3}", default));
        }

        registerElapsed.Stop();

        // The same (source, alias) miss, repeated: a pure read over the grain's connection list
        // (nothing was ever wired), isolating Route's read cost from Register's write cost.
        var missSource = new NeuronId("chart", owner, "budget-probe");
        var routeElapsed = Stopwatch.StartNew();
        for (var i = 0; i < RouteCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await grain.Route(missSource, "budget-alias");
        }

        routeElapsed.Stop();

        // Resolves against the 100 registered nodes' shared type -- a read plus the per-context
        // recency/tally scoring Resolve performs on every call.
        var resolveElapsed = Stopwatch.StartNew();
        for (var i = 0; i < ResolveCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await grain.Resolve("counter", context: null);
        }

        resolveElapsed.Stop();
        total.Stop();

        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine(
            $"Register: {RegisterCount} calls, {registerElapsed.Elapsed.TotalMilliseconds:F1} ms total, "
            + $"{registerElapsed.Elapsed.TotalMilliseconds / RegisterCount:F2} ms/op");
        output?.WriteLine(
            $"Route: {RouteCount} calls, {routeElapsed.Elapsed.TotalMilliseconds:F1} ms total, "
            + $"{routeElapsed.Elapsed.TotalMilliseconds / RouteCount:F2} ms/op");
        output?.WriteLine(
            $"Resolve: {ResolveCount} calls, {resolveElapsed.Elapsed.TotalMilliseconds:F1} ms total, "
            + $"{resolveElapsed.Elapsed.TotalMilliseconds / ResolveCount:F2} ms/op");
        output?.WriteLine($"Whole run: {total.Elapsed.TotalMilliseconds:F1} ms");

        Assert.True(
            total.Elapsed < RunCeiling,
            $"{RegisterCount} registers + {RouteCount} routes + {ResolveCount} resolves took "
            + $"{total.Elapsed} against the real Default blob provider -- exceeded the "
            + $"{RunCeiling} never-flakes ceiling.");
    }
}
