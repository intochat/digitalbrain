using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;

namespace DigitalBrain.Tests;

public sealed class RuntimeSignalFacts
{
    [Fact]
    public async Task LateSubscribersReceiveTheModuleSnapshotAndFutureSignals()
    {
        var signals = new RuntimeSignals(NullLogger<RuntimeSignals>.Instance);
        signals.Publish(new ModuleLoaded("module-a"));
        using var subscription = signals.Subscribe();
        signals.Publish(new ModuleLoaded("module-b"));
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal("module-a", Assert.IsType<ModuleLoaded>(await subscription.Reader.ReadAsync(ct)).ModuleType);
        Assert.Equal("module-b", Assert.IsType<ModuleLoaded>(await subscription.Reader.ReadAsync(ct)).ModuleType);
        Assert.Equal(["module-a", "module-b"], signals.Modules.Select(module => module.ModuleType));
    }

    [Fact]
    public async Task StartupAnnouncesSelectedModulesWithoutRegistry()
    {
        await using var brain = await UnitTest.Create().WithModule<RuntimeFixtureModule>()
            .StartAsync(TestContext.Current.CancellationToken);
        var signals = brain.SiloServices.GetRequiredService<RuntimeSignals>();
        using var subscription = signals.Subscribe();

        Assert.Equal(typeof(RuntimeFixtureModule).AssemblyQualifiedName,
            Assert.IsType<ModuleLoaded>(await subscription.Reader.ReadAsync(TestContext.Current.CancellationToken)).ModuleType);
    }

    [Fact]
    public async Task LifecycleSignalsDoNotDependOnDerivedHooksCallingBase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        using var signals = brain.SiloServices.GetRequiredService<RuntimeSignals>().Subscribe();
        var neuron = brain.Get<IRuntimeProbe>("runtime-probe");
        await neuron.Ping();
        var activated = Assert.IsType<NeuronActivated>(await signals.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Contains("test.runtime-probe", activated.TypeIds);
        Assert.Equal("runtime-probe", activated.Key);

        await neuron.Deactivate();
        var deactivated = Assert.IsType<NeuronDeactivated>(await signals.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(activated.ActivationId, deactivated.ActivationId);
        Assert.Equal(activated.NeuronId, deactivated.NeuronId);

        await neuron.Ping();
        var again = Assert.IsType<NeuronActivated>(await signals.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(activated.NeuronId, again.NeuronId);
        Assert.NotEqual(activated.ActivationId, again.ActivationId);
    }

    [Fact]
    public async Task FailedActivationIsNotReportedAsActive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        using var signals = brain.SiloServices.GetRequiredService<RuntimeSignals>().Subscribe();
        await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IRuntimeProbe>("fail").Ping());
        Assert.False(signals.Reader.TryRead(out _));
    }

    public sealed class RuntimeFixtureModule : IModule
    {
        public void Configure(ISiloBuilder silo) { }
    }
}

[Alias("test.runtime-probe")]
public interface IRuntimeProbe : INeuron
{
    Task Ping();
    Task Deactivate();
}

[GrainType("test-runtime-probe")]
public sealed class RuntimeProbe : Neuron, IRuntimeProbe
{
    public override Task OnActivateAsync(CancellationToken cancellationToken)
        => this.GetPrimaryKeyString() == "fail" ? throw new InvalidOperationException("activation failed") : Task.CompletedTask;

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Ping() => Task.CompletedTask;
    public Task Deactivate() { DeactivateOnIdle(); return Task.CompletedTask; }
}