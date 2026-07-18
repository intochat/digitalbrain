using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DigitalBrain.FeatureBuilder;

internal static class FeatureScenarioResultReader
{
    private const int MaximumResultFileBytes = 4_194_304;
    private const int MaximumScenarioCount = 128;
    private const int MaximumScenarioIdCharacters = 128;
    private const int MaximumScenarioNameCharacters = 256;
    private const int MaximumSafeFailureCharacters = 1024;
    private static readonly Regex AbsolutePath = new(
        @"(?i)(?<![\p{L}\p{N}_])(?:[a-z]:[\\/]|\\\\|/)[^\s]+",
        RegexOptions.CultureInvariant);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    internal static FeatureScenarioResult Read(string path, int expectedScenarioCount)
    {
        if (!File.Exists(path))
        {
            throw Invalid("The scenario runner did not produce a result file.");
        }
        var length = new FileInfo(path).Length;
        if (length is <= 0 or > MaximumResultFileBytes)
        {
            throw Invalid("The scenario result file exceeded its bound.");
        }
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumResultFileBytes
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        var counters = document.Descendants().SingleOrDefault(static element => element.Name.LocalName == "Counters")
            ?? throw Invalid("The scenario result does not contain counters.");
        var total = Counter(counters, "total");
        var passed = Counter(counters, "passed");
        var failed = Counter(counters, "failed");
        var skipped = Counter(counters, "notExecuted");
        if (total is <= 0 or > MaximumScenarioCount || total != expectedScenarioCount)
        {
            throw Invalid($"The test runner reported {total} tests for {expectedScenarioCount} compiled BDD scenarios.");
        }
        var workspace = Workspace(path);
        var results = document.Descendants()
            .Where(static element => element.Name.LocalName == "UnitTestResult")
            .Select(element => Scenario(element, workspace))
            .OrderBy(static result => result.Name, StringComparer.Ordinal)
            .ThenBy(static result => result.ScenarioId, StringComparer.Ordinal)
            .ToArray();
        if (results.Length != total ||
            results.Count(static result => result.Outcome == FeatureScenarioOutcome.Passed) != passed ||
            results.Count(static result => result.Outcome == FeatureScenarioOutcome.Failed) != failed ||
            results.Count(static result => result.Outcome == FeatureScenarioOutcome.Skipped) != skipped ||
            passed + failed + skipped != total)
        {
            throw Invalid("The scenario result counters do not match the individual scenario results.");
        }
        return new FeatureScenarioResult(total, passed, failed, skipped, results);
    }

    private static FeatureScenarioEvidence Scenario(XElement element, string workspace)
    {
        var scenarioId = SafeText(RequiredAttribute(element, "testId"), MaximumScenarioIdCharacters, workspace);
        var name = SafeText(RequiredAttribute(element, "testName"), MaximumScenarioNameCharacters, workspace);
        var outcome = Outcome(RequiredAttribute(element, "outcome"));
        var safeFailure = outcome == FeatureScenarioOutcome.Failed
            ? Failure(element, workspace)
            : null;
        return new FeatureScenarioEvidence(scenarioId, name, outcome, safeFailure, DurationMilliseconds(element));
    }

    private static string Failure(XElement result, string workspace)
    {
        var message = result.Descendants()
            .Where(static element => element.Name.LocalName == "ErrorInfo")
            .SelectMany(static element => element.Elements())
            .FirstOrDefault(static element => element.Name.LocalName == "Message")
            ?.Value;
        return string.IsNullOrWhiteSpace(message)
            ? "Scenario failed."
            : SafeText(message, MaximumSafeFailureCharacters, workspace);
    }

    private static long DurationMilliseconds(XElement element)
    {
        var value = element.Attribute("duration")?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }
        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration) || duration < TimeSpan.Zero)
        {
            throw Invalid("The scenario result contains an invalid duration.");
        }
        return (long)Math.Ceiling(duration.TotalMilliseconds);
    }

    private static FeatureScenarioOutcome Outcome(string value) => value switch
    {
        "Passed" => FeatureScenarioOutcome.Passed,
        "NotExecuted" => FeatureScenarioOutcome.Skipped,
        _ => FeatureScenarioOutcome.Failed
    };

    private static string RequiredAttribute(XElement element, string name) =>
        element.Attribute(name)?.Value is { Length: > 0 } value
            ? value
            : throw Invalid($"The scenario result is missing attribute '{name}'.");

    private static string SafeText(string value, int maximumCharacters, string workspace)
    {
        var scrubbed = value.Replace(workspace, "[workspace]", StringComparison.OrdinalIgnoreCase);
        scrubbed = scrubbed.Replace(workspace.Replace('\\', '/'), "[workspace]", StringComparison.OrdinalIgnoreCase);
        scrubbed = AbsolutePath.Replace(scrubbed, "[path]");
        scrubbed = Whitespace.Replace(scrubbed, " ").Trim();
        if (scrubbed.Length == 0)
        {
            return "Unavailable";
        }
        return scrubbed.Length <= maximumCharacters ? scrubbed : scrubbed[..maximumCharacters];
    }

    private static string Workspace(string path)
    {
        var resultsDirectory = Directory.GetParent(Path.GetFullPath(path))
            ?? throw Invalid("The scenario result path is invalid.");
        return resultsDirectory.Parent?.Parent?.FullName
            ?? resultsDirectory.FullName;
    }

    private static int Counter(XElement counters, string name) =>
        int.TryParse(counters.Attribute(name)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Invalid($"The scenario result is missing counter '{name}'.");

    private static FeatureBuildException Invalid(string message) =>
        new(FeatureBuildFailure.ScenarioFailed, message);
}
