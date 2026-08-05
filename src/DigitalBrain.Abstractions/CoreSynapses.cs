namespace DigitalBrain;

// ── topology (handled by Core on the receiving emitter). Connect/Disconnect/Schedule/
//    Unschedule are RESERVED kinds: a module declaring INeuron<> for any of them fails
//    boot, so Core's interception is never ambiguous with module dispatch. The outcome
//    kinds (ConnectionRefused, DeliveryFailed, AskExpired, ScheduleFailed) are ordinary
//    listenable facts — self-healing is one line: Hear(ScheduleFailed f) => Schedule(…).
public sealed record Connect(string Fact, NeuronId To) : Synapse;

public sealed record Disconnect(string Fact, NeuronId To) : Synapse;

public sealed record ConnectionRefused(SynapseRef Request, string Fact, NeuronId To, string Reason) : Synapse;

// ── delivery outcomes (journaled on the sender; any module may listen) ─────────────────
public sealed record DeliveryFailed(SynapseRef Fact, NeuronId Receiver, string Reason, int Attempts) : Synapse;

// ── asks (journaled on the asker) ──────────────────────────────────────────────────────
public sealed record AskExpired(SynapseRef Ask, string Question) : Synapse;

// ── time (facts to a neuron mutate its Core-owned schedule table at commit; the same
//    table is what the in-turn Schedule/Unschedule verbs write — one mechanism; an
//    Unschedule naming an unknown/unscheduled kind is a journaled no-op reception) ──────
public sealed record Schedule(Synapse Fact, TimeSpan Period) : Synapse;

public sealed record Unschedule(string Fact) : Synapse;

public sealed record ScheduleFailed(string Fact, string Reason, int ConsecutiveFailures) : Synapse;
