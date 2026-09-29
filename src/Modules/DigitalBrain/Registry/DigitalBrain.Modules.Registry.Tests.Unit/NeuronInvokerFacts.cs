using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Registry;
using DigitalBrain.Qdrant;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Modules.Registry.Tests.Unit;

public sealed class NeuronInvokerFacts
{
    [Fact]
    public async Task RecordArgumentsBindFromJsonAndTheResultComesBackTyped()
    {
        await using var brain = await Start();
        var invoker = brain.SiloServices.GetRequiredService<NeuronInvoker>();

        await invoker.Invoke("test.tally/Add", "c1", Json("""{"step":{"amount":3,"reason":"x"}}"""), TestContext.Current.CancellationToken);
        var read = await invoker.Invoke("test.tally/Read", "c1", Json("{}"), TestContext.Current.CancellationToken);

        Assert.Null(read.Error);
        Assert.Equal(3, read.Value!.Value.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ContractFailuresComeBackForRepairAndOthersPropagate()
    {
        await using var brain = await Start();
        var invoker = brain.SiloServices.GetRequiredService<NeuronInvoker>();
        var ct = TestContext.Current.CancellationToken;

        var negative = await invoker.Invoke("test.tally/Add", "c2", Json("""{"step":{"amount":-1}}"""), ct);
        var missing = await invoker.Invoke("test.tally/Add", "c2", Json("{}"), ct);
        var unknown = await invoker.Invoke("test.tally/Nope", "c2", Json("{}"), ct);

        Assert.Equal(nameof(ArgumentOutOfRangeException), negative.Error);
        Assert.Equal(nameof(ArgumentException), missing.Error);
        Assert.Equal(nameof(KeyNotFoundException), unknown.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.Invoke("test.tally/Break", "c2", Json("{}"), ct));
    }

    [Fact]
    public async Task EveryCallableMethodIsListedOncePerName()
    {
        await using var brain = await Start();

        var methods = brain.SiloServices.GetRequiredService<NeuronRegistry>().Methods.Where(method => method.Contract.Id == "test.tally");

        Assert.Equal(["test.tally/Add", "test.tally/Break", "test.tally/Read"], methods.Select(method => method.Id).Order());
        Assert.Contains("Amount", methods.Single(method => method.Id == "test.tally/Add").SearchText, StringComparison.Ordinal);
    }

    private static Task<UnitBrain> Start() => UnitTest.Create().WithModule<QdrantModule>().WithModule<RegistryModule>()
        .WithModule<TallyModule>().StartAsync(TestContext.Current.CancellationToken);

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    public sealed class TallyModule : IModule
    {
        public void Configure(ISiloBuilder silo) { }
    }
}

[GenerateSerializer, Alias("test.tally-step")]
public sealed record TallyStep([property: Id(0)] int Amount, [property: Id(1)] string? Reason = null);

[GenerateSerializer, Alias("test.tally-total")]
public sealed record TallyTotal([property: Id(0)] int Total);

[Alias("test.tally")]
public interface ITally : INeuron
{
    Task Add(TallyStep step, CancellationToken cancellationToken = default);
    Task<TallyTotal> Read();
    Task Break();
}

[GrainType("test-tally")]
public sealed class Tally : Neuron, ITally
{
    private int _total;

    public Task Add(TallyStep step, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(step.Amount);
        _total += step.Amount;
        return Task.CompletedTask;
    }

    public Task<TallyTotal> Read() => Task.FromResult(new TallyTotal(_total));

    public Task Break() => throw new InvalidOperationException("broken");
}