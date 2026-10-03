using System.Text.Json;
using DigitalBrain.Microsoft.CSharp;

namespace DigitalBrain.Apps;

// How a revision's tests.cs is executed. The default runs it as an ordinary sandbox script; a host
// or test replaces the service to run tests elsewhere.
public interface ITestScriptRunner
{
    Task<AppTestRun> RunAsync(PackageRevisionRef revision, string tests, CancellationToken cancellationToken);
}

internal sealed class CSharpFileTestRunner(IGrainFactory grains, TimeProvider clock) : ITestScriptRunner
{
    private static readonly TimeSpan RunDeadline = TimeSpan.FromMinutes(50);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private const int LogTail = 4000;
    private const string PassLine = "dbtest:pass ";
    private const string FailLine = "dbtest:fail ";

    public async Task<AppTestRun> RunAsync(PackageRevisionRef revision, string tests, CancellationToken cancellationToken)
    {
        // The tests run as an ordinary sandbox script: same token, edge and contract limits as any
        // app script. The script creates its own scratch installations from the revision handed here.
        var file = grains.GetGrain<ICSharpFile>($"specs/{revision.Package}@{revision.Revision}/{Guid.NewGuid():N}");
        try
        {
            await file.Write(tests, cancellationToken);
            var settings = new Dictionary<string, string>
            {
                ["Package"] = revision.Package.ToString(),
                ["Revision"] = revision.Revision,
            };
            if (DigitalBrain.Kernel.Enforcement.CallerContextStamper.TryGet(out _))
            { settings["BrainScope"] = DigitalBrain.Kernel.Enforcement.BrainScope.CurrentId(); }
            await file.Configure(settings, cancellationToken);
            var snapshot = await file.Start(cancellationToken);
            var deadline = clock.GetUtcNow() + RunDeadline;
            while (snapshot.ExitCode is null && snapshot.Status is CSharpFileStatus.Running or CSharpFileStatus.Restarting)
            {
                if (clock.GetUtcNow() > deadline) { break; }
                await Task.Delay(PollInterval, cancellationToken);
                snapshot = await file.Read(cancellationToken);
            }
            return new AppTestRun(ParseVerdicts(await file.ReadLogs(LogTail, cancellationToken)), snapshot.ExitCode);
        }
        finally
        {
            try { await file.Delete(CancellationToken.None); }
            catch (Exception error) when (error is not OperationCanceledException) { }
        }
    }

    internal static AppScenarioVerdict[] ParseVerdicts(string logs)
    {
        var verdicts = new List<AppScenarioVerdict>();
        foreach (var raw in logs.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("dbtest:json ", StringComparison.Ordinal))
            {
                try
                {
                    var verdict = JsonSerializer.Deserialize<AppScenarioVerdict>(line[12..], new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (verdict is null || string.IsNullOrWhiteSpace(verdict.Name) || string.IsNullOrWhiteSpace(verdict.ScenarioId))
                    { throw new JsonException("A scenario report needs its ID and name."); }
                    verdicts.Add(verdict);
                }
                catch (JsonException)
                { verdicts.Add(new("Invalid scenario report", false, "The tests emitted an invalid JSON scenario report.")); }
            }
            else if (line.StartsWith(PassLine, StringComparison.Ordinal))
            {
                verdicts.Add(new(line[PassLine.Length..].Trim(), true, ""));
            }
            else if (line.StartsWith(FailLine, StringComparison.Ordinal))
            {
                var body = line[FailLine.Length..];
                var tab = body.IndexOf('\t', StringComparison.Ordinal);
                verdicts.Add(tab < 0 ? new(body.Trim(), false, "") : new(body[..tab].Trim(), false, body[(tab + 1)..].Trim()));
            }
        }
        return [.. verdicts];
    }
}
