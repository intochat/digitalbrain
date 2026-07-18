using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Ast;

public abstract record ScenarioStep(SourceSpan Span);

public sealed record GivenNeuronReturns(string Port, Expr Value, SourceSpan Span) : ScenarioStep(Span);
public sealed record GivenPredicate(CallExpr Subject, string Value, SourceSpan Span) : ScenarioStep(Span);
public sealed record WhenInject(string Port, IReadOnlyList<NamedArg> Args, SourceSpan Span) : ScenarioStep(Span);
public sealed record ThenSynapseEmitted(string Port, string? WithField, Expr? WithValue, SourceSpan Span) : ScenarioStep(Span);
public sealed record ThenResourceHas(string Port, Expr Value, SourceSpan Span) : ScenarioStep(Span);
public sealed record ThenCounter(string Counter, long Value, SourceSpan Span) : ScenarioStep(Span);

public sealed record ScenarioDecl(string Name, IReadOnlyList<ScenarioStep> Steps, SourceSpan Span);
