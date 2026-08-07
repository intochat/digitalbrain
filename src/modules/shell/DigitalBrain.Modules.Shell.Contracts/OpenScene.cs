using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Shell;

[GenerateSerializer]
[Alias("flutter.open-scene")]
[Description("Open a scene on the shell")]
public sealed record OpenScene : Synapse
{
    public OpenScene(CommandId commandId, string sceneKey, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        CommandId = commandId;
        SceneKey = sceneKey;
        Title = title;
    }

    [Id(0)]
    public CommandId CommandId { get; init; }

    [Id(1)]
    public string SceneKey { get; init; }

    [Id(2)]
    public string Title { get; init; }
}
