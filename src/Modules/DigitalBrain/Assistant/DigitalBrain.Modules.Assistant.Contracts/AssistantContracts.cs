using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Chat;
using Orleans.Concurrency;

namespace DigitalBrain.Assistant;

[Alias("apps.assistant"), Orleans.Metadata.DefaultGrainType("apps.assistant")]
public interface IAssistant : INeuron, IUiEventHandler
{
    Task Activate();
    [ReadOnly] Task<AssistantState> Read();
    Task<AssistantWindow> OpenWindow(string? draft = null, string? title = null);
    Task SetDraft(string draft);
    [OneWay] Task Act(string action, string? value);
    Task<AssistantTranscriptionResult> Transcribe(string? base64, CancellationToken ct = default);
    [AlwaysInterleave, ResponseTimeout("00:10:00")] IAsyncEnumerable<string> Run(AssistantRun request, CancellationToken ct = default);
    Task<AgentConversationState> ReadConversation(string threadId, CancellationToken ct = default);
    Task<ModelCatalog> Models();
    Task Configure(AgentDefinition definition);
    Task<IAgent> Conversation(string threadId);
    Task<AgentDefinition> DefineTurn(string? summary);
    // One-way, so the chat or voice input that announced the request is free to take the reply.
    [OneWay] Task Answer(string message);
    [OneWay] Task Hear(byte[] audio, string mimeType);
}

[GenerateSerializer, Alias("apps.assistant-state")]
public sealed record AssistantState
{
    [Id(0)] public bool Active { get; init; }
    [Id(1)] public AgentDefinition? Definition { get; init; }
    [Id(2)] public string SelectedThread { get; init; } = "";
    [Id(3)] public IReadOnlyList<AssistantThread> Threads { get; init; } = [];
    [Id(4)] public string Owner { get; init; } = "";
    // [Id(5)] retired (LegacyRestored); never reuse
}

[GenerateSerializer, Alias("apps.assistant-configured")]
public sealed record AssistantConfigured([property: Id(0)] string Key) : Signal;

[GenerateSerializer, Alias("assistant.thread")]
public sealed record AssistantThread
{
    [Id(0)] public required string Id { get; init; }
    [Id(1)] public string Title { get; init; } = "New conversation";
    [Id(2)] public string Draft { get; init; } = "";
    [Id(3)] public string? ModelProfile { get; init; }
    [Id(4)] public IReadOnlyList<ChatEntry> Messages { get; init; } = [];
    [Id(5)] public IReadOnlyList<string> Receipts { get; init; } = [];
    [Id(6)] public IReadOnlyList<string> Results { get; init; } = [];
    [Id(7)] public string? Error { get; init; }
    [Id(8)] public string? Status { get; init; }
    [Id(9)] public string? TurnId { get; init; }
}

[GenerateSerializer, Alias("assistant.window")]
public sealed record AssistantWindow(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] UiChildRef Surface);

[GenerateSerializer, Alias("assistant.run")]
public sealed record AssistantRun(
    [property: Id(0)] string ThreadId,
    [property: Id(1)] string RunId,
    [property: Id(2)] string Message,
    [property: Id(3)] string Owner,
    [property: Id(4)] string? ModelProfile = null);

[GenerateSerializer, Alias("assistant.transcription-result")]
public sealed record AssistantTranscriptionResult(
    [property: Id(0)] int Status,
    [property: Id(1)] string? Text = null,
    [property: Id(2)] string? Error = null);
