using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Gherkin;
using Gherkin.Ast;
using Reqnroll.Bindings;

namespace DigitalBrain.SmartPrompt;

public sealed class BehaviorCompiler : IBehaviorCompiler
{
    private const int MaximumSourceLength = 128 * 1024;
    private const int MaximumScenarios = 64;
    private const int MaximumStepsPerScenario = 64;

    private readonly BehaviorStepCatalog _catalog;

    private BehaviorCompiler(BehaviorStepCatalog catalog) => _catalog = catalog;

    public IReadOnlyList<BehaviorStepSuggestion> Suggestions => _catalog.Suggestions;

    public static BehaviorCompiler CreateDefault() => new(BehaviorStepCatalog.CreateDefault());

    public BehaviorCompilation Compile(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Failed("BEH001", "A behavior feature must not be blank.");
        }
        if (source.Length > MaximumSourceLength)
        {
            return Failed("BEH002", $"A behavior feature cannot exceed {MaximumSourceLength} characters.");
        }

        GherkinDocument document;
        try
        {
            document = new Parser().Parse(new StringReader(source));
        }
        catch (CompositeParserException exception)
        {
            return Failed("BEH001", exception.Message);
        }

        var scenarios = document.Feature.Children.OfType<Scenario>().ToArray();
        var diagnostics = new List<BehaviorDiagnostic>();
        if (scenarios.Length > MaximumScenarios)
        {
            diagnostics.Add(Error("BEH002", $"A feature cannot contain more than {MaximumScenarios} scenarios."));
        }

        var behaviors = new List<BehaviorScenarioPlan>();
        var tests = new List<BehaviorTestPlan>();
        foreach (var scenario in scenarios)
        {
            if (scenario.Steps.Count() > MaximumStepsPerScenario)
            {
                diagnostics.Add(Error(
                    "BEH002",
                    $"Scenario '{scenario.Name}' cannot contain more than {MaximumStepsPerScenario} steps.",
                    scenario.Location.Line,
                    scenario.Location.Column));
                continue;
            }

            var tags = scenario.Tags.Select(static tag => tag.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var isBehavior = tags.Contains("@behavior");
            var isTest = tags.Contains("@test");
            if (isBehavior == isTest)
            {
                diagnostics.Add(Error(
                    "BEH005",
                    $"Scenario '{scenario.Name}' must have exactly one of @behavior or @test.",
                    scenario.Location.Line,
                    scenario.Location.Column));
                continue;
            }

            var calls = Bind(scenario, diagnostics);
            if (calls is null)
            {
                continue;
            }

            if (isBehavior)
            {
                var triggerKey = TriggerKey(calls, scenario, diagnostics);
                if (triggerKey is not null)
                {
                    behaviors.Add(new BehaviorScenarioPlan(scenario.Name, triggerKey, calls));
                }
            }
            else
            {
                tests.Add(new BehaviorTestPlan(scenario.Name, calls));
            }
        }

        if (behaviors.Count == 0)
        {
            diagnostics.Add(Error("BEH006", "A feature requires at least one @behavior scenario."));
        }
        if (tests.Count == 0)
        {
            diagnostics.Add(Error("BEH006", "A feature requires at least one paired @test scenario."));
        }
        foreach (var behavior in behaviors)
        {
            var pairCount = tests.Count(test => test.Steps.Any(step =>
                step.Role == BehaviorStepRole.Invoke
                && string.Equals(step.Arguments.FirstOrDefault(), behavior.Name, StringComparison.Ordinal)));
            if (pairCount == 0)
            {
                diagnostics.Add(Error(
                    "BEH008",
                    $"Behavior scenario '{behavior.Name}' must be invoked by at least one @test scenario; found none."));
            }
        }

        if (diagnostics.Any(static diagnostic => diagnostic.Severity == BehaviorDiagnosticSeverity.Error))
        {
            return new BehaviorCompilation(null, diagnostics);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        return new BehaviorCompilation(
            new BehaviorPlan(document.Feature.Name, hash, behaviors, tests),
            diagnostics);
    }

    private IReadOnlyList<BehaviorStepCall>? Bind(Scenario scenario, List<BehaviorDiagnostic> diagnostics)
    {
        var calls = new List<BehaviorStepCall>();
        StepDefinitionType? previousType = null;
        foreach (var step in scenario.Steps)
        {
            var expectedType = step.KeywordType switch
            {
                StepKeywordType.Context => StepDefinitionType.Given,
                StepKeywordType.Action => StepDefinitionType.When,
                StepKeywordType.Outcome => StepDefinitionType.Then,
                StepKeywordType.Conjunction => previousType,
                _ => null,
            };
            if (expectedType is null)
            {
                diagnostics.Add(Error("BEH003", $"Unsupported step keyword for '{step.Text}'.", step.Location.Line, step.Location.Column));
                continue;
            }

            previousType = expectedType;
            var matches = _catalog.Definitions
                .Where(definition => definition.Type == expectedType && definition.Expression.Match(step.Text).Success)
                .Select(definition => (Definition: definition, Match: definition.Expression.Match(step.Text)))
                .ToArray();
            if (matches.Length == 0)
            {
                diagnostics.Add(Error("BEH003", $"No step binding matches '{step.Text}'.", step.Location.Line, step.Location.Column));
                continue;
            }
            if (matches.Length > 1)
            {
                diagnostics.Add(Error("BEH004", $"More than one step binding matches '{step.Text}'.", step.Location.Line, step.Location.Column));
                continue;
            }

            var match = matches[0];
            calls.Add(new BehaviorStepCall(
                step.Keyword.Trim(),
                step.Text,
                match.Definition.Name,
                match.Definition.Role,
                match.Match.Groups.Cast<Group>().Skip(1).Select(static group => group.Value).ToArray(),
                step.Location.Line));
        }

        return calls.Count == scenario.Steps.Count() ? calls : null;
    }

    private static string? TriggerKey(
        IReadOnlyList<BehaviorStepCall> calls,
        Scenario scenario,
        List<BehaviorDiagnostic> diagnostics)
    {
        var triggers = calls.Where(static call => call.Role == BehaviorStepRole.Trigger).ToArray();
        if (triggers.Length != 1)
        {
            diagnostics.Add(Error(
                "BEH007",
                $"Behavior scenario '{scenario.Name}' requires exactly one trigger step.",
                scenario.Location.Line,
                scenario.Location.Column));
            return null;
        }

        var trigger = triggers[0];
        if (trigger.Binding == nameof(BuiltInBehaviorSteps.NewXPost))
        {
            var account = calls.FirstOrDefault(static call => call.Binding == nameof(BuiltInBehaviorSteps.XAccount));
            if (account is null)
            {
                diagnostics.Add(Error("BEH007", "An X.Post trigger requires X.Account setup.", trigger.Line));
                return null;
            }
            return $"x.post/account:{account.Arguments[0].Trim().ToLowerInvariant()}";
        }

        var mapping = trigger.Binding switch
        {
            nameof(BuiltInBehaviorSteps.NewEmail) => ("email.received", nameof(BuiltInBehaviorSteps.EmailAccount)),
            nameof(BuiltInBehaviorSteps.CalendarEventStarts) => ("calendar.event", nameof(BuiltInBehaviorSteps.Calendar)),
            nameof(BuiltInBehaviorSteps.MarketPriceChanges) => ("market.price", nameof(BuiltInBehaviorSteps.MarketSymbol)),
            nameof(BuiltInBehaviorSteps.FileCreated) => ("file.created", nameof(BuiltInBehaviorSteps.Folder)),
            nameof(BuiltInBehaviorSteps.HealthMetricRecorded) => ("health.metric", nameof(BuiltInBehaviorSteps.HealthMetric)),
            nameof(BuiltInBehaviorSteps.GitHubIssueOpened) => ("github.issue", nameof(BuiltInBehaviorSteps.GitHubRepository)),
            nameof(BuiltInBehaviorSteps.LocationEntered) => ("location.entered", nameof(BuiltInBehaviorSteps.Geofence)),
            _ => default,
        };
        if (mapping == default)
        {
            diagnostics.Add(Error("BEH007", $"Trigger binding '{trigger.Binding}' has no trigger-key mapping.", trigger.Line));
            return null;
        }

        var setup = calls.FirstOrDefault(call => call.Binding == mapping.Item2);
        if (setup is null)
        {
            diagnostics.Add(Error("BEH007", $"Trigger '{trigger.Text}' requires its source setup step.", trigger.Line));
            return null;
        }
        return $"{mapping.Item1}/source:{setup.Arguments[0].Trim().ToLowerInvariant()}";
    }

    private static BehaviorCompilation Failed(string code, string message)
        => new(null, [Error(code, message)]);

    private static BehaviorDiagnostic Error(string code, string message, int line = 1, int column = 1)
        => new(code, BehaviorDiagnosticSeverity.Error, message, line, column);
}
