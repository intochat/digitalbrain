using DigitalBrain.AI.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DigitalBrain.AI;
using DigitalBrain.Compute;
using DigitalBrain.Discovery;
using DigitalBrain.Flutter;
using DigitalBrain.Qdrant;

namespace DigitalBrain.Apps.Assistant;

public sealed class AssistantApp : IApplication
{
    public static string Key(string workspace) => workspace + "/applications/assistant";

    public const string ThreadsPart = "threads";
    public const string ModelPart = "model";
    public const string MessagesPart = "messages";
    public const string DraftPart = "draft";
    public const string VoicePart = "voice";
    public const string AttachPart = "attachments";
    public const string SendPart = "send";
    public const string StopPart = "stop";
    public const string NewPart = "new-conversation";
    public const string StatusPart = "status";
    public const string ResultsPart = "results";
    public const string ReceiptsPart = "receipts";

    public void Configure(IAppBuilder app) => app
        .ConfigureServices(services => services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolFactory, WorkspaceFormTools>()))
        .RequireModule<AppsModule>()
        .RequireModule<AIModule>()
        .RequireModule<ComputeModule>()
        .RequireModule<FlutterModule>()
        .RequireModule<QdrantModule>()
        .RequireModule<DiscoveryModule>()
        .Ui(ui => ui.Surface("Assistant",
            ui.Layout("main", "column", [64, 0, 40, 32, 100, 48],
                ui.Layout("toolbar", "row", [0, 100, 0],
                    ui.Select(ThreadsPart, "Conversation").OnEvent<IAssistant>(),
                    ui.Button(NewPart, "New", "new-conversation").OnEvent<IAssistant>(),
                    ui.Select(ModelPart, "Model").OnEvent<IAssistant>()),
                ui.List("content", followEnd: true,
                    ui.Layout(MessagesPart, "list"),
                    ui.Layout(ResultsPart, "list"),
                    ui.Layout(ReceiptsPart, "list")),
                ui.Layout("prompts", "row",
                    ui.Button("help", "What can you help me with?", "prompt:help").OnEvent<IAssistant>(),
                    ui.Button("data", "Show me my data", "prompt:data").OnEvent<IAssistant>()),
                ui.Text(StatusPart, ""),
                ui.TextField(DraftPart, "Message", "multiline", submitButton: SendPart).OnEvent<IAssistant>(),
                ui.Layout("actions", "row",
                    ui.FileInput(AttachPart, "Attach file").OnEvent<IAssistant>(),
                    ui.VoiceInput(VoicePart, "Hold to talk").OnEvent<IAssistant>(),
                    ui.Button(SendPart, "Send", "submit").OnEvent<IAssistant>(),
                    ui.Button(StopPart, "Stop", "cancel").OnEvent<IAssistant>()))))
        .OnStart(start => start.Grains.GetGrain<IAssistant>(start.Key).Activate());
}
