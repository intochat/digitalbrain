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
    string Detail)
{
    public static BehaviorInstallTestReport Pass(int scenarioCount, string detail = "passed")
        => new(true, scenarioCount, detail);

    public static BehaviorInstallTestReport Fail(string detail, int scenarioCount = 0)
        => new(false, scenarioCount, detail);
}
