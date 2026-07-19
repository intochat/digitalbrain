using Orleans;

namespace DigitalBrain.ProbeHost;

[GenerateSerializer]
[Alias("probe.remembered")]
internal sealed record Remembered([property: Id(0)] string What) : Synapse;

internal sealed class Recorder : Neuron, IHandle<Remembered>
{
    public Task HandleAsync(Remembered synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}

[GenerateSerializer]
[Alias("probe.asked")]
internal sealed record Asked([property: Id(0)] string Question) : Synapse;

[GenerateSerializer]
[Alias("probe.answered")]
internal sealed record Answered([property: Id(0)] string Text) : Synapse, IAnswer;

internal sealed class Asker : Neuron, IHandle<Asked>, IEmit<Answered>
{
    public async Task HandleAsync(Asked synapse, CancellationToken cancellationToken)
        => await EmitAsync(new Answered(await AskModelAsync(ModelTier.Balanced, synapse.Question, cancellationToken)));
}
