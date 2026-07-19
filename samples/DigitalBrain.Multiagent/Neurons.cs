using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Orleans;

namespace DigitalBrain.Multiagent;

[GenerateSerializer]
[Alias("multiagent.question-asked")]
internal sealed record QuestionAsked([property: Id(0)] string Question) : Synapse;

[GenerateSerializer]
[Alias("multiagent.opinion-wanted")]
internal sealed record OpinionWanted([property: Id(0)] string Question) : Synapse;

[GenerateSerializer]
[Alias("multiagent.opinion-offered")]
internal sealed record OpinionOffered([property: Id(0)] string From, [property: Id(1)] string Opinion) : Synapse;

[GenerateSerializer]
[Alias("multiagent.verdict-reached")]
internal sealed record VerdictReached([property: Id(0)] string Verdict) : Synapse;

internal sealed class Moderator : Neuron, IHandle<QuestionAsked>, IEmit<OpinionWanted>, IHandle<OpinionOffered>, IEmit<VerdictReached>
{
    private readonly List<string> _heard = [];

    public async Task HandleAsync(QuestionAsked synapse, CancellationToken cancellationToken)
    {
        await SendAsync(NeuronId.For<Optimist>(Id.Owner, "one"), new OpinionWanted(synapse.Question));
        await SendAsync(NeuronId.For<Skeptic>(Id.Owner, "one"), new OpinionWanted(synapse.Question));
    }

    public async Task HandleAsync(OpinionOffered synapse, CancellationToken cancellationToken)
    {
        _heard.Add($"{synapse.From} says \"{synapse.Opinion}\"");

        if (_heard.Count == 2)
        {
            await EmitAsync(new VerdictReached(string.Join(" and ", _heard)));
        }
    }
}

internal sealed class Optimist : Neuron, IHandle<OpinionWanted>, IEmit<OpinionOffered>
{
    public Task HandleAsync(OpinionWanted synapse, CancellationToken cancellationToken)
        => ReplyAsync(new OpinionOffered(nameof(Optimist), "ship it"));
}

internal sealed class Skeptic : Neuron, IHandle<OpinionWanted>, IEmit<OpinionOffered>
{
    public Task HandleAsync(OpinionWanted synapse, CancellationToken cancellationToken)
        => ReplyAsync(new OpinionOffered(nameof(Skeptic), "measure it first"));
}

internal sealed class Scribe : Neuron, IHandle<VerdictReached>
{
    public Task HandleAsync(VerdictReached synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}
