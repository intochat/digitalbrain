using System.Text.Json;
using System.Text.RegularExpressions;
using Gherkin;
using Gherkin.Ast;

namespace DigitalBrain.AI;

// The .feature corpus that scripts BddMockChatClient in testing mode. One scenario is one
// scripted turn:
//   Given the user says "<regex over the LAST user message>"
//   When the assistant fires "<contract>" [at "<grainType>/<instance>" | at the chat] with {json}
//   Then the assistant replies "<final assistant text>"
// Every When becomes a real 'fire' tool call through the production pipeline, so the corpus
// must honor pipeline invariants (e.g. a ChartCard's Title names the chart instance the
// preceding points were fired at — the card handler reads the entity by that name).
internal sealed partial class BddScenarioCorpus
{
    private readonly IReadOnlyList<ScriptedScenario> _scenarios;

    private BddScenarioCorpus(IReadOnlyList<ScriptedScenario> scenarios)
    {
        _scenarios = scenarios;
        GivenPatterns = [.. scenarios.Select(static scenario => scenario.Given.ToString())];
    }

    internal IReadOnlyList<string> GivenPatterns { get; }

    // Boot-time stand-in for test hosts that never talk to the LLM (e.g. an E2E health-check
    // smoke): no scenarios are loaded, so a prompt that DOES reach the mock throws
    // MockLlmMissException naming the config key to set, instead of failing host startup.
    internal static BddScenarioCorpus Empty() => new([]);

    internal static BddScenarioCorpus Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException(
                $"The BDD corpus directory '{directory}' does not exist.");
        }

        var featureFiles = Directory
            .EnumerateFiles(directory, "*.feature", SearchOption.AllDirectories)
            .OrderBy(static file => file, StringComparer.Ordinal)
            .ToArray();
        if (featureFiles.Length == 0)
        {
            throw new InvalidOperationException(
                $"The BDD corpus directory '{directory}' contains no .feature files.");
        }

        var parser = new Parser();
        var scenarios = new List<ScriptedScenario>();
        foreach (var featureFile in featureFiles)
        {
            // A file holding only comments parses to a document without a Feature.
            if (parser.Parse(featureFile).Feature is not { } feature)
            {
                continue;
            }

            foreach (var child in feature.Children)
            {
                scenarios.Add(child is Scenario scenario
                    ? ParseScenario(featureFile, scenario)
                    : throw Malformed(
                        featureFile,
                        feature.Name,
                        $"unsupported element '{child.GetType().Name}'; only plain Scenario blocks script the mock"));
            }
        }

        if (scenarios.Count == 0)
        {
            throw new InvalidOperationException(
                $"The BDD corpus in '{directory}' defines no scenarios.");
        }

        return new BddScenarioCorpus(scenarios);
    }

    internal ScriptedScenario? Match(string lastUserMessage)
        => _scenarios.FirstOrDefault(scenario => scenario.Given.IsMatch(lastUserMessage));

    private static ScriptedScenario ParseScenario(string featureFile, Scenario scenario)
    {
        Regex? given = null;
        var fires = new List<ScriptedFire>();
        string? finalReply = null;

        // 'And'/'But' rows continue the section their preceding Given/When/Then opened.
        var section = StepKeywordType.Unspecified;
        foreach (var step in scenario.Steps)
        {
            if (step.KeywordType != StepKeywordType.Conjunction)
            {
                section = step.KeywordType;
            }

            switch (section)
            {
                case StepKeywordType.Context:
                    given = given is null
                        ? ParseGiven(featureFile, scenario.Name, step.Text)
                        : throw Malformed(featureFile, scenario.Name, "has more than one Given step");
                    break;
                case StepKeywordType.Action:
                    fires.Add(ParseFire(featureFile, scenario.Name, step.Text));
                    break;
                case StepKeywordType.Outcome:
                    finalReply = finalReply is null
                        ? ParseReply(featureFile, scenario.Name, step.Text)
                        : throw Malformed(featureFile, scenario.Name, "has more than one Then step");
                    break;
                default:
                    throw Malformed(
                        featureFile, scenario.Name, $"has an unsupported step '{step.Keyword}{step.Text}'");
            }
        }

        if (given is null)
        {
            throw Malformed(featureFile, scenario.Name, "is missing its Given step");
        }

        if (finalReply is null)
        {
            throw Malformed(featureFile, scenario.Name, "is missing its Then step");
        }

        return new ScriptedScenario(scenario.Name, given, fires, finalReply);
    }

    private static Regex ParseGiven(string featureFile, string scenarioName, string stepText)
    {
        var shape = GivenStepShape().Match(stepText);
        if (!shape.Success)
        {
            throw Malformed(
                featureFile,
                scenarioName,
                $"Given must read: the user says \"<regex>\" — got '{stepText}'");
        }

        try
        {
            return new Regex(
                shape.Groups["pattern"].Value,
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException invalidPattern)
        {
            throw Malformed(
                featureFile, scenarioName, $"Given holds an invalid regex: {invalidPattern.Message}");
        }
    }

    private static ScriptedFire ParseFire(string featureFile, string scenarioName, string stepText)
    {
        var shape = WhenStepShape().Match(stepText);
        if (!shape.Success)
        {
            throw Malformed(
                featureFile,
                scenarioName,
                "When must read: the assistant fires \"<contract>\" [at \"<grainType>/<instance>\""
                + $" | at the chat] with {{json}} — got '{stepText}'");
        }

        JsonElement arguments;
        try
        {
            using var document = JsonDocument.Parse(shape.Groups["json"].Value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Malformed(featureFile, scenarioName, "the with-clause must be a JSON object");
            }

            arguments = document.RootElement.Clone();
        }
        catch (JsonException invalidJson)
        {
            throw Malformed(
                featureFile, scenarioName, $"the with-clause holds invalid JSON: {invalidJson.Message}");
        }

        return new ScriptedFire(
            shape.Groups["contract"].Value,
            arguments,
            shape.Groups["target"].Success ? FireTargetOf(shape.Groups["target"].Value) : null,
            TargetsChat: shape.Groups["chat"].Success);
    }

    // The corpus writes targets as "grainType/instance"; SystemTools.ResolveTarget accepts a
    // bare grain type, a bare instance name, or 'type:name' — so the first '/' becomes ':'
    // unless the author already wrote the accepted colon shape.
    private static string FireTargetOf(string corpusTarget)
    {
        if (corpusTarget.Contains(':', StringComparison.Ordinal))
        {
            return corpusTarget;
        }

        var separator = corpusTarget.IndexOf('/', StringComparison.Ordinal);
        return separator < 0
            ? corpusTarget
            : $"{corpusTarget[..separator]}:{corpusTarget[(separator + 1)..]}";
    }

    private static string ParseReply(string featureFile, string scenarioName, string stepText)
    {
        var shape = ThenStepShape().Match(stepText);
        return shape.Success
            ? shape.Groups["reply"].Value
            : throw Malformed(
                featureFile,
                scenarioName,
                $"Then must read: the assistant replies \"<text>\" — got '{stepText}'");
    }

    private static InvalidOperationException Malformed(
        string featureFile, string scenarioName, string reason)
        => new($"BDD corpus scenario '{scenarioName}' in '{featureFile}': {reason}.");

    [GeneratedRegex(
        """^the user says "(?<pattern>.+)"$""",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex GivenStepShape();

    [GeneratedRegex(
        """^the assistant fires "(?<contract>[^"]+)"(?: at (?:(?<chat>the chat)|"(?<target>[^"]+)"))? with (?<json>\{.*\})$""",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex WhenStepShape();

    [GeneratedRegex(
        """^the assistant replies "(?<reply>.+)"$""",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ThenStepShape();
}

internal sealed record ScriptedScenario(
    string Name,
    Regex Given,
    IReadOnlyList<ScriptedFire> Fires,
    string FinalReply);

internal sealed record ScriptedFire(
    string Contract,
    JsonElement Arguments,
    string? Target,
    bool TargetsChat);
