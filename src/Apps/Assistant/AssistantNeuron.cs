using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Media;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Discovery.Agents;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Chat;
using DigitalBrain.Flutter.Chat.Signals;
using DigitalBrain.Flutter.VoiceInput;
using DigitalBrain.Flutter.VoiceInput.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Apps.Assistant;

[Alias("apps.assistant"), Orleans.Metadata.DefaultGrainType("apps.assistant")]
public interface IAssistant : INeuron
{
    Task Activate();
    // One-way, so the chat or voice input that announced the request is free to take the reply.
    [OneWay] Task Answer(string message);
    [OneWay] Task Hear(byte[] audio, string mimeType);
}

[GenerateSerializer, Alias("apps.assistant-state")]
public sealed record AssistantState
{
    [Id(0)] public bool Active { get; init; }
}

[GenerateSerializer, Alias("apps.assistant-activated")]
public sealed record AssistantActivated([property: Id(0)] string Key) : Signal;

// Reentrant because the chat it watches announces every post it makes while an answer is in flight.
[Reentrant, GrainType("apps.assistant")]
internal sealed class AssistantNeuron(
    [PersistentState("apps.assistant", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AssistantState> store)
    : Neuron<AssistantState>(store), IAssistant, INeuronObserver
{
    private static readonly string Instructions = ReadInstructions();
    private IGrainTimer? _renewal;

    private string Key => this.GetPrimaryKeyString();
    private IChat Chat => GrainFactory.GetGrain<IChat>(UiComposer.NameOf(Key, AssistantApp.ChatPart));
    private IVoiceInput Voice => GrainFactory.GetGrain<IVoiceInput>(UiComposer.NameOf(Key, AssistantApp.VoicePart));
    // An assistant started for a workspace is keyed "{workspace}/applications/assistant".
    private string Workspace => Key.Split("/applications/")[0];
    private AgentDefinition Agent => new()
    {
        DisplayName = "Assistant",
        Instructions = Instructions + $"\nThe user's workspace is the ui.workspace neuron \"{Workspace}\".",
        Tools = [CapabilityTools.FindTool],
        ContextProviders = [CapabilityTools.ProviderName],
    };
    private IAgent Conversation => GrainFactory.GetGrain<IAgent>(UiComposer.NameOf(Key, "agent"));

    public async Task Activate()
    {
        await Conversation.Configure(Agent, (await Conversation.GetState()).Revision);
        if (!Snapshot.Active) { await Save(Snapshot with { Active = true }, new AssistantActivated(Key)); }
        await Subscribe();
    }

    public async Task Answer(string message)
    {
        await Chat.Post(ChatRole.User, message);
        await Reply(message);
    }

    public async Task Hear(byte[] audio, string mimeType)
    {
        string transcript;
        try
        {
            var recognized = await GrainFactory.GetGrain<ISpeechRecognizer>(UiComposer.NameOf(Key, "speech"))
                .Recognize(new(new MediaPayload(audio, mimeType), "voice" + Extension(mimeType)));
            transcript = recognized.Text.Trim();
        }
        catch (Exception error) when (error is NotSupportedException or ArgumentException or InvalidOperationException)
        {
            await Chat.Post(ChatRole.Assistant, "I could not understand the recording: " + error.Message);
            return;
        }
        if (transcript.Length == 0) { await Chat.Post(ChatRole.Assistant, "I did not hear anything."); return; }
        await Answer(transcript);
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (Snapshot.Active) { await Subscribe(); }
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _renewal?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    Task INeuronObserver.OnSignalAsync(Signal signal)
    {
        var self = this.AsReference<IAssistant>();
        return signal switch
        {
            ChatSubmitted submitted when submitted.Name == Chat.GetPrimaryKeyString() => self.Answer(submitted.Text),
            VoiceCaptured captured when captured.Name == Voice.GetPrimaryKeyString() => self.Hear(captured.Audio, captured.MimeType),
            _ => Task.CompletedTask,
        };
    }

    private async Task Reply(string message)
    {
        string reply;
        try { reply = (await Conversation.GetRichResponse(message)).Text; }
        catch (Exception error) when (error is not OperationCanceledException)
        { reply = "The assistant failed to answer: " + error.Message; }
        await Chat.Post(ChatRole.Assistant, string.IsNullOrWhiteSpace(reply) ? "(no answer)" : reply);
    }

    private async Task Subscribe()
    {
        var observer = this.AsReference<INeuronObserver>();
        await WatchInputs(observer);
        if (_renewal is not null) { return; }
        var interval = ObserverRenewal;
        _renewal = this.RegisterGrainTimer((_, _) => WatchInputs(observer), 0,
            new GrainTimerCreationOptions { DueTime = interval, Period = interval, Interleave = true, KeepAlive = true });
    }

    private Task WatchInputs(INeuronObserver observer) => Task.WhenAll(Chat.Watch(observer), Voice.Watch(observer));

    private static string ReadInstructions()
    {
        using var stream = typeof(AssistantNeuron).Assembly.GetManifestResourceStream("instructions.md")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Extension(string mimeType) => mimeType.Split(';')[0].Trim() switch
    {
        "audio/webm" => ".webm",
        "audio/ogg" => ".ogg",
        "audio/wav" or "audio/x-wav" => ".wav",
        "audio/mpeg" => ".mp3",
        "audio/mp4" => ".m4a",
        _ => ".bin",
    };
}
