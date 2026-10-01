using System.Collections.Concurrent;
using DigitalBrain.Apps;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

// Stands in for the sandbox in draft tests: a tests file whose source contains a scripted marker
// "runs" instantly with that marker's verdicts.
internal sealed class ScriptedTestRunner : ITestScriptRunner
{
    public ConcurrentDictionary<string, (int ExitCode, string Logs)> BySourceMarker { get; } = new(StringComparer.Ordinal);

    public Task<AppTestRun> RunAsync(PackageRevisionRef revision, string tests, CancellationToken cancellationToken)
    {
        var scripted = BySourceMarker.FirstOrDefault(pair => tests.Contains(pair.Key, StringComparison.Ordinal));
        return Task.FromResult(scripted.Key is { Length: > 0 }
            ? new AppTestRun(CSharpFileTestRunnerVerdicts(scripted.Value.Logs), scripted.Value.ExitCode)
            : new AppTestRun([], null));
    }

    // The same line protocol the real runner parses.
    private static AppScenarioVerdict[] CSharpFileTestRunnerVerdicts(string logs)
    {
        var verdicts = new List<AppScenarioVerdict>();
        foreach (var raw in logs.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("dbtest:pass ", StringComparison.Ordinal)) { verdicts.Add(new(line[12..].Trim(), true, "")); }
            else if (line.StartsWith("dbtest:fail ", StringComparison.Ordinal))
            {
                var body = line[12..];
                var tab = body.IndexOf('\t', StringComparison.Ordinal);
                verdicts.Add(tab < 0 ? new(body.Trim(), false, "") : new(body[..tab].Trim(), false, body[(tab + 1)..].Trim()));
            }
        }
        return [.. verdicts];
    }
}


