namespace DigitalBrain.AI.Agents;

internal static class AgentToolPolicy
{
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
}
