using Orleans;

namespace DigitalBrain.Simulations;

[GenerateSerializer]
[Alias("db.test.asked")]
internal sealed record Asked : Synapse;

[GenerateSerializer]
[Alias("db.test.answered")]
internal sealed record Answered([property: Id(0)] string Text) : Synapse, IAnswer;

internal sealed class Thinker : Neuron, IHandle<Asked>, IEmit<Answered>
{
    public async Task HandleAsync(Asked synapse, CancellationToken cancellationToken)
    {
        var answer = await AskModelAsync(ModelTier.Balanced, "ping", cancellationToken);

        await EmitAsync(new Answered(answer));
    }
}
