using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.open-surface")]
public sealed record OpenSurface : Synapse
{
    // Overrides IUIRenderer's own default ("default"): an untargeted fire must still reach the
    // "desk" surface SurfaceBoot opens and the shell watches, even though the renderer serves
    // other capabilities (ChartPoint) that keep the neuron-level default.
    public const string DefaultInstanceName = ISurface.DefaultInstanceName;

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
