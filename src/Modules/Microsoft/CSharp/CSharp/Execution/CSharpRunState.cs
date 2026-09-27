namespace DigitalBrain.Microsoft.CSharp;

internal sealed record CSharpRunState(CSharpFileStatus Status, int? ExitCode, DateTimeOffset? StartedAt)
{
    public static readonly CSharpRunState Stopped = new(CSharpFileStatus.Stopped, null, null);
}
