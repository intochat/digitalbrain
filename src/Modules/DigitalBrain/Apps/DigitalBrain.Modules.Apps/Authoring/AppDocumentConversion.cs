using System.Text;
using System.Text.RegularExpressions;

namespace DigitalBrain.Apps;

internal static class AppDocumentConversion
{
    public static AppAuthoringDocument Propose(string spec)
    {
        var scenarios = new List<AppScenarioDocument>();
        var preamble = new StringBuilder();
        var body = new StringBuilder();
        string? name = null;
        string? fence = null;
        var live = false;
        foreach (var line in Regex.Split(spec, "(?<=\n)"))
        {
            var trim = line.TrimStart();
            if (trim.StartsWith("```") || trim.StartsWith("~~~"))
            { fence = fence is null ? trim[..3] : trim.StartsWith(fence, StringComparison.Ordinal) ? null : fence; }
            var heading = fence is null ? Regex.Match(line, @"^\s*##\s+Scenario:\s*(\S[^\r\n]*)") : Match.Empty;
            if (heading.Success)
            {
                Flush();
                name = heading.Groups[1].Value.Trim();
                live = name.EndsWith(" (live)", StringComparison.Ordinal);
                if (live) { name = name[..^7]; }
            }
            else { (name is null ? preamble : body).Append(line); }
        }
        Flush();
        var document = new AppAuthoringDocument(1, preamble.ToString(), [], scenarios.ToArray());
        AppDocumentCodec.Validate(document);
        return document;

        void Flush()
        {
            if (name is null) { return; }
            scenarios.Add(new(Guid.NewGuid().ToString("N"), name, body.ToString(), live));
            body.Clear();
        }
    }
}
