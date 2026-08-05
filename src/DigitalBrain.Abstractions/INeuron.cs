namespace DigitalBrain;

// Listener: hearing IS the behavior. The default implementations make the bodiless
// declaration-only listener legal (`class Diary : Neuron, INeuron<DayPlanned>;`) and give
// synchronous handlers a zero-ceremony surface: override void Hear for verb-only turns,
// override Task HandleAsync when the turn awaits its own IO. Core always invokes
// HandleAsync; the pair is one delivery path with two author surfaces. Override exactly
// one — a custom HandleAsync never calls Hear unless you call it yourself.
public interface INeuron<in TFact>
    where TFact : Synapse
{
    Task HandleAsync(TFact fact, CancellationToken cancellationToken)
    {
        Hear(fact);
        return Task.CompletedTask;
    }

    void Hear(TFact fact)
    {
    }
}

// Answerer: at most ONE kind per question type across the composition (two+ fails boot;
// declaring the interface without overriding either member fails boot). Return the reply
// to answer this turn; return null to defer — Core keeps the ask durably open and the
// neuron's later emission of a TReply-typed fact closes it (multi-turn answers: chat
// with tools, approvals, fan-out).
public interface INeuron<in TQuestion, TReply>
    where TQuestion : Synapse<TReply>
    where TReply : Synapse
{
    Task<TReply?> HandleAsync(TQuestion question, CancellationToken cancellationToken)
        => Task.FromResult(Answer(question));

    TReply? Answer(TQuestion question) => null;
}
