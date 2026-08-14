using Brain.Modules.Conversation.Contracts;
using Orleans.Runtime;

namespace Brain.Modules.Conversation;

public sealed class ConversationGrain(
    [PersistentState("conversation", "Default")]
    IPersistentState<ConversationState> state) : Grain, IConversationGrain
{
    private readonly IPersistentState<ConversationState> _state = state;

    public async Task<ConversationSnapshot> AppendAsync(ConversationAppendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Principal)
            || string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("A conversation message requires principal, idempotency key, and text.");
        }

        EnsureIdentity();
        if (_state.State.ProcessedRequests.Add(request.IdempotencyKey))
        {
            _state.State.Messages.Add(new ConversationMessage(
                _state.State.Messages.Count + 1L,
                "user",
                request.Message.Trim(),
                request.Principal));
            await _state.WriteStateAsync();
        }

        return Snapshot();
    }

    public Task<ConversationSnapshot> ReadAsync()
    {
        EnsureIdentity();
        return Task.FromResult(Snapshot());
    }

    private void EnsureIdentity()
    {
        if (_state.State.ConversationId.Length != 0)
        {
            return;
        }

        var key = this.GetPrimaryKeyString();
        _state.State.ConversationId = key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..];
    }

    private ConversationSnapshot Snapshot()
        => new(_state.State.ConversationId, [.. _state.State.Messages]);
}
