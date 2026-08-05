namespace DigitalBrain.Core.Tests;

public sealed record UserAsked(string Text) : Synapse;

public sealed record MemoryQuery(string Query) : Synapse;

public sealed record MemoryHit(string Snippet) : Synapse;

public sealed record AssistantSaid(string Text) : Synapse;

// Chat spine (sc37): hear user turn → Ask memory → continue on MemoryHit → speak.
// Continuation is INeuron<TReply>, never Answer<> or same-turn await of the answerer.
public sealed class RecallChat : Neuron, INeuron<UserAsked>, INeuron<MemoryHit>
{
    public Task HandleAsync(UserAsked fact, CancellationToken cancellationToken)
    {
        Ask<MemoryHit>(new MemoryQuery(fact.Text));
        return Task.CompletedTask;
    }

    public Task HandleAsync(MemoryHit fact, CancellationToken cancellationToken)
    {
        Emit(new AssistantSaid(fact.Snippet));
        return Task.CompletedTask;
    }
}

public sealed class EpisodicMemory : Neuron, IAnswers<MemoryQuery, MemoryHit>
{
    public Task<MemoryHit?> HandleAsync(MemoryQuery question, CancellationToken cancellationToken)
        => Task.FromResult<MemoryHit?>(new($"recall:{question.Query}"));
}

// Ambient catalog listener for AssistantSaid — Emit requires a declared kind (S02 trap).
public sealed class AssistantLedger : Neuron, INeuron<AssistantSaid>
{
    public Task HandleAsync(AssistantSaid fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
