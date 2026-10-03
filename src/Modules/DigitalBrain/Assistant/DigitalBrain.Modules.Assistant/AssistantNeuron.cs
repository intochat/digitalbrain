using DigitalBrain.AI.Media;
using Microsoft.Extensions.Options;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Button.Signals;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Flutter.TextField.Signals;
using DigitalBrain.Flutter.Select;
using DigitalBrain.Flutter.FileInput;
using DigitalBrain.Flutter.VoiceInput.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Assistant;


// Input commands may arrive while a turn streams its presentation.
[Reentrant, GrainType("apps.assistant")]
internal sealed partial class AssistantNeuron(
    [PersistentState("apps.assistant", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AssistantState> store, IServiceProvider services)
    : Neuron<AssistantState>(store), IAssistant
{
    private string Key => this.GetPrimaryKeyString();
    private ITextField Draft => GrainFactory.GetGrain<ITextField>(UiParts.NameOf(Key, AssistantSurface.DraftPart));
    public Task<AssistantState> Read() => Task.FromResult(Snapshot);
    // An assistant started for a workspace is keyed "{workspace}/applications/assistant".
    private string Workspace => Key.Split("/applications/")[0];
    public async IAsyncEnumerable<string> Run(AssistantRun request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var item in new AssistantTurnExecution(services, GrainFactory).Run(Workspace, request, summary => DefineTurnForRequest(summary, request.Message), ct))
        { yield return item; }
    }

    public Task<AssistantTranscriptionResult> Transcribe(string? base64, CancellationToken ct = default)
        => new AssistantTranscription(GrainFactory.GetGrain<ISpeechRecognizer>(Key + "/voice")).Transcribe(base64, ct);

    public async Task<AgentConversationState> ReadConversation(string threadId, CancellationToken ct = default)
        => await (await Conversation(threadId)).ReadConversation(ct);

    public Task<ModelCatalog> Models() => new AssistantTurnExecution(services, GrainFactory).Models();

    public Task<IAgent> Conversation(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId) || threadId.Length > 200 ||
            threadId.Any(character => char.IsControl(character) || character is '/' or '\\'))
        { throw new ArgumentException("A bounded thread identifier is required.", nameof(threadId)); }
        return Task.FromResult(GrainFactory.GetGrain<IAgent>(AssistantConversations.Key(Workspace, threadId)));
    }

    public Task<AgentDefinition> DefineTurn(string? summary) => DefineTurnForRequest(summary, null);

    private async Task<AgentDefinition> DefineTurnForRequest(string? summary, string? message)
    {
        var appTools = await new AgentToolSelection(GrainFactory).ResolveAsync(Workspace, CancellationToken.None);
        var registered = services.GetServices<IAgentToolFactory>()
            .SelectMany(factory => factory.Create(() => throw new InvalidOperationException("No tool call is active.")))
            .Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var authoringTools = registered.Where(AssistantToolPolicy.IsCSharpTool).ToArray();
        var definition = Snapshot.Definition ?? AssistantDefinition.For(authoringTools, appTools, message, services.GetRequiredService<IOptions<AssistantOptions>>().Value);
        if (Snapshot.Definition is null)
        {
            var native = services.GetService<NativeTools>();
            definition = definition with
            {
                Instructions = definition.Instructions + $"\nThe user's workspace is the ui.workspace neuron \"{Workspace}\".",
                // Optional host modules may be absent. Their default tools must not
                // make an otherwise standalone Assistant unusable. App-declared
                // tools can also come from dynamic sources resolved by the runner.
                Tools = definition.Tools.Where(tool => appTools.Contains(tool) || registered.Contains(tool) || native?.Contains(tool) == true).ToArray(),
            };
        }
        if (!string.IsNullOrEmpty(summary))
        { definition = definition with { Instructions = definition.Instructions + "\nEarlier conversation summary: " + summary }; }
        return definition;
    }

    public async Task Configure(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await Change(state => state with { Definition = definition });
    }

    public async Task Activate()
    {
        await AssistantSurface.Compose(GrainFactory, Key);
        if (!Snapshot.Active) { await Change(state => state with { Active = true }); }
        _published.Clear();
        await InitializePresentation();
    }

    public async Task Answer(string message)
    {
        await Send(message);
    }

    public async Task Hear(byte[] audio, string mimeType)
    {
        await Act("voice", Convert.ToBase64String(audio));
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        foreach (var running in _running.Values) { running.Cancel(); }
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    public async Task HandleUiEvent(Signal signal)
    {
        var prefix = Key + "/";
        switch (signal)
        {
            case TextFieldChanged changed when changed.Name == prefix + AssistantSurface.DraftPart:
                await Act("draft", changed.Value);
                break;
            case SelectChanged changed when changed.Name == prefix + AssistantSurface.ThreadsPart:
                await Act("conversation", changed.Value);
                break;
            case SelectChanged changed when changed.Name == prefix + AssistantSurface.ModelPart:
                await Act("model", changed.Value);
                break;
            case FileCaptured captured when captured.Name == prefix + AssistantSurface.AttachPart:
                await Act("attach", System.Text.Json.JsonSerializer.Serialize(new { name = captured.FileName, content = captured.Content }));
                break;
            case VoiceCaptured captured when captured.Name == prefix + AssistantSurface.VoicePart:
                await Hear(captured.Audio, captured.MimeType);
                break;
            case ButtonClicked clicked when clicked.Name.StartsWith(prefix, StringComparison.Ordinal):
                switch (clicked.Action)
                {
                    case "submit": await Act("submit", (await Draft.Read()).Value); break;
                    case "cancel": await Act("cancel", null); break;
                    case "new-conversation": await Act("new-conversation", null); break;
                    case "prompt:help": await SetDraft("What can you help me with?"); break;
                    case "prompt:data": await SetDraft("Show me my data"); break;
                    case var command when command.StartsWith("command:", StringComparison.Ordinal):
                        using (var document = System.Text.Json.JsonDocument.Parse(command[8..]))
                        {
                            await Act(JsonText(document.RootElement, "action") ?? "", JsonText(document.RootElement, "value"));
                        }
                        break;
                }
                break;
        }
    }

}
