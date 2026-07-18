using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Ast;

public abstract record Trigger(SourceSpan Span);
public sealed record PortTrigger(string Port, SourceSpan Span) : Trigger(Span);
// v5 C2: BroadcastTrigger replaces v4's SignalFqnTrigger. Same semantics —
// "subscribe to broadcasts of this contract FQN globally, no local port
// needed" — under the v5 one-synapse vocabulary.
public sealed record BroadcastTrigger(string Fqn, SourceSpan Span) : Trigger(Span);
public sealed record LifecycleTrigger(string Name, SourceSpan Span) : Trigger(Span); // activated/...
public sealed record FailureTrigger(string Branch, SourceSpan Span) : Trigger(Span);

public sealed record Predicate(CallExpr Subject, string Expected, SourceSpan Span);

public sealed record Handler(
    Trigger Trigger,
    Predicate? Where,
    IReadOnlyList<Stmt> Body,
    SourceSpan Span);
