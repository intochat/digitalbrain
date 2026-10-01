using DigitalBrain.Contracts.Signals;
using System.ComponentModel;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Registry;
using DigitalBrain.Time;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Modules.Registry.Tests.Unit;

public sealed class ObservedRegistryFacts
{
    [Fact]
    public async Task TypesAreDiscoveredFromSelectedModulesWithoutVectorServices()
    {
        await using var brain = await UnitTest.Create().WithModule<RegistryModule>().WithModule<TimeModule>().WithReminders()
            .StartAsync(TestContext.Current.CancellationToken);
        var types = await brain.Get<IRegistry>(RegistryModule.Key).Types();

        Assert.Contains(types, type => type.Id == "timer" && type.Methods.Any(method => method.Contains("Start", StringComparison.Ordinal)));
        var timer = types.Single(type => type.Id == "timer");
        Assert.Equal(typeof(DigitalBrain.Time.Timers.ITimer).FullName, timer.Contract);
        Assert.Equal("time", timer.ModuleId);
        Assert.Contains(timer.Signals!, signal => signal.Contains("TimerTick", StringComparison.Ordinal) && signal.Contains("TimerId", StringComparison.Ordinal));
        Assert.Contains(types, type => type.Id == "reminder");
        Assert.DoesNotContain(types, type => type.Id == "test.registry-clock");
        await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IRegistry>(RegistryModule.Key).Search("schedule a timer", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheIntegrationRegistrationContractIsExcludedFromTheNeuronTypeCatalog()
    {
        await using var brain = await UnitTest.Create().WithModule<RegistryModule>()
            .StartAsync(TestContext.Current.CancellationToken);
        var types = await brain.Get<IRegistry>(RegistryModule.Key).Types();

        Assert.DoesNotContain(types, type => type.Id == "integration.registration" || type.Contract == typeof(DigitalBrain.Platform.Integrations.IIntegrationRegistration).FullName);
    }

    [Fact]
    public async Task TheWholePlatformAssemblyContributesNoNeuronTypesToTheCatalog()
    {
        await using var brain = await UnitTest.Create().WithModule<RegistryModule>()
            .StartAsync(TestContext.Current.CancellationToken);
        var types = await brain.Get<IRegistry>(RegistryModule.Key).Types();

        var platformAssembly = typeof(DigitalBrain.Platform.PlatformHosting).Assembly;
        var platformContracts = platformAssembly.GetExportedTypes().Where(type => type.IsInterface).Select(type => type.FullName);
        Assert.DoesNotContain(types, type => platformContracts.Contains(type.Contract));
    }

    [Fact]
    public async Task ObservationsTrackActivationDeactivationAndSurviveRegistryReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start();
        var registry = brain.Get<IRegistry>(RegistryModule.Key);
        var clock = brain.Get<IRegistryClock>("clock-one");
        await clock.Ping();
        var first = await Wait(registry, instance => instance.Key == "clock-one" && instance.LastKnownActive);

        await brain.DeactivateAsync(clock, ct);
        var stopped = await Wait(registry, instance => instance.Key == "clock-one" && !instance.LastKnownActive);
        Assert.Equal(first.ActivationId, stopped.ActivationId);
        Assert.Equal(first.FirstSeenAt, stopped.FirstSeenAt);

        await brain.DeactivateAsync(registry, ct);
        var persisted = Assert.Single(await registry.Instances("test.registry-clock"));
        Assert.Equivalent(stopped, persisted);

        await clock.Ping();
        var again = await Wait(registry, instance => instance.Key == "clock-one" && instance.LastKnownActive);
        Assert.NotEqual(first.ActivationId, again.ActivationId);
        Assert.Equal(first.FirstSeenAt, again.FirstSeenAt);
        Assert.Single(await registry.Instances("test.registry-clock", activeOnly: true));
        Assert.Empty(await registry.Instances("registry"));
    }

    [Fact]
    public async Task StaleAndDuplicateSignalsCannotDeactivateANewerActivation()
    {
        await using var brain = await Start();
        var sink = brain.Grains.GetGrain<IRegistryObserver>(RegistryModule.Key);
        var registry = brain.Get<IRegistry>(RegistryModule.Key);
        var first = new NeuronActivated
        {
            NeuronId = "clock/history",
            Key = "history",
            TypeIds = ["test.registry-clock"],
            ActivationId = Guid.NewGuid(),
            ObservedAt = DateTimeOffset.Parse("2026-09-29T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        };
        var next = first with { ActivationId = Guid.NewGuid(), ObservedAt = first.ObservedAt.AddSeconds(2) };
        await sink.Observe(first);
        await sink.Observe(next);
        await sink.Observe(next);
        await sink.Observe(new NeuronDeactivated
        {
            NeuronId = first.NeuronId,
            Key = first.Key,
            TypeIds = first.TypeIds,
            ActivationId = first.ActivationId,
            ObservedAt = next.ObservedAt.AddSeconds(1),
        });

        var instance = Assert.Single(await registry.Instances("test.registry-clock", activeOnly: true));
        Assert.Equal(next.ActivationId, instance.ActivationId);
        Assert.Equal(first.ObservedAt, instance.FirstSeenAt);
        Assert.Empty(await registry.Instances("test.registry-clock", skip: 1));
    }

    private static Task<UnitBrain> Start() => UnitTest.Create().WithModule<RegistryModule>().WithModule<RegistryFixtureModule>()
        .StartAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task DuplicateActivationCannotResurrectAnEndedActivation()
    {
        await using var brain = await Start();
        var sink = brain.Grains.GetGrain<IRegistryObserver>(RegistryModule.Key);
        var started = new NeuronActivated
        {
            NeuronId = "clock/duplicate",
            Key = "duplicate",
            TypeIds = ["test.registry-clock"],
            ActivationId = Guid.NewGuid(),
            ObservedAt = DateTimeOffset.UtcNow,
        };
        await sink.Observe(started);
        await sink.Observe(new NeuronDeactivated
        {
            NeuronId = started.NeuronId,
            Key = started.Key,
            TypeIds = started.TypeIds,
            ActivationId = started.ActivationId,
            ObservedAt = started.ObservedAt,
        });
        await sink.Observe(started);

        Assert.False(Assert.Single(await brain.Get<IRegistry>(RegistryModule.Key).Instances("test.registry-clock")).LastKnownActive);
    }

    private static async Task<NeuronInstance> Wait(IRegistry registry, Func<NeuronInstance, bool> done)
        => (await TestWait.UntilAsync(_ => registry.Instances("test.registry-clock"), items => items.Any(done),
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Single(done);
}

public sealed class RegistryFixtureModule : IModule
{
    public void Configure(ISiloBuilder silo) { }
}

[Alias("test.registry-clock"), Description("Schedule alarms and wake up at a requested time.")]
public interface IRegistryClock : INeuron { Task Ping(); }

[GrainType("test-registry-clock")]
public sealed class RegistryClock : Neuron, IRegistryClock
{
    public Task Ping() => Task.CompletedTask;
}
