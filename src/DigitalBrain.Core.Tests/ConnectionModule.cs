namespace DigitalBrain.Core.Tests;

public sealed record StageSaid(string Note) : Synapse;

public sealed class StageSpeaker : Neuron, INeuron<PlanDay>
{
    public Task HandleAsync(PlanDay fact, CancellationToken cancellationToken)
    {
        Emit(new StageSaid($"day-{fact.Date:yyyyMMdd}"));
        return Task.CompletedTask;
    }
}

public sealed class StageAudience : Neuron, INeuron<StageSaid>
{
    public Task HandleAsync(StageSaid fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class StageArchive : Neuron, INeuron<StageSaid>
{
    public Task HandleAsync(StageSaid fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class SilentPeer : Neuron
{
}
