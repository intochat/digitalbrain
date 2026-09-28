using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Chat;

[Alias("uichat"), Orleans.Metadata.DefaultGrainType(UIVocabulary.ChatType)]
public interface IChat : INeuron
{
    Task Configure(string label);
    Task Post(ChatRole role, string text);
    Task SetDraft(string draft);
    Task Submit();
    [ReadOnly, Alias("read")] Task<ChatState> Read();
}

[GenerateSerializer]
public enum ChatRole { User, Assistant }

[GenerateSerializer, Alias("ui.chat-entry")]
public sealed record ChatEntry([property: Id(0)] string Id, [property: Id(1)] ChatRole Role, [property: Id(2)] string Text);

[GenerateSerializer, Alias("ui.chat-state")]
public sealed class ChatState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public long Revision { get; set; }
    [Id(2)] public string Label { get; set; } = "";
    [Id(3)] public string Draft { get; set; } = "";
    [Id(4)] public IReadOnlyList<ChatEntry> Messages { get; set; } = [];
}
