
namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

// 04-20: activation (load, OnActivate) runs on the grain scheduler, so grain-context APIs work there.
public sealed class ActivationSchedulerFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    [Fact]
    public async Task A_neuron_reads_its_reminders_and_saves_and_publishes_during_activation()
    {
        var id = "b1/probe.activating/" + Guid.NewGuid().ToString("N");
        Assert.Equal("reminders:1;saved:1", await silo.Get<IActivatingProbe>(id).Seen());

        // A restart makes the next activation load stored state, which completes asynchronously: the continuation must stay on the grain.
        await silo.Restart(TestContext.Current.CancellationToken);

        Assert.Equal("reminders:1;saved:2", await silo.Get<IActivatingProbe>(id).Seen());
    }
}

public interface IActivatingProbe
{
    Task<string> Seen();
}

[Neuron("probe.activating", State = "activating")]
public sealed class ActivatingProbe : Neuron<CounterState>, IActivatingProbe
{
    private string _seen = "";

    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        if (State.Value == 0) { await Context.Reminders.Start("activation", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)); }
        var names = await Context.Reminders.Names();
        await Save(State with { Value = State.Value + 1 }, new Counted(State.Value + 1));
        await Publish(new Noted("activated"));
        _seen = $"reminders:{names.Count};saved:{State.Value}";
    }

    public Task<string> Seen() => Task.FromResult(_seen);
}
