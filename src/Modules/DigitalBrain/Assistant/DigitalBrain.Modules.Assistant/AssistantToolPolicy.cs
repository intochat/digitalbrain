namespace DigitalBrain.Assistant;

// The default turn carries one small set of tools chosen for the intent: an always-on core, the
// generic live-table pair when the request is about data, and the tools of the apps the request or
// the conversation makes relevant. The whole selection stays within MaxDefaultTools.
public static class AssistantToolPolicy
{
    public const int MaxDefaultTools = 8;
    public const string CSharpToolPrefix = "csharp_";

    public static readonly IReadOnlyList<string> CoreTools =
        ["table_read", "table_refine", "show_form", "show_view"];

    public static readonly IReadOnlyList<string> TableTools =
        ["supabase_schema", "show_supabase_query_table", "postgres_schema", "show_postgres_query_table"];

    // The static product list is the core plus the generic live-table pair, used when no app is relevant.
    public static readonly IReadOnlyList<string> ProductTools = [.. CoreTools, .. TableTools];

    public static IReadOnlyList<string> SelectTools(IReadOnlyList<string> authoringTools,
        IReadOnlyList<string>? appTools = null, bool tableIntent = false, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(authoringTools);
        var selected = new List<string>(CoreTools);
        var relevant = appTools ?? [];
        if (relevant.Count == 0 || tableIntent || System.Text.RegularExpressions.Regex.IsMatch(message ?? "", @"\b(?:postgres(?:ql)?|supabase)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        { selected.AddRange(ForDatabase(TableTools, message)); }
        selected.AddRange(relevant);
        var product = selected.Distinct(StringComparer.Ordinal).Take(MaxDefaultTools).ToList();
        product.AddRange(authoringTools);
        return product;
    }

    // An explicit source constrains the allowlist, not merely the model's prompt. If both are
    // named, leave both available so comparisons remain possible; ambiguous requests are clarified.
    public static string? DatabaseSource(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) { return null; }
        var postgres = System.Text.RegularExpressions.Regex.IsMatch(message, @"\bpostgres(?:ql)?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var supabase = System.Text.RegularExpressions.Regex.IsMatch(message, @"\bsupabase\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return postgres == supabase ? null : postgres ? "postgres" : "supabase";
    }

    public static IReadOnlyList<string> ForDatabase(IReadOnlyList<string> tools, string? message)
    {
        var source = DatabaseSource(message);
        if (source is null) { return tools; }
        var other = source == "postgres" ? "supabase" : "postgres";
        return tools.Where(tool => tool != other + "_schema" && tool != "show_" + other + "_query_table").ToArray();
    }
    public static bool IsCSharpTool(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(CSharpToolPrefix, StringComparison.Ordinal);
    }

}
