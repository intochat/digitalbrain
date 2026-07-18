using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Lexing;
using DigitalBrain.InoLang.Parsing;

namespace DigitalBrain.InoLang.Tests;

// v3 §5: "an InoLang-scenario adapter that discovers and runs .ino scenarios … one
// test command holds." InoTestRunner (#50) gave us file-level discovery + an
// aggregate report; this projects each scenario into its own xUnit-v3 row so MTP
// (and `dotnet test --filter "DisplayName~..."`) can address scenarios one by one.
//
// Rows carry (relativePath, scenarioName, scenarioKey). The third column is the
// dispatch identifier — `scenario:<index>` for a real scenario, one of the
// `<…>`-bracketed sentinels for L6 synthetic failures. Index-based dispatch
// (rather than name) is what stops two same-named scenarios collapsing into one
// run, and is also what stops a user-authored `scenario "<compile error>"` from
// being misread as the synthetic sentinel.
public static class InoScenarioProjection
{
    public const string CompileErrorScenarioKey = "<compile-error>";
    public const string NoScenariosScenarioKey = "<no-scenarios>";
    public const string MissingRootScenarioKey = "<missing-root>";

    const string ScenarioKeyPrefix = "scenario:";

    static readonly ScenarioRunner SharedRunner = new();

    public static IEnumerable<TheoryDataRow<string, string, string>> Discover(string rootPath)
    {
        var absoluteRoot = Path.GetFullPath(rootPath);

        if (!Directory.Exists(absoluteRoot))
        {
            yield return new TheoryDataRow<string, string, string>(
                string.Empty, "<missing root>", MissingRootScenarioKey)
            {
                Label = $"<missing root>: {absoluteRoot}",
            };
            yield break;
        }

        foreach (var absolute in InoFileDiscovery.Enumerate(absoluteRoot))
        {
            var relative = NormalizePath(Path.GetRelativePath(absoluteRoot, absolute));
            foreach (var row in ProjectFileRows(absolute, relative))
                yield return row;
        }
    }

    public static async Task<ScenarioRunReport> RunAsync(
        string rootPath,
        string relativePath,
        string scenarioName,
        string scenarioKey,
        IContractCatalog catalog,
        CancellationToken ct)
    {
        if (scenarioKey == MissingRootScenarioKey)
            return new ScenarioRunReport(false,
                $"root path does not exist: {Path.GetFullPath(rootPath)}");

        var absolute = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var source = await File.ReadAllTextAsync(absolute, ct).ConfigureAwait(false);
        var compiled = InoCompiler.Compile(source, catalog);

        if (scenarioKey == CompileErrorScenarioKey)
            return compiled.Success
                ? new ScenarioRunReport(false,
                    $"{relativePath}: expected compile errors but the file now compiles cleanly")
                : new ScenarioRunReport(false,
                    $"{relativePath}: {FormatErrors(compiled.Diagnostics)}");

        if (!compiled.Success)
            return new ScenarioRunReport(false,
                $"{relativePath} failed to compile: {FormatErrors(compiled.Diagnostics)}");

        if (scenarioKey == NoScenariosScenarioKey)
            return compiled.Plan!.Scenarios.Count == 0
                ? new ScenarioRunReport(false,
                    $"v3 §L6: {relativePath} has zero scenarios — spec-first refuses to gate it.")
                : new ScenarioRunReport(false,
                    $"{relativePath}: expected zero scenarios but the file now has {compiled.Plan.Scenarios.Count}");

        if (!TryParseScenarioIndex(scenarioKey, out var index))
            return new ScenarioRunReport(false, $"unrecognised scenario key '{scenarioKey}'");

        var plan = compiled.Plan!;
        if (index < 0 || index >= plan.Scenarios.Count)
            return new ScenarioRunReport(false,
                $"scenario index {index} out of range in {relativePath} (has {plan.Scenarios.Count} scenarios)");

        // InoLang's frozen ABI exposes only RunAllAsync, so run all and pick by index.
        var fullReport = await SharedRunner.RunAllAsync(plan, ct).ConfigureAwait(false);
        var result = fullReport.Results[index];

        return result.Passed
            ? new ScenarioRunReport(true, $"{relativePath} :: {result.Name}: passed")
            : new ScenarioRunReport(false,
                $"{relativePath} :: {result.Name}: " + string.Join(" | ", result.Failures));
    }

    static IEnumerable<TheoryDataRow<string, string, string>> ProjectFileRows(
        string absolutePath, string relativePath)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(File.ReadAllText(absolutePath), diagnostics).Lex();
        var document = new Parser(tokens, diagnostics).ParseDocument();

        if (document is null || diagnostics.HasErrors)
        {
            yield return SyntheticRow(relativePath, "<compile error>", CompileErrorScenarioKey);
            yield break;
        }

        if (document.Scenarios.Count == 0)
        {
            yield return SyntheticRow(relativePath, "<no scenarios>", NoScenariosScenarioKey);
            yield break;
        }

        var duplicateNames = document.Scenarios
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < document.Scenarios.Count; i++)
        {
            var scenario = document.Scenarios[i];
            var label = duplicateNames.Contains(scenario.Name)
                ? $"{relativePath} :: {scenario.Name} [#{i}]"
                : $"{relativePath} :: {scenario.Name}";
            yield return new TheoryDataRow<string, string, string>(
                relativePath, scenario.Name, ScenarioKeyPrefix + i)
            {
                Label = label,
            };
        }
    }

    static TheoryDataRow<string, string, string> SyntheticRow(
        string relativePath, string scenarioName, string scenarioKey)
        => new(relativePath, scenarioName, scenarioKey)
        {
            Label = $"{relativePath} :: {scenarioName}",
        };

    static bool TryParseScenarioIndex(string scenarioKey, out int index)
    {
        index = -1;
        return scenarioKey.StartsWith(ScenarioKeyPrefix, StringComparison.Ordinal)
            && int.TryParse(scenarioKey.AsSpan(ScenarioKeyPrefix.Length), out index);
    }

    static string NormalizePath(string relativePath) =>
        relativePath.Replace(Path.DirectorySeparatorChar, '/');

    static string FormatErrors(IReadOnlyList<Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Code} {d.Message}"));
}

public sealed record ScenarioRunReport(bool Passed, string Message);
