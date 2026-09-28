namespace DigitalBrain.Microsoft.CSharp;

internal sealed record CSharpRun(string Owner, string RunId, string Source, IReadOnlyDictionary<string, string> Environment);

// Where runs execute: the development sandbox through Aspire, or a production session per owner.
internal interface ICSharpRunner
{
    Task StartAsync(CSharpRun run, CancellationToken cancellationToken);

    Task StopAsync(string owner, string runId, CancellationToken cancellationToken);

    Task<CSharpRunState> InspectAsync(string owner, string runId, CancellationToken cancellationToken);

    Task<string> LogsAsync(string owner, string runId, int tail, CancellationToken cancellationToken);
}
