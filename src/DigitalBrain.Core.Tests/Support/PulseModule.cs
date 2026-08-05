namespace DigitalBrain.Core.Tests.Support;

public sealed record StartPulse(TimeSpan Period) : Synapse;

public sealed record Tick : Synapse;

public sealed record PulseBeat : Synapse;

public sealed class Pulse : Neuron, INeuron<StartPulse>, INeuron<Tick>
{
    public Task HandleAsync(StartPulse fact, CancellationToken cancellationToken)
    {
        Schedule(new Tick(), fact.Period);
        return Task.CompletedTask;
    }

    public Task HandleAsync(Tick fact, CancellationToken cancellationToken)
    {
        Emit(new PulseBeat());
        return Task.CompletedTask;
    }
}

public sealed class PulseObserver : Neuron, INeuron<PulseBeat>
{
    public Task HandleAsync(PulseBeat fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class FailingPulse : Neuron, INeuron<StartPulse>, INeuron<Tick>
{
    public Task HandleAsync(StartPulse fact, CancellationToken cancellationToken)
    {
        Schedule(new Tick(), fact.Period);
        return Task.CompletedTask;
    }

    public Task HandleAsync(Tick fact, CancellationToken cancellationToken)
        => throw new InvalidOperationException("scheduled tick refused");
}

public sealed class SteadyPulse : Neuron, INeuron<StartPulse>, INeuron<StopPulse>, INeuron<Tick>
{
    public Task HandleAsync(StartPulse fact, CancellationToken cancellationToken)
    {
        Schedule(new Tick(), fact.Period);
        return Task.CompletedTask;
    }

    public Task HandleAsync(StopPulse fact, CancellationToken cancellationToken)
    {
        Unschedule<Tick>();
        return Task.CompletedTask;
    }

    public Task HandleAsync(Tick fact, CancellationToken cancellationToken)
    {
        Emit(new PulseBeat());
        return Task.CompletedTask;
    }
}

public sealed record StopPulse : Synapse;
