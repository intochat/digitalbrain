using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter.Chat.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Chat;

[GrainType(UIVocabulary.ChatType)]
internal sealed class ChatNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ChatState> store)
    : Neuron<ChatState>(store), IChat
{
    private const int MaxTextLength = 32_000;
    private const int MaxMessages = 1_000;

    public Task Configure(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var next = Next();
        next.Label = label.Trim();
        return Save(next, Changed(next));
    }

    public Task Post(ChatRole role, string text)
    {
        if (!Enum.IsDefined(role)) { throw new ArgumentOutOfRangeException(nameof(role)); }
        ValidateText(text);
        var next = Next();
        next.Messages = [.. next.Messages.TakeLast(MaxMessages - 1), new ChatEntry(Guid.NewGuid().ToString("N"), role, text)];
        return Save(next, Changed(next));
    }

    public Task SetDraft(string draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Length > MaxTextLength) { throw new ArgumentException($"A draft has at most {MaxTextLength} characters.", nameof(draft)); }
        var next = Next();
        next.Draft = draft;
        return Save(next, Changed(next));
    }

    public Task Submit()
    {
        var text = Snapshot.Draft.Trim();
        if (text.Length == 0) { throw new InvalidOperationException("There is no draft to submit."); }
        var next = Next();
        next.Draft = "";
        return Save(next, Changed(next), new ChatSubmitted(next.Name, text));
    }

    [ReadOnly] public Task<ChatState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }

    private ChatState Next()
    {
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Revision++;
        return next;
    }

    private static ChatChanged Changed(ChatState state) => new(state.Name, state.Revision);

    private static void ValidateText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > MaxTextLength) { throw new ArgumentException($"A message has at most {MaxTextLength} characters.", nameof(text)); }
    }
}
