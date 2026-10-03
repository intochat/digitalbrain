namespace DigitalBrain.Apps;

internal static class AppScenarioChecks
{
    public static AppTestRun Bind(PackageContent content, AppTestRun run)
    {
        if (content.File(AppDocumentCodec.Path) is null)
        { return run with { Scenarios = run.Scenarios.Select(s => s with { ScenarioId = null }).ToArray() }; }
        var read = AppDocumentCodec.Read(content);
        if (read.Document is not { } document)
        { return run with { Scenarios = [.. run.Scenarios, new("Authoring document", false, read.Error ?? "Invalid authoring document.")] }; }
        var expected = document.Scenarios.Where(s => !s.IsLive).ToArray();
        var verdicts = new List<AppScenarioVerdict>();
        foreach (var scenario in expected)
        {
            var matches = run.Scenarios.Where(v => v.ScenarioId is { } id ? id == scenario.Id : v.Name == scenario.Name).ToArray();
            verdicts.Add(matches.Length == 1
                ? matches[0] with { Name = scenario.Name, ScenarioId = scenario.Id }
                : new(scenario.Name, false, matches.Length == 0 ? "The tests did not report this scenario." : "The tests reported this scenario more than once.", scenario.Id));
        }
        foreach (var verdict in run.Scenarios)
        {
            if (!expected.Any(s => verdict.ScenarioId is { } id ? s.Id == id : s.Name == verdict.Name))
            { verdicts.Add(verdict with { Passed = false, Message = "The tests reported an unknown or live-only scenario." }); }
        }
        return run with { Scenarios = verdicts.ToArray() };
    }
}
