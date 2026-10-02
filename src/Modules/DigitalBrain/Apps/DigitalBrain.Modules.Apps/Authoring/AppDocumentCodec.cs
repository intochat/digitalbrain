using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DigitalBrain.Apps;

internal static class AppDocumentCodec
{
    public const string Path = "app.authoring.json";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Encode(AppAuthoringDocument document) => JsonSerializer.Serialize(document, Json);
    public static string Hash(AppAuthoringDocument document) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Encode(document))));

    public static void Validate(AppAuthoringDocument d, IReadOnlyDictionary<string, string>? files = null, bool requireSources = false)
    {
        if (d is null || d.Version != 1 || d.Behaviors is null || d.Scenarios is null || d.Preamble is null)
        { throw new ArgumentException("Unsupported or incomplete authoring document."); }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        CheckText(d.Preamble);
        foreach (var s in d.Scenarios)
        {
            if (s is null || !Guid.TryParse(s.Id, out _) || !ids.Add(s.Id) || string.IsNullOrWhiteSpace(s.Name) || s.Name != s.Name.Trim() || s.Name.Contains('\n') || s.Name.Contains('\r') || s.Name.Contains('\t') || s.Name.EndsWith("(live)", StringComparison.Ordinal) || !names.Add(s.Name))
            { throw new ArgumentException("Scenarios need unique IDs and exact, unique single-line names without tabs."); }
            CheckText(s.Body);
        }
        var scenarioIds = ids.ToHashSet(StringComparer.Ordinal);
        foreach (var b in d.Behaviors)
        {
            if (b is null || !Guid.TryParse(b.Id, out _) || !ids.Add(b.Id) || string.IsNullOrWhiteSpace(b.Title) || b.Title.Contains('\n') || b.Title.Contains('\r') || b.SourcePaths is null || b.ScenarioIds is null)
            { throw new ArgumentException("Behaviors need unique IDs, titles and reference arrays."); }
            CheckText(b.Title);
            CheckText(b.Description);
            if (b.ScenarioIds.Distinct(StringComparer.Ordinal).Count() != b.ScenarioIds.Length || b.ScenarioIds.Any(id => !scenarioIds.Contains(id)))
            { throw new ArgumentException("A behavior refers to an unknown or repeated scenario."); }
            foreach (var path in b.SourcePaths)
            {
                if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') || path.Split('/').Any(p => p is "" or "." or "..") || !(path == "app.cs" || path.StartsWith("behaviors/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)))
                { throw new ArgumentException("Source references must be behavior file keys inside this package."); }
                if (requireSources && (files is null || !files.ContainsKey(path)))
                { throw new ArgumentException($"Source file '{path}' is missing from this revision."); }
            }
        }
    }

    private static void CheckText(string? text)
    {
        if (text is null || Regex.IsMatch(text, @"(?m)^\s*##\s+Scenario\s*:"))
        { throw new ArgumentException("Use scenario records instead of embedding scenario headings in descriptions."); }
    }

    public static string ExportSpec(AppAuthoringDocument d)
    {
        Validate(d);
        var text = new StringBuilder(d.Preamble).Append("\n\n");
        foreach (var b in d.Behaviors) { text.Append("### ").Append(b.Title).Append("\n\n").Append(b.Description).Append("\n\n"); }
        foreach (var s in d.Scenarios) { text.Append("## Scenario: ").Append(s.Name).Append(s.IsLive ? " (live)" : "").Append("\n\n").Append(s.Body).Append("\n\n"); }
        return text.ToString();
    }

    public static AppDocumentReadResult Read(PackageContent content)
    {
        var read = Read(content.File(PackageContent.SpecPath) ?? "", content.File(Path));
        if (read.Document is not { } document) { return read; }
        try
        {
            Validate(document, content.Programs(), true);
            if (ExportSpec(document) != read.OriginalSpec)
            { throw new ArgumentException("The authoring metadata does not match this revision's specification."); }
            return read;
        }
        catch (ArgumentException e) { return new(null, read.OriginalSpec, false, e.Message); }
    }
    public static AppDocumentReadResult Read(string spec, string? metadata)
    {
        if (metadata is null) { return new(null, spec, false, null); }
        try
        {
            var document = JsonSerializer.Deserialize<AppAuthoringDocument>(metadata, Json) ?? throw new ArgumentException("Empty authoring document.");
            Validate(document);
            return new(document, spec, true, null);
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        { return new(null, spec, false, $"Authoring metadata is read-only: {e.Message}"); }
    }
}
