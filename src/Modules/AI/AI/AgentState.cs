using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.AI;

// An agent's private working memory: one serialized MAF session per correlation, newest
// last. Bounded, because a Session neuron outlives every conversation it ever held.
[GenerateSerializer]
[Alias("db.ai.agent-state")]
public sealed record AgentState(
    [property: Id(0)] List<AgentSessionEntry> Sessions,
    // Causation, not correlation: a second Ask on the same correlation is a new question.
    [property: Id(1)] List<SignalId> Answered)
{
    // How many conversations an agent keeps before it forgets the oldest.
    public const int MaxSessions = 32;
    public const int MaxAnswered = 64;
}

// One conversation: the correlation that names it and MAF's own serialized session.
[GenerateSerializer]
[Alias("db.ai.agent-session")]
public sealed record AgentSessionEntry([property: Id(0)] string Correlation, [property: Id(1)] string SessionJson);
