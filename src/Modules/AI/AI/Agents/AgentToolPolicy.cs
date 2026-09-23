namespace DigitalBrain.AI.Agents;

// Phase 0 keeps the default turn small and developer-only code out of it.
public static class AgentToolPolicy
{
    public const int MaxDefaultTools = 8;
    public const string BehaviorToolPrefix = "behavior_";
    public const string BehaviorAuthoringFallback =
        "Authoring C# behaviors isn't supported outside developer mode yet; it arrives in Phase 1.";

    public static readonly IReadOnlyList<string> ProductTools = ["supabase_schema", "show_supabase_query_table", "table_read", "table_refine", "show_form", "show_view", "save_as_app", "open_app"];

    public static IReadOnlyList<string> SelectTools(bool developerMode, IReadOnlyList<string> developerTools)
    {
        ArgumentNullException.ThrowIfNull(developerTools);
        return developerMode ? [.. ProductTools, .. developerTools] : ProductTools;
    }

    public static bool IsBehaviorTool(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(BehaviorToolPrefix, StringComparison.Ordinal);
    }

    // Returns the one-line fallback when the owner explicitly disabled developer mode and
    // asked for behavior authoring; the caller must not reach the model in that case.
    public static string? UnsupportedBehaviorAuthoring(bool developerMode, string message) =>
        !developerMode && RequestsBehaviorAuthoring(message) ? BehaviorAuthoringFallback : null;

    // An absent setting is the required default-on for the local owner; an explicit but
    // unparseable value fails closed instead of silently granting developer tools.
    public static bool DeveloperModeEnabled(string? configured) =>
        configured is null || (bool.TryParse(configured, out var enabled) && enabled);

    public static bool RequestsBehaviorAuthoring(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) { return false; }
        if (message.Contains("behavior", StringComparison.OrdinalIgnoreCase)) { return true; }
        var csharp = message.Contains("c#", StringComparison.OrdinalIgnoreCase)
            || message.Contains("csharp", StringComparison.OrdinalIgnoreCase);
        return csharp
            && (message.Contains("author", StringComparison.OrdinalIgnoreCase)
                || message.Contains("code", StringComparison.OrdinalIgnoreCase)
                || message.Contains("compile", StringComparison.OrdinalIgnoreCase)
                || message.Contains("program", StringComparison.OrdinalIgnoreCase)
                || message.Contains("deploy", StringComparison.OrdinalIgnoreCase));
    }
}