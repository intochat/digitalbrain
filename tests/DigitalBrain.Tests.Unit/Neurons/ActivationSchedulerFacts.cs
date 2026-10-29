using DigitalBrain.Spi;

namespace DigitalBrain.Tests.Unit.Neurons;

// 04-20: activation stays on the host's scheduler (an Orleans grain's activation scheduler) across a state load that completes
// elsewhere, so OnActivate can use grain-context APIs such as reminders and timers.
public sealed class ActivationSchedulerFacts
{
    [Fact]
    public async Task OnActivate_runs_on_the_scheduler_that_started_activation_when_the_state_load_completes_on_another_thread()
    {
        var neuron = new SchedulerProbe();
        var context = new RecordingContext(NeuronId.Parse("b1/probe.scheduler/one")) { LoadsOffThread = true };
        context.Store(new ProbeState { Value = 1 });
        NeuronAttachment.Attach(neuron, context);
        var host = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;

        await Task.Factory.StartNew(() => NeuronAttachment.Activate(neuron, CancellationToken.None), CancellationToken.None, TaskCreationOptions.None, host).Unwrap();

        Assert.Same(host, neuron.ActivatedOn);
        Assert.Same(host, neuron.SavedOn);
        Assert.Equal(2, ((ProbeState)Assert.Single(context.Writes).State).Value);
    }
}

public sealed record ProbeState
{
    public int Value { get; init; }
}

public sealed record ProbeActivated(int Value) : Signal;

public sealed class SchedulerProbe : Neuron<ProbeState>
{
    public TaskScheduler? ActivatedOn { get; private set; }

    public TaskScheduler? SavedOn { get; private set; }

    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        ActivatedOn = TaskScheduler.Current;
        await Save(State with { Value = State.Value + 1 }, new ProbeActivated(State.Value + 1));
        SavedOn = TaskScheduler.Current;
    }
}
