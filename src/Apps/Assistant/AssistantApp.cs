using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Discovery;
using DigitalBrain.Flutter;
using DigitalBrain.Qdrant;

namespace DigitalBrain.Apps.Assistant;

public sealed class AssistantApp : IApplication
{
    public const string ChatPart = "chat";
    public const string VoicePart = "voice";

    public void Configure(IAppBuilder app) => app
        .RequireModule<AIModule>()
        .RequireModule<FlutterModule>()
        .RequireModule<QdrantModule>()
        .RequireModule<DiscoveryModule>()
        .Ui(ui => ui.Surface("Assistant", ui.Column(
            ui.Chat(ChatPart, "Message the assistant"),
            ui.VoiceInput(VoicePart, "Hold to talk"))))
        .OnStart(start => start.Grains.GetGrain<IAssistant>(start.Key).Activate());
}
