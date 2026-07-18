using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Diagnostics;

public sealed record Diagnostic(
    string Code,
    string Message,
    SourceSpan Span,
    DiagnosticSeverity Severity);
