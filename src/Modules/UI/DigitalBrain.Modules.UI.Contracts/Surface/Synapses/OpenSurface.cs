using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.open-surface")]
public sealed record OpenSurface : Synapse
{
    public OpenSurface(CommandId commandId, string surfaceKey, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        CommandId = commandId;
        SurfaceKey = surfaceKey;
        Title = title;
    }

    [Id(0)]
    public CommandId CommandId { get; init; }

    [Id(1)]
    public string SurfaceKey { get; init; }

    [Id(2)]
    public string Title { get; init; }
}
