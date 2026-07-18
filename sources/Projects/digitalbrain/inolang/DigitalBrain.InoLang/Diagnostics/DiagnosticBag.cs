using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Diagnostics;

public sealed class DiagnosticBag
{
    readonly List<Diagnostic> _items = [];

    public IReadOnlyList<Diagnostic> Items => _items;
    public bool HasErrors => _items.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void Error(string code, string message, SourceSpan span)
        => _items.Add(new Diagnostic(code, message, span, DiagnosticSeverity.Error));

    public void Warning(string code, string message, SourceSpan span)
        => _items.Add(new Diagnostic(code, message, span, DiagnosticSeverity.Warning));
}
