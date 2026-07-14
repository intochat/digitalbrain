using DigitalBrain.Kernel.Runtime;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

[GrainType("digitalbrain.runtime.conversation-archive.v1")]
internal sealed class ConversationArchiveNeuron(
    [PersistentState("conversation-archive", RuntimeStateStorageProviders.Conversations)]
    IPersistentState<EncryptedRuntimeStateEnvelope> persistentState,
    EncryptedRuntimeStateProtector protector) : Grain, IConversationArchiveNeuron
{
    private EncryptedPersistentState<ConversationArchiveState>? _state;

    private string SegmentId => this.GetPrimaryKeyString() ?? throw new InvalidOperationException("Conversation archive grains require a string key.");

    private EncryptedPersistentState<ConversationArchiveState> State => _state ??= new(
        persistentState,
        protector,
        SegmentId,
        RuntimeStateKinds.ConversationArchive,
        RuntimeStateSchemas.ConversationArchive,
        ConversationArchiveState.Empty,
        static value => value.Revision,
        ConversationArchiveTransitions.ValidateState);

    public async Task<ConversationArchiveSegment?> ReadAsync()
    {
        var state = await State.ReadAsync();
        if (state.Segment is not null && !string.Equals(state.Segment.SegmentId, SegmentId, StringComparison.Ordinal))
            throw new RuntimeStateIntegrityException("conversation archive grain key is invalid");
        return state.Segment;
    }

    public async Task<ConversationArchiveSegment> PutAsync(ConversationArchiveSegment segment)
    {
        ConversationArchiveTransitions.ValidateSegment(segment);
        if (!string.Equals(segment.SegmentId, SegmentId, StringComparison.Ordinal))
            throw new RuntimeStateIntegrityException("conversation archive segment key is invalid");
        var current = await State.ReadAsync();
        if (current.Segment is not null)
        {
            if (!ConversationArchiveTransitions.SameSegment(current.Segment, segment))
                throw new RuntimeStateIntegrityException("immutable conversation archive segment changed");
            return current.Segment;
        }
        var persisted = await State.UpdateAsync(current.Revision, state => state with { Revision = checked(state.Revision + 1), Segment = segment });
        return persisted.Segment!;
    }
}
