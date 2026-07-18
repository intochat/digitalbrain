using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Ast;

public sealed record UiDecl(
    string CardName,
    UiWidgetExpr? RootWidget,
    SourceSpan Span)
{
    public string SerializeJson() => RootWidget?.SerializeJson() ?? "{}";
}

public sealed record UiWidgetExpr(
    string Name, // e.g. "Card", "Button", "Column", "Row", "Text", "Input"
    IReadOnlyDictionary<string, string> Arguments,
    IReadOnlyList<UiWidgetExpr> Children,
    SourceSpan Span)
{
    // Argument names whose value is already a JSON array/object literal (a ui: data
    // literal, e.g. EarthGlobe points/arcs). They serialize raw so the client decodes
    // real arrays. Tracked explicitly rather than sniffed from the value's first char,
    // so a scalar string that happens to start with '[' or '{' still serializes quoted.
    public IReadOnlySet<string> RawJsonArgs { get; init; } = EmptyRawJsonArgs;

    static readonly IReadOnlySet<string> EmptyRawJsonArgs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string SerializeJson()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        sb.Append($"\"name\":\"{Name}\",");
        sb.Append("\"arguments\":{");
        bool first = true;
        foreach (var kvp in Arguments)
        {
            if (!first) sb.Append(',');
            first = false;
            var value = kvp.Value;
            if (RawJsonArgs.Contains(kvp.Key))
            {
                sb.Append($"\"{kvp.Key}\":{value}");
            }
            else
            {
                var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
                sb.Append($"\"{kvp.Key}\":\"{escaped}\"");
            }
        }
        sb.Append("},\"children\":[");
        first = true;
        foreach (var child in Children)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(child.SerializeJson());
        }
        sb.Append("]}");
        return sb.ToString();
    }
}
