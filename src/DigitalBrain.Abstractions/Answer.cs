namespace DigitalBrain;

// The continuation view: Core pairs the reply with the original typed question,
// reconstructed from the asker's own journal at Answers.Sequence. Never journaled, never
// emittable by modules (Emit refuses it) — the reply fact is the journal record; this
// record exists only at dispatch.
public sealed record Answer<TQuestion, TReply>(TQuestion Question, TReply Reply) : Synapse
    where TQuestion : Synapse<TReply>
    where TReply : Synapse;
