using DigitalBrain.Contracts.Signals;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;

namespace DigitalBrain.Kernel.Tests.Unit;

public sealed class RuntimeSignalFacts
{
    [Fact]
    public async Task SiloSubscriptionsReceiveOrdinaryNeuronSignalsAndFilterByType()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        using var signals = brain.SiloServices.GetRequiredService<LocalSignalHub>().Subscribe<Number>();
        await brain.Get<ITestEmitter>("one").EmitText("ignored");
        await brain.Get<ITestEmitter>("one").Emit(1);
        await brain.Get<ITestEmitter>("two").Emit(2);
        Assert.Equal(1, (await signals.Reader.ReadAsync(ct)).Value);
        Assert.Equal(2, (await signals.Reader.ReadAsync(ct)).Value);
        Assert.False(signals.Reader.TryRead(out _));
        signals.Dispose();
        await brain.Get<ITestEmitter>("one").Emit(3);
        await signals.Reader.Completion;
    }

    [Fact]
    public async Task StartupAnnouncesModulesThroughTheSharedHubAndInventoryRemainsAvailable()
    {
        var hub = new LocalSignalHub(NullLogger<LocalSignalHub>.Instance);
        var inventory = new ModuleInventory([typeof(RuntimeFixtureModule)]);
        using var subscription = hub.Subscribe<ModuleLoaded>();
        await new RuntimeStartupTask(inventory, hub).Execute(TestContext.Current.CancellationToken);
        Assert.Equal(typeof(RuntimeFixtureModule).AssemblyQualifiedName,
            (await subscription.Reader.ReadAsync(TestContext.Current.CancellationToken)).ModuleType);
        Assert.Equal([typeof(RuntimeFixtureModule)], inventory.Types);
    }
    [Fact]
    public async Task LifecycleSignalsDoNotDependOnDerivedHooksCallingBase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        using var signals = brain.SiloServices.GetRequiredService<LocalSignalHub>().Subscribe<NeuronActivity>();
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
        using var signals = brain.SiloServices.GetRequiredService<LocalSignalHub>().Subscribe<NeuronActivity>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IRuntimeProbe>("fail").Ping());
        await brain.Get<IRuntimeProbe>("healthy").Ping();

        var firstActivation = Assert.IsType<NeuronActivated>(await signals.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Equal("healthy", firstActivation.Key);
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
