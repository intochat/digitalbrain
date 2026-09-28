namespace DigitalBrain.Microsoft.CSharp.Sandbox;

// The HTTP vocabulary shared with the brain's runner, which keeps its own copy of these shapes.
internal sealed record RunRequest(string Source, Dictionary<string, string>? Environment);

internal sealed record RunStatus(string Identifier, string Status, int? ExitCode, DateTimeOffset StartedAt);

internal static class RunStatuses
{
    public const string Running = "Running";
    public const string Exited = "Exited";
}
