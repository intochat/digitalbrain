using DigitalBrain.Core;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Tests;

public sealed class IsolationFacts
{
    [Fact(Timeout = 180_000)]
    public async Task StateSurvivesRestartButIsNotSharedBetweenRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new IntegrationOptions
        {
            Modules = [new(typeof(PersistentValueModule))],
            Execution = new() { StartupTimeout = TimeSpan.FromSeconds(45), Diagnostics = d => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "restart-progress.log"), d + Environment.NewLine) }
        };
        await using var first = await ModuleDigitalBrainSimulation.StartAsync(options, ct);
        await first.Get<IPersistentValue>("same").Set(42);
        await using var second = await ModuleDigitalBrainSimulation.StartAsync(options, ct);
        Assert.Equal(0, await second.Get<IPersistentValue>("same").Get());
        await first.RestartRuntimeAsync(ct);
        Assert.Equal(42, await first.Get<IPersistentValue>("same").Get());
    }

    [Fact]
    public async Task AlreadyCanceledStartupDoesNotAcquireResources()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ModuleDigitalBrainSimulation.StartAsync(
            new() { Modules = [new(typeof(PersistentValueModule))] }, canceled.Token));
    }
}

public sealed class PersistentValueModule : IModule
{
    public void Configure(Orleans.Hosting.ISiloBuilder silo) { }
}

[Alias("testing.persistent-value")]
public interface IPersistentValue : DigitalBrain.Contracts.INeuron
{
    Task Set(int value);
    Task<int> Get();
}

[GenerateSerializer]
public sealed class PersistentValueState
{
    [Id(0)] public int Value { get; set; }
}

public sealed class PersistentValue([PersistentState("value")] IPersistentState<PersistentValueState> state)
    : Neuron, IPersistentValue
{
    public async Task Set(int value) { state.State.Value = value; await state.WriteStateAsync(); }
    public Task<int> Get() => Task.FromResult(state.State.Value);
}
