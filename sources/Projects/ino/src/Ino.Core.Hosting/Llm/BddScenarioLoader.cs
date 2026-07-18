using System.Text.RegularExpressions;
using Gherkin;
using Gherkin.Ast;

namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Walks a directory for <c>*.feature</c> files and extracts one
/// <see cref="BddScenario"/> per Gherkin Scenario (plus one row per
/// Scenario Outline Examples row). The convention consumed here:
///
///   <c>Given the user says "find flights to Bali"</c>
///   <c>Then the assistant replies "Searching flights…"</c>
///
/// The quoted text in the first <c>Given</c> step becomes the prompt pattern
/// (treated as a regex — literal strings work, <c>\d+</c> etc. also work).
/// The quoted text in the first <c>Then</c> step becomes the reply. Scenarios
/// that don't follow this shape are skipped with a note in
/// <see cref="LoadResult.SkippedReasons"/> — callers can surface skipped
/// scenarios in a startup log.
/// </summary>
public static class BddScenarioLoader
{
    static readonly Regex QuotedText = new("\"([^\"]*)\"", RegexOptions.Compiled);

    public sealed record LoadResult(
        IReadOnlyList<BddScenario> Scenarios,
        IReadOnlyList<string> SkippedReasons);

    public static LoadResult LoadFromDirectories(IEnumerable<string> directories)
    {
        var scenarios = new List<BddScenario>();
        var skipped = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parser = new Parser();

        foreach (var dir in directories.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.feature", SearchOption.AllDirectories))
            {
                if (!seenPaths.Add(file)) continue;
                LoadFile(parser, file, scenarios, skipped);
            }
        }

        return new LoadResult(scenarios, skipped);
    }

    public static LoadResult LoadFromString(string featureContent, string sourceName = "<inline>")
    {
        var parser = new Parser();
        var scenarios = new List<BddScenario>();
        var skipped = new List<string>();
        using var reader = new StringReader(featureContent);
        try
        {
            var document = parser.Parse(reader);
            AppendScenarios(document, sourceName, scenarios, skipped);
        }
        catch (Exception ex)
        {
            skipped.Add($"parse-error {sourceName}: {ex.Message}");
        }
        return new LoadResult(scenarios, skipped);
    }

    static void LoadFile(Parser parser, string path, List<BddScenario> scenarios, List<string> skipped)
    {
        try
        {
            var document = parser.Parse(path);
            AppendScenarios(document, path, scenarios, skipped);
        }
        catch (Exception ex)
        {
            skipped.Add($"parse-error {path}: {ex.Message}");
        }
    }

    static void AppendScenarios(GherkinDocument doc, string source, List<BddScenario> scenarios, List<string> skipped)
    {
        var feature = doc.Feature;
        if (feature is null)
        {
            skipped.Add($"no-feature {source}");
            return;
        }

        var featureTitle = feature.Name ?? Path.GetFileNameWithoutExtension(source);
        var featureTags = feature.Tags.Select(t => t.Name).ToArray();

        foreach (var child in feature.Children.OfType<Scenario>())
        {
            var scenarioTags = featureTags
                .Concat(child.Tags.Select(t => t.Name))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var steps = child.Steps.ToList();
            var given = steps.FirstOrDefault(s => s.Keyword.TrimEnd().Equals("Given", StringComparison.OrdinalIgnoreCase));
            var then = steps.FirstOrDefault(s => s.Keyword.TrimEnd().Equals("Then", StringComparison.OrdinalIgnoreCase));

            if (given is null || then is null)
            {
                skipped.Add($"shape {source} / {child.Name}: expected Given + Then steps");
                continue;
            }

            var givenText = ExtractQuoted(given.Text);
            var thenText = ExtractQuoted(then.Text);
            if (givenText is null || thenText is null)
            {
                skipped.Add($"quotes {source} / {child.Name}: Given/Then must quote the prompt and reply");
                continue;
            }

            var examples = child.Examples?.ToList() ?? new List<Examples>();
            if (examples.Count == 0)
            {
                scenarios.Add(new BddScenario(
                    FeatureTitle: featureTitle,
                    ScenarioName: child.Name ?? "(unnamed scenario)",
                    PromptPattern: givenText,
                    ReplyText: thenText,
                    Tags: scenarioTags,
                    SourceFile: source));
                continue;
            }

            foreach (var ex in examples)
            {
                var header = ex.TableHeader?.Cells.Select(c => c.Value).ToArray() ?? Array.Empty<string>();
                foreach (var row in ex.TableBody ?? Array.Empty<TableRow>())
                {
                    var values = row.Cells.Select(c => c.Value).ToArray();
                    var prompt = SubstitutePlaceholders(givenText, header, values);
                    var reply = SubstitutePlaceholders(thenText, header, values);
                    scenarios.Add(new BddScenario(
                        FeatureTitle: featureTitle,
                        ScenarioName: $"{child.Name} [{string.Join(",", values)}]",
                        PromptPattern: prompt,
                        ReplyText: reply,
                        Tags: scenarioTags,
                        SourceFile: source));
                }
            }
        }
    }

    static string? ExtractQuoted(string step)
    {
        var match = QuotedText.Match(step);
        return match.Success ? match.Groups[1].Value : null;
    }

    static string SubstitutePlaceholders(string template, string[] header, string[] values)
    {
        var result = template;
        for (var i = 0; i < header.Length && i < values.Length; i++)
            result = result.Replace($"<{header[i]}>", values[i], StringComparison.Ordinal);
        return result;
    }
}
