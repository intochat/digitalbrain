namespace DigitalBrain.E2ETesting;

public sealed record E2EOptions
{
    public string[] Args { get; init; } =
    [
        "DigitalBrain:Flutter:Hosting:Kind=Web",
    ];
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(3);
    public IReadOnlyList<string> WaitFor { get; init; } = ["IntoChat", "Flutter"];
}
