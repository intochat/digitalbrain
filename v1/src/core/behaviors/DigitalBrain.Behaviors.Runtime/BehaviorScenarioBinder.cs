using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Behaviors.Manifest;

namespace DigitalBrain.Behaviors.Runtime;

internal static class BehaviorScenarioBinder
{
    private static readonly Regex ScenarioLine = new(
        """^\s*Scenario(?:\s+Outline)?\s*:\s*(.+?)\s*$""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline);

    public static IReadOnlyList<GherkinScenario> ParseFeatureScenarios(string featureSource)
    {
        ArgumentNullException.ThrowIfNull(featureSource);

        var scenarios = new List<GherkinScenario>();
        foreach (Match match in ScenarioLine.Matches(featureSource))
        {
            var title = match.Groups[1].Value.Trim();
            if (title.Length == 0)
            {
                continue;
            }

            scenarios.Add(new GherkinScenario(title, ScenarioIdFor(title), BindingKeyFor(title)));
        }

        return scenarios;
    }

    public static IReadOnlyList<BehaviorScenarioManifest> DeriveScenarios(string featureSource)
        => ParseFeatureScenarios(featureSource)
            .Select(static scenario => new BehaviorScenarioManifest(
                scenario.ScenarioId,
                scenario.Title,
                scenario.BindingKey))
            .ToArray();

    public static string ProjectOverview(string displayName, IReadOnlyList<BehaviorScenarioManifest> scenarios)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(scenarios);

        if (scenarios.Count == 0)
        {
            return displayName;
        }

        var titles = scenarios
            .OrderBy(static scenario => scenario.ScenarioId, StringComparer.Ordinal)
            .Select(static scenario => scenario.Title);
        return $"{displayName}: {string.Join("; ", titles)}";
    }

    public static ScenarioBindingEvaluation Bind(
        string featureSource,
        IReadOnlyList<BehaviorScenarioManifest> declaredScenarios,
        IReadOnlyList<BehaviorScenarioResult>? executableResults = null)
    {
        ArgumentNullException.ThrowIfNull(featureSource);
        ArgumentNullException.ThrowIfNull(declaredScenarios);

        var gherkin = ParseFeatureScenarios(featureSource);
        if (gherkin.Count == 0)
        {
            return ScenarioBindingEvaluation.Fail(
                "A behavior proposal must include at least one Gherkin Scenario.",
                0);
        }

        var duplicateTitles = gherkin
            .GroupBy(static scenario => scenario.Title, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (duplicateTitles.Length > 0)
        {
            return ScenarioBindingEvaluation.Fail(
                $"Duplicate Gherkin scenario titles are not allowed: {string.Join(", ", duplicateTitles)}.",
                gherkin.Count);
        }

        var scenarios = declaredScenarios.Count == 0
            ? gherkin.Select(static scenario => new BehaviorScenarioManifest(
                scenario.ScenarioId,
                scenario.Title,
                scenario.BindingKey)).ToArray()
            : declaredScenarios.ToArray();

        if (scenarios.Select(static scenario => scenario.ScenarioId).Distinct(StringComparer.Ordinal).Count() != scenarios.Length)
        {
            return ScenarioBindingEvaluation.Fail("Scenario IDs must be unique.", gherkin.Count);
        }

        if (scenarios.Select(static scenario => scenario.BindingKey).Distinct(StringComparer.Ordinal).Count() != scenarios.Length)
        {
            return ScenarioBindingEvaluation.Fail("Scenario binding keys must be unique.", gherkin.Count);
        }

        var byTitle = scenarios.ToDictionary(static scenario => scenario.Title, StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var scenario in gherkin)
        {
            if (!byTitle.ContainsKey(scenario.Title))
            {
                missing.Add(scenario.Title);
            }
        }

        if (missing.Count > 0)
        {
            return ScenarioBindingEvaluation.Fail(
                $"Missing scenario bindings for Gherkin scenarios: {string.Join(", ", missing.Order(StringComparer.Ordinal))}.",
                gherkin.Count);
        }

        var gherkinTitles = gherkin.Select(static scenario => scenario.Title).ToHashSet(StringComparer.Ordinal);
        var orphaned = scenarios
            .Where(scenario => !gherkinTitles.Contains(scenario.Title))
            .Select(static scenario => scenario.Title)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (orphaned.Length > 0)
        {
            return ScenarioBindingEvaluation.Fail(
                $"Orphaned scenario bindings without Gherkin scenarios: {string.Join(", ", orphaned)}.",
                gherkin.Count);
        }

        if (scenarios.Length != gherkin.Count)
        {
            return ScenarioBindingEvaluation.Fail(
                "Scenario bindings must match Gherkin scenarios one-to-one.",
                gherkin.Count);
        }

        if (executableResults is null)
        {
            return ScenarioBindingEvaluation.Pass(scenarios, gherkin.Count, "bindings accepted");
        }

        if (executableResults.Count == 0)
        {
            return ScenarioBindingEvaluation.Fail(
                "Every Gherkin scenario requires one executable result.",
                gherkin.Count);
        }

        var resultByBinding = new Dictionary<string, BehaviorScenarioResult>(StringComparer.Ordinal);
        foreach (var result in executableResults)
        {
            if (string.IsNullOrWhiteSpace(result.BindingKey)
                || string.IsNullOrWhiteSpace(result.ScenarioId)
                || string.IsNullOrWhiteSpace(result.Title))
            {
                return ScenarioBindingEvaluation.Fail(
                    "Executable scenario results must carry scenario id, title, and binding key.",
                    gherkin.Count);
            }

            if (!resultByBinding.TryAdd(result.BindingKey, result))
            {
                return ScenarioBindingEvaluation.Fail(
                    $"Duplicate executable result for binding '{result.BindingKey}'.",
                    gherkin.Count);
            }
        }

        foreach (var scenario in scenarios)
        {
            if (!resultByBinding.ContainsKey(scenario.BindingKey))
            {
                return ScenarioBindingEvaluation.Fail(
                    $"Missing executable result for scenario '{scenario.Title}'.",
                    gherkin.Count);
            }
        }

        foreach (var result in executableResults)
        {
            if (scenarios.All(scenario => !string.Equals(scenario.BindingKey, result.BindingKey, StringComparison.Ordinal)))
            {
                return ScenarioBindingEvaluation.Fail(
                    $"Orphaned executable result for binding '{result.BindingKey}'.",
                    gherkin.Count);
            }
        }

        if (executableResults.Any(static result => !result.Passed))
        {
            var failed = executableResults
                .Where(static result => !result.Passed)
                .Select(static result => result.Title)
                .Order(StringComparer.Ordinal);
            return ScenarioBindingEvaluation.Fail(
                $"Scenario failures: {string.Join(", ", failed)}.",
                gherkin.Count,
                scenarios);
        }

        return ScenarioBindingEvaluation.Pass(scenarios, gherkin.Count, "all scenarios passed");
    }

    public static string ScenarioIdFor(string title) => $"scenario.{Slug(title)}";

    public static string BindingKeyFor(string title) => $"bind.{Slug(title)}";

    public static string EvidenceJson(
        bool passed,
        int scenarioCount,
        string detail,
        IReadOnlyList<BehaviorScenarioManifest> scenarios)
    {
        var payload = new
        {
            passed,
            scenarios = scenarioCount,
            detail,
            bindings = scenarios
                .OrderBy(static scenario => scenario.ScenarioId, StringComparer.Ordinal)
                .Select(static scenario => new
                {
                    scenarioId = scenario.ScenarioId,
                    title = scenario.Title,
                    bindingKey = scenario.BindingKey,
                })
                .ToArray(),
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string Slug(string title)
    {
        var builder = new StringBuilder(title.Length);
        var previousDash = false;
        foreach (var character in title.Trim())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousDash = false;
                continue;
            }

            if (!previousDash && builder.Length > 0)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        if (builder.Length > 0 && builder[^1] == '-')
        {
            builder.Length--;
        }

        return builder.Length == 0 ? "unnamed" : builder.ToString();
    }
}

internal sealed record GherkinScenario(string Title, string ScenarioId, string BindingKey);

internal sealed record ScenarioBindingEvaluation(
    bool Passed,
    int ScenarioCount,
    string Detail,
    IReadOnlyList<BehaviorScenarioManifest> Scenarios)
{
    public static ScenarioBindingEvaluation Pass(
        IReadOnlyList<BehaviorScenarioManifest> scenarios,
        int scenarioCount,
        string detail)
        => new(true, scenarioCount, detail, scenarios);

    public static ScenarioBindingEvaluation Fail(
        string detail,
        int scenarioCount,
        IReadOnlyList<BehaviorScenarioManifest>? scenarios = null)
        => new(false, scenarioCount, detail, scenarios ?? []);
}
