namespace DigitalBrain.Microsoft.CSharp;

// The sandbox host's HTTP shapes; the Sandbox project keeps its own copy because it builds alone.
internal sealed record SandboxRunRequest(string Source, Dictionary<string, string> Environment);

internal sealed record SandboxRunStatus(string Identifier, string Status, int? ExitCode, DateTimeOffset StartedAt);
