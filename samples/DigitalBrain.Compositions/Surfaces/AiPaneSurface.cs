using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Surfaces;

public sealed class AiPaneSurface
{
    public const string SceneKey = "ai-pane";
    public const string SceneTitle = "AI";

    public async Task<ChatResponse> RunAsync(
        IDigitalBrain brain,
        string shellName,
        string modelName,
        string prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        cancellationToken.ThrowIfCancellationRequested();

        var shell = brain.Get<IShell>(shellName);
        await shell.Open(new OpenScene(CommandId.New(), SceneKey, SceneTitle));

        var model = brain.Get<ILlama32>(modelName);
        return await model.Respond([new ChatMessage(ChatRole.User, prompt)]);
    }
}
