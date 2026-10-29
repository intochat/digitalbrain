using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

public sealed class SynapseMutationFacts(SynapseSilo silo) : IClassFixture<SynapseSilo>
{
    [Fact]
    public async Task AnOrdinaryMutationCannotEnterWhileAStreamReactionIsSuspended()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Source = "b1/probe.counter/held-reaction";
        var listener = silo.Get<IHeldReaction>("b1/probe.held-reaction/listener");
        await listener.Follow(Source);
        var publishing = silo.Get<ICounter>(Source).Add(1);
        await HeldReactionNeuron.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        var mutation = listener.Mutate();
        bool enteredBeforeReactionCompleted;
        try { enteredBeforeReactionCompleted = await Task.WhenAny(mutation, Task.Delay(200, ct)) == mutation; }
        finally { HeldReactionNeuron.Release.TrySetResult(); }
        await Task.WhenAll(publishing, mutation).WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.False(enteredBeforeReactionCompleted);
    }

    [Fact]
    public async Task AReactionCanSaveEnsureSaveRemoveAndSaveWithoutLosingItsCursor()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "b1/probe.changing-subscriptions/save-and-follow", Source = "b1/probe.counter/save-and-follow";
        var listener = silo.Get<IChangingSubscriptions>(Listener);
        await listener.Follow(Source);
        var failures = silo.Counters.ReactionFailures;
        await silo.Get<ICounter>(Source).Add(1);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await listener.Stage() < 3 && silo.Counters.ReactionFailures == failures && DateTime.UtcNow < deadline)
        { await Task.Delay(20, ct); }
        Assert.Equal(3, await listener.Stage());
        Assert.Equal(failures, silo.Counters.ReactionFailures);

        await silo.Restart(ct);
        Assert.Equal(3, await silo.Get<IChangingSubscriptions>(Listener).Stage());
    }
}

public interface IHeldReaction
{
    Task Follow(string source);
    Task Mutate();
}

[Neuron("probe.held-reaction")]
public sealed class HeldReactionNeuron : Neuron, IHeldReaction
{
    internal static TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Follow(string source) => Context.Synapses.Ensure<Counted>(NeuronId.Parse(source));
    public Task Mutate() => Task.CompletedTask;

    [On]
    public async Task On(Counted signal)
    {
        Heard.Add(Id.ToString(), "entered");
        Entered.TrySetResult();
        await Release.Task;
    }
}

public interface IChangingSubscriptions
{
    Task Follow(string source);
    Task<int> Stage();
}

public sealed record ChangingSubscriptionsState
{
    public string Source { get; init; } = "";
    public int Stage { get; init; }
}

[Neuron("probe.changing-subscriptions")]
public sealed class ChangingSubscriptionsNeuron : Neuron<ChangingSubscriptionsState>, IChangingSubscriptions
{
    public async Task Follow(string source)
    {
        await Save(State with { Source = source }, new Noted("configured"));
        await Context.Synapses.Ensure<Counted>(NeuronId.Parse(source));
    }

    public Task<int> Stage() => Task.FromResult(State.Stage);

    [On]
    public async Task On(Counted signal)
    {
        await Save(State with { Stage = 1 }, new Noted("first"));
        await Context.Synapses.Ensure<Noted>(NeuronId.Parse(State.Source));
        await Save(State with { Stage = 2 }, new Noted("second"));
        await Context.Synapses.Remove(NeuronId.Parse(State.Source));
        await Save(State with { Stage = 3 }, new Noted("third"));
    }
}
