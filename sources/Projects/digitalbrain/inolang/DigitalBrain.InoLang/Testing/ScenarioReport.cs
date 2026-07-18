namespace DigitalBrain.InoLang.Testing;

public sealed record ScenarioResult(string Name, IReadOnlyList<string> Failures)
{
    public bool Passed => Failures.Count == 0;
}

public sealed record ScenarioReport(IReadOnlyList<ScenarioResult> Results)
{
    public bool AllPassed => Results.Count > 0 && Results.All(r => r.Passed);
}
