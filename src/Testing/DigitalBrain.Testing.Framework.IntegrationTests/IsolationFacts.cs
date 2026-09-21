using DigitalBrain.Core;
using Orleans;
using Orleans.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests;

public sealed class IsolationFacts
{
    [Fact(Timeout = 180_000)]
    public async Task ProviderAdapterIsLoadedInsideExternalRuntime()
    {
        await using var brain = await IntegrationTest.Create().WithModule<PersistentValueModule>()
            .StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(73, await brain.Get<IPersistentValue>("provider").ProviderValue());
        Assert.NotEqual(Environment.ProcessId,
            await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<int>(brain.HttpClient, "/process", TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 180_000)]
    public async Task StateSurvivesRestartButIsNotSharedBetweenRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        static IntegrationTestBuilder Create() => IntegrationTest.Create().WithModule<PersistentValueModule>()
            .WithExecution(new() { StartupTimeout = TimeSpan.FromSeconds(45), Diagnostics = d => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "restart-progress.log"), d + Environment.NewLine) });
        await using var first = await Create().StartAsync(ct);
        await first.Get<IPersistentValue>("same").Set(42);
        await using var second = await Create().StartAsync(ct);
        Assert.NotEqual(first.HttpClient.BaseAddress!.Port, second.HttpClient.BaseAddress!.Port);
        Assert.Equal(0, await second.Get<IPersistentValue>("same").Get());
        await first.RestartRuntimeAsync(ct);
        Assert.Equal(42, await first.Get<IPersistentValue>("same").Get());
    }

    [Fact]
    public async Task AlreadyCanceledStartupDoesNotAcquireResources()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => IntegrationTest.Create().WithModule<PersistentValueModule>()
            .StartAsync(canceled.Token));
    }
}

public sealed class PersistentValueModule : IModule
{
    public void Configure(Orleans.Hosting.ISiloBuilder silo)
        => silo.Services.AddSingleton<IValueProvider>(new FixedValueProvider());
}

public interface IValueProvider { int Value { get; } }
public sealed class FixedValueProvider : IValueProvider { public int Value => 73; }

[Alias("testing.persistent-value")]
public interface IPersistentValue : DigitalBrain.Contracts.INeuron
{
    Task Set(int value);
    Task<int> Get();
    Task<int> ProviderValue();
}

[GenerateSerializer]
public sealed class PersistentValueState
{
    [Id(0)] public int Value { get; set; }
}

public sealed class PersistentValue([PersistentState("value")] IPersistentState<PersistentValueState> state, IValueProvider provider)
    : Neuron, IPersistentValue
{
    public async Task Set(int value) { state.State.Value = value; await state.WriteStateAsync(); }
    public Task<int> Get() => Task.FromResult(state.State.Value);
    public Task<int> ProviderValue() => Task.FromResult(provider.Value);
}
