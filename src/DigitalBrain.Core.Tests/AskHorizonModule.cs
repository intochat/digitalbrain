namespace DigitalBrain.Core.Tests;

public sealed record Probe(string Topic) : Synapse;

public sealed record ProbeReply(string Text) : Synapse;

public sealed record ReleaseProbe : Synapse;

public sealed class DeferredProber : Neuron, IAnswers<Probe, ProbeReply>, INeuron<ReleaseProbe>
{
    public Task<ProbeReply?> HandleAsync(Probe question, CancellationToken cancellationToken)
        => Task.FromResult<ProbeReply?>(null);

    public Task HandleAsync(ReleaseProbe fact, CancellationToken cancellationToken)
    {
        Emit(new ProbeReply("late"));
        return Task.CompletedTask;
    }
}

public sealed class ProbeContinuation : Neuron, INeuron<StartProbe>, INeuron<ProbeReply>
{
    public Task HandleAsync(StartProbe fact, CancellationToken cancellationToken)
    {
        Ask<ProbeReply>(new Probe(fact.Topic));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ProbeReply fact, CancellationToken cancellationToken)
    {
        Emit(new ProbeContinued(fact.Text));
        return Task.CompletedTask;
    }
}

public sealed record StartProbe(string Topic) : Synapse;

public sealed record ProbeContinued(string Text) : Synapse;
