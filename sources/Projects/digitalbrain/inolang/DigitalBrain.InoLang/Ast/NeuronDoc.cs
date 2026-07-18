using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Ast;

public sealed record StateDecl(
    string Name,
    string Type,
    SourceSpan Span);

public sealed record NeuronDoc(
    string Fqn,
    string? Intent,
    IReadOnlyList<UsingDecl> Usings,
    IReadOnlyList<string> Counters,
    IReadOnlyList<Handler> Handlers,
    IReadOnlyList<ScenarioDecl> Scenarios,
    UiDecl? Ui,
    SourceSpan Span,
    IReadOnlyList<StateDecl>? States = null);
