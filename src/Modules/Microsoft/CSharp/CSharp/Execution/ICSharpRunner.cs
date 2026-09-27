namespace DigitalBrain.Microsoft.CSharp;

internal interface ICSharpRunner
{
    Task StartAsync(string fileId, string source, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken);
    Task StopAsync(string fileId, CancellationToken cancellationToken);
    Task<CSharpContainerState> InspectAsync(string fileId, CancellationToken cancellationToken);
    Task<string> LogsAsync(string fileId, int tail, CancellationToken cancellationToken);
}

internal sealed record CSharpContainerState(CSharpFileStatus Status, int? ExitCode, DateTimeOffset? StartedAt)
{
    public static readonly CSharpContainerState Missing = new(CSharpFileStatus.Stopped, null, null);
}
