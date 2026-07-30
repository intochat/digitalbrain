namespace DigitalBrain.Behaviors;

public interface IBehaviorInstallTests
{
    ValueTask<BehaviorInstallTestReport> RunAsync(
        IBehaviorContext context,
        IReadOnlyDictionary<string, string> features,
        CancellationToken cancellationToken);
}

public sealed record BehaviorInstallTestReport(
    bool Passed,
    int ScenarioCount,
    string Detail,
    IReadOnlyList<BehaviorScenarioResult> Results)
{
    public static BehaviorInstallTestReport Fail(string detail, int scenarioCount = 0)
        => new(false, scenarioCount, detail, []);

    public static BehaviorInstallTestReport FromResults(IReadOnlyList<BehaviorScenarioResult> results, string detail = "results")
    {
        ArgumentNullException.ThrowIfNull(results);
        var passed = results.Count > 0 && results.All(static result => result.Passed);
        return new(passed, results.Count, detail, results);
    }
}
