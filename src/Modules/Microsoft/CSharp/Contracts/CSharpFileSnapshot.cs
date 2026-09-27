namespace DigitalBrain.Microsoft.CSharp;

[GenerateSerializer, Alias("microsoft.csharp.file-status")]
public enum CSharpFileStatus { Stopped, Running, Restarting, Exited }

[GenerateSerializer, Alias("microsoft.csharp.file-snapshot")]
public sealed record CSharpFileSnapshot(
    [property: Id(0)] string Id,
    [property: Id(1)] string Source,
    [property: Id(2)] IReadOnlyDictionary<string, string> Settings,
    [property: Id(3)] CSharpFileStatus Status,
    [property: Id(4)] int? ExitCode,
    [property: Id(5)] DateTimeOffset? StartedAt,
    [property: Id(6)] bool ShouldRun,
    [property: Id(7)] int Failures);

