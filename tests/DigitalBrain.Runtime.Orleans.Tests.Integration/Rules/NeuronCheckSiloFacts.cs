using System.Collections.Concurrent;
using DigitalBrain.Rules;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Rules;

public sealed record CheckCounterState
{
    public int Total { get; init; }
}

[Signal("test.check.added")]
public sealed record Added(int N) : Signal;

public sealed record Positive(int N) : Rule("positive")
{
    public override bool Broken => N <= 0;

    public override string Message => $"{N} is not positive.";
}

public interface ICheckCounter
{
    Task Add(int n);

    Task<int> Total();
}

[Neuron("check.counter")]
public sealed class CheckCounter : Neuron<CheckCounterState>, ICheckCounter
{
    public async Task Add(int n)
    {
        await Check("add", new Positive(n));
        await Save(State with { Total = State.Total + n }, new Added(n));
    }

    public Task<int> Total() => Task.FromResult(State.Total);
}

// The NeuronCheckFacts body on the silo: Rejected reaches a cluster listener, the turn saves nothing and the journal holds no Rejected.
public sealed class NeuronCheckSiloFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    [Fact]
    public async Task RejectedTurnIsHeardAndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = NeuronId.Parse("b/check.counter/c");
        var heard = new ConcurrentQueue<string>();
        await using var listener = await silo.Services.GetRequiredService<DigitalBrain.Spi.IClusterListeners>().Listen<Rejected>("check", (_, rejected, _) =>
        {
            heard.Enqueue(rejected.Operation);
            return Task.CompletedTask;
        });
        var counter = silo.Get<ICheckCounter>(id.ToString());

        await counter.Add(2);
        var error = await Assert.ThrowsAsync<RuleViolationException>(() => counter.Add(-1));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (heard.IsEmpty && DateTime.UtcNow < deadline) { await Task.Delay(50, ct); }

        Assert.Equal(["positive"], error.Rejected.Rules);
        Assert.Equal(2, await counter.Total());
        Assert.Equal(["add"], heard);
        var page = await silo.History("b").Read(id, 0, 10, ct);
        Assert.DoesNotContain(page.Signals, entry => entry.Signal is Rejected);
        Assert.Equal([2], page.Signals.Select(entry => ((Added)entry.Signal).N));
    }
}
