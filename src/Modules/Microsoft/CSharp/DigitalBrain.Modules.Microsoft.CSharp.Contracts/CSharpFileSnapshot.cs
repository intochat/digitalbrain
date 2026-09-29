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
    [property: Id(7)] int Failures,
    [property: Id(8)] CSharpTrigger? Trigger)
{
    [Id(9)] public IReadOnlyList<CSharpSubscriptionView> Subscriptions { get; init; } = [];
}

[GenerateSerializer, Alias("microsoft.csharp.subscription-view")]
public sealed record CSharpSubscriptionView(
    [property: Id(0)] string Neuron,
    [property: Id(1)] string Signal,
    [property: Id(2)] int Pending);

