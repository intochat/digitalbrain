namespace DigitalBrain.Connections;

// The stable source names a Connect flow can offer. Shared with the assistant tool schema so a
// connector exists in the product profile only when it is named here.
public static class ConnectionSources
{
    public const string Supabase = "supabase";
    public const string Salesforce = "salesforce";
    public const string Gmail = "gmail";
    public const string WebResearch = "webresearch";
    public const string Mcp = "mcp";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        Supabase,
        Salesforce,
        Gmail,
        WebResearch,
        Mcp,
    };

    public static IReadOnlyCollection<string> All => Known;

    public static bool IsKnown(string? source) => source is not null && Known.Contains(source);

    public static string Require(string? source)
    {
        if (!IsKnown(source))
        {
            throw new ArgumentException(
                $"Unknown connection source '{source}'. Known sources: {string.Join(", ", Known.Order(StringComparer.Ordinal))}.",
                nameof(source));
        }

        return source!;
    }
}