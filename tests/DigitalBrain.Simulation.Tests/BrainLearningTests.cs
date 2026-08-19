using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Brain;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

// Deterministic learning pins: the brain's recency and tallies under a controlled clock.
// Own cluster on purpose -- a frozen clock on the shared fixture would poison sibling suites.
public sealed class BrainLearningTests : IAsyncLifetime
{
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
        public DateTimeOffset Now => _now;
    }

    private readonly ManualClock _clock = new();
    private BrainSimulation _sim = null!;

    public async ValueTask InitializeAsync()
        => _sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleAssemblies(
                [typeof(DigitalBrain.Chat.SendMessage).Assembly],
                [typeof(DigitalBrain.UI.UiModule).Assembly, typeof(BrainLearningTests).Assembly]),
            ConfigureSilo = silo =>
                silo.Services.AddKeyedSingleton<TimeProvider>(NeuronTime.ServiceKey, _clock),
        });

    public async ValueTask DisposeAsync() => await _sim.DisposeAsync();

    [Fact]
    public async Task TheBrainRecordsLastUsedFromTheInjectedClock()
    {
        var brain = _sim.BrainFor(_sim.UniqueId("clock-owner"));
        var grain = _sim.Grains.GetGrain<IBrain>(
            EntityId.For<IBrain>(brain.Owner, DigitalBrainNames.DefaultBrain).ToGrainId());
        var first = _clock.Now;

        await grain.Register(new BrainReference(BrainReferenceKind.Entity, "counterentity", "alpha", default));
        _clock.Advance(TimeSpan.FromDays(10));
        await grain.Register(new BrainReference(BrainReferenceKind.Entity, "counterentity", "beta", default));

        var state = await grain.Read();
        Assert.NotNull(state);
        Assert.Equal(first, Assert.Single(state!.Nodes, n => n.Name == "alpha").LastUsed);
        Assert.Equal(first + TimeSpan.FromDays(10), Assert.Single(state.Nodes, n => n.Name == "beta").LastUsed);
    }

    [Fact]
    public async Task RecencyResolutionFollowsTheInjectedClockNotWallTime()
    {
        var brain = _sim.BrainFor(_sim.UniqueId("recency-owner"));
        var grain = _sim.Grains.GetGrain<IBrain>(
            EntityId.For<IBrain>(brain.Owner, DigitalBrainNames.DefaultBrain).ToGrainId());

        // Registered later in wall time, but STALER on the injected clock: alpha wins only
        // if the brain reads the seam, not DateTimeOffset.UtcNow.
        _clock.Advance(TimeSpan.FromDays(10));
        await grain.Register(new BrainReference(BrainReferenceKind.Entity, "counterentity", "alpha", default));
        _clock.Advance(TimeSpan.FromDays(-5));
        await grain.Register(new BrainReference(BrainReferenceKind.Entity, "counterentity", "beta", default));

        var resolved = await grain.Resolve("counter", context: null);
        Assert.NotNull(resolved);
        Assert.Equal("alpha", resolved!.Name);
    }
}
