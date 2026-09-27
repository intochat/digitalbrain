namespace DigitalBrain.Specs;

internal sealed record ParsedStep(int Line, string Keyword, string Text, string? DocString);
internal sealed record ParsedScenario(string Name, int Line, IReadOnlyList<string> Tags, IReadOnlyList<ParsedStep> Steps);
internal sealed record ParsedFeature(string Name, IReadOnlyList<ParsedStep> Background, IReadOnlyList<ParsedScenario> Scenarios);

internal sealed class SpecSyntaxException(int line, string message) : Exception(message)
{
    public int Line { get; } = line;
}

// The subset of Gherkin a spec needs: Feature, free description, Background, Scenario, tags,
// comments, Given/When/Then/And/But/* steps and """ doc strings. Anything else is a syntax error
// with its line, so an author sees exactly what to fix.
internal static class GherkinParser
{
    private static readonly string[] StepKeywords = ["Given", "When", "Then", "And", "But", "*"];

    public static ParsedFeature Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string? featureName = null;
        var background = new List<ParsedStep>();
        var scenarios = new List<ParsedScenario>();
        var pendingTags = new List<string>();
        List<ParsedStep>? steps = null;
        (string Name, int Line, List<string> Tags)? scenario = null;

        void CloseScenario()
        {
            if (scenario is { } open) { scenarios.Add(new(open.Name, open.Line, open.Tags, steps!)); }
            scenario = null;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#')) { continue; }
            if (line.StartsWith('@'))
            {
                pendingTags.AddRange(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                continue;
            }
            if (TryHeader(line, "Feature", out var name))
            {
                if (featureName is not null) { throw new SpecSyntaxException(lineNumber, "A spec has exactly one Feature."); }
                featureName = name;
                pendingTags.Clear();
                continue;
            }
            if (featureName is null) { throw new SpecSyntaxException(lineNumber, "A spec starts with 'Feature: <name>'."); }
            if (TryHeader(line, "Background", out _))
            {
                if (scenario is not null || scenarios.Count > 0) { throw new SpecSyntaxException(lineNumber, "Background comes before the first Scenario."); }
                steps = background;
                continue;
            }
            if (TryHeader(line, "Scenario Outline", out _) || TryHeader(line, "Scenario Template", out _))
            { throw new SpecSyntaxException(lineNumber, "Scenario Outline is not supported; write each example as its own Scenario."); }
            if (TryHeader(line, "Scenario", out name) || TryHeader(line, "Example", out name))
            {
                CloseScenario();
                if (name.Length == 0) { throw new SpecSyntaxException(lineNumber, "A Scenario needs a name."); }
                scenario = (name, lineNumber, [.. pendingTags]);
                pendingTags.Clear();
                steps = [];
                continue;
            }
            if (StepKeywords.FirstOrDefault(keyword => line.StartsWith(keyword + " ", StringComparison.Ordinal)) is { } stepKeyword)
            {
                if (steps is null) { throw new SpecSyntaxException(lineNumber, "Steps belong to a Background or a Scenario."); }
                var stepText = line[(stepKeyword.Length + 1)..].Trim();
                var docString = ReadDocString(lines, ref index);
                steps.Add(new(lineNumber, stepKeyword, stepText, docString));
                continue;
            }
            if (line.StartsWith('|')) { throw new SpecSyntaxException(lineNumber, "Tables are not supported yet; use a doc string."); }
            if (line.StartsWith("\"\"\"", StringComparison.Ordinal)) { throw new SpecSyntaxException(lineNumber, "A doc string belongs right after a step."); }
            // Free text after a header is description.
            if (steps is null || (steps.Count == 0 && scenario is not null)) { continue; }
            throw new SpecSyntaxException(lineNumber, $"'{line}' is not a step. Steps start with Given, When, Then, And or But.");
        }
        CloseScenario();
        if (featureName is null) { throw new SpecSyntaxException(1, "A spec starts with 'Feature: <name>'."); }
        if (scenarios.Count == 0) { throw new SpecSyntaxException(lines.Length, "A spec needs at least one Scenario."); }
        foreach (var empty in scenarios.Where(item => item.Steps.Count == 0))
        { throw new SpecSyntaxException(empty.Line, $"Scenario '{empty.Name}' has no steps."); }
        return new(featureName, background, scenarios);
    }

    private static bool TryHeader(string line, string keyword, out string name)
    {
        name = "";
        if (!line.StartsWith(keyword + ":", StringComparison.Ordinal)) { return false; }
        name = line[(keyword.Length + 1)..].Trim();
        return true;
    }

    private static string? ReadDocString(string[] lines, ref int index)
    {
        if (index + 1 >= lines.Length || !lines[index + 1].Trim().StartsWith("\"\"\"", StringComparison.Ordinal)) { return null; }
        var opening = index + 1;
        var indent = lines[opening].Length - lines[opening].TrimStart().Length;
        var body = new List<string>();
        for (var cursor = opening + 1; cursor < lines.Length; cursor++)
        {
            if (lines[cursor].Trim() == "\"\"\"")
            {
                index = cursor;
                return string.Join("\n", body);
            }
            var content = lines[cursor];
            var strip = Math.Min(indent, content.Length - content.TrimStart().Length);
            body.Add(content[strip..].TrimEnd('\r'));
        }
        throw new SpecSyntaxException(opening + 1, "This doc string is never closed with \"\"\".");
    }
}
