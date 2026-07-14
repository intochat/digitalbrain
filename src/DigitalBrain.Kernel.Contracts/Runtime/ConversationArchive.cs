using System.Security.Cryptography;
using System.Text.Json;
using Orleans;

namespace DigitalBrain.Kernel.Runtime;

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-archive-segment")]
public sealed record ConversationArchiveSegment(
    [property: Id(0)] string SegmentId,
    [property: Id(1)] string ConversationScopeHash,
    [property: Id(2)] string? PreviousSegmentId,
    [property: Id(3)] string? PreviousDigest,
    [property: Id(4)] string Digest,
    [property: Id(5)] long FromSequence,
    [property: Id(6)] long ThroughSequence,
    [property: Id(7)] ConversationTurn[] Turns);

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-archive-state")]
public sealed record ConversationArchiveState([property: Id(0)] int SchemaVersion, [property: Id(1)] long Revision, [property: Id(2)] ConversationArchiveSegment? Segment)
{
    public static ConversationArchiveState Empty() => new(RuntimeStateSchemas.ConversationArchive, 0, null);
}

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-archive-cursor")]
public sealed record ConversationArchiveCursor([property: Id(0)] string SegmentId, [property: Id(1)] string Digest, [property: Id(2)] long BeforeSequence);

[GenerateSerializer, Alias("digitalbrain.runtime.conversation-archive-page")]
public sealed record ConversationArchivePage([property: Id(0)] ConversationTurn[] Turns, [property: Id(1)] ConversationArchiveCursor? NextCursor);

[Alias("digitalbrain.runtime.i-conversation-archive-neuron")]
public interface IConversationArchiveNeuron : IGrainWithStringKey
{
    [Alias("digitalbrain.runtime.conversation-archive.read")]
    Task<ConversationArchiveSegment?> ReadAsync();

    [Alias("digitalbrain.runtime.conversation-archive.put")]
    Task<ConversationArchiveSegment> PutAsync(ConversationArchiveSegment segment);
}

public static class ConversationArchiveTransitions
{
    public const int MaximumPageTurns = 256;
    public const int InlineTurnsAfterCompaction = 96;
    private static readonly JsonSerializerOptions DigestJson = new(JsonSerializerDefaults.Web);

    public static ConversationState Compact(ConversationState state, int maximumInlineTurns)
    {
        if (state.Turns.Length <= maximumInlineTurns) return state;
        if (state.Identity is null) throw new RuntimeStateIntegrityException("conversation archive identity is missing");
        if (InlineTurnsAfterCompaction >= maximumInlineTurns)
            throw new InvalidOperationException("Conversation archive thresholds are invalid.");
        var removed = state.Turns[..(state.Turns.Length - InlineTurnsAfterCompaction)];
        var prior = state.Archive;
        ValidateDescriptor(prior, state.Turns);
        var digest = Digest(prior?.Digest, removed);
        var scopeHash = RuntimeStateKeys.Conversation(state.Identity.OwnerId, state.Identity.ActorId, state.Identity.ConversationId);
        var segmentId = RuntimeStateKeys.ConversationArchiveSegment(scopeHash, prior?.HeadSegmentId, removed[^1].Sequence, digest);
        return state with
        {
            Turns = state.Turns[^InlineTurnsAfterCompaction..],
            Archive = new ConversationArchiveDescriptor(
                (prior?.ArchivedTurnCount ?? 0) + removed.Length,
                removed[^1].Sequence,
                prior?.FirstTurnAt ?? removed[0].CreatedAt,
                removed[^1].CreatedAt,
                digest,
                segmentId)
        };
    }

    public static ConversationArchiveSegment? PrepareSegment(string conversationScopeHash, ConversationState current, ConversationState next)
    {
        RuntimeStateKeys.DemandScopeHash(conversationScopeHash);
        var nextArchive = next.Archive;
        if (nextArchive is null || string.Equals(current.Archive?.HeadSegmentId, nextArchive.HeadSegmentId, StringComparison.Ordinal))
            return null;
        var priorThrough = current.Archive?.ThroughSequence ?? 0;
        var removed = current.Turns.Where(turn => turn.Sequence > priorThrough && turn.Sequence <= nextArchive.ThroughSequence)
            .ToArray();
        var expectedCount = nextArchive.ArchivedTurnCount - (current.Archive?.ArchivedTurnCount ?? 0);
        if (removed.Length == 0 || removed.Length != expectedCount)
            throw new RuntimeStateIntegrityException("conversation archive pointer has no complete source segment");
        var segment = new ConversationArchiveSegment(
            nextArchive.HeadSegmentId,
            conversationScopeHash,
            current.Archive?.HeadSegmentId,
            current.Archive?.Digest,
            nextArchive.Digest,
            removed[0].Sequence,
            removed[^1].Sequence,
            removed.Select(static turn => turn with { }).ToArray());
        ValidateSegment(segment);
        return segment;
    }

    public static async Task<ConversationArchivePage> ReadPageAsync(
        string conversationScopeHash,
        ConversationArchiveDescriptor? archive,
        ConversationArchiveCursor? cursor,
        int maximumTurns,
        Func<string, Task<ConversationArchiveSegment?>> readSegment)
    {
        RuntimeStateKeys.DemandScopeHash(conversationScopeHash);
        ArgumentNullException.ThrowIfNull(readSegment);
        if (maximumTurns is < 1 or > MaximumPageTurns)
            throw new ArgumentOutOfRangeException(nameof(maximumTurns));
        if (archive is null)
        {
            if (cursor is not null) throw new RuntimeStateIntegrityException("conversation archive cursor has no archive");
            return new([], null);
        }
        ValidateDescriptor(archive, []);
        var current = cursor ?? new(archive.HeadSegmentId, archive.Digest, checked(archive.ThroughSequence + 1));
        ValidateCursor(current);
        var newestFirst = new List<ConversationTurn>(maximumTurns);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        ConversationArchiveCursor? nextCursor = current;
        while (newestFirst.Count < maximumTurns && nextCursor is not null)
        {
            current = nextCursor;
            if (!seen.Add(current.SegmentId))
                throw new RuntimeStateIntegrityException("conversation archive chain contains a cycle");
            var segment = await readSegment(current.SegmentId).ConfigureAwait(false)
                          ?? throw new RuntimeStateIntegrityException("conversation archive segment is missing");
            ValidateSegment(segment);
            if (!string.Equals(segment.SegmentId, current.SegmentId, StringComparison.Ordinal) ||
                !string.Equals(segment.Digest, current.Digest, StringComparison.Ordinal) ||
                !string.Equals(segment.ConversationScopeHash, conversationScopeHash, StringComparison.Ordinal))
                throw new RuntimeStateIntegrityException("conversation archive segment binding is invalid");
            var candidates = segment.Turns.Where(turn => turn.Sequence < current.BeforeSequence)
                .OrderByDescending(static turn => turn.Sequence)
                .ToArray();
            var remaining = maximumTurns - newestFirst.Count;
            newestFirst.AddRange(candidates.Take(remaining));
            if (candidates.Length > remaining)
            {
                nextCursor = new(segment.SegmentId, segment.Digest, newestFirst[^1].Sequence);
                break;
            }
            nextCursor = segment.PreviousSegmentId is null
                ? null
                : new ConversationArchiveCursor(segment.PreviousSegmentId, segment.PreviousDigest!, segment.FromSequence);
        }
        return new(newestFirst.OrderBy(static turn => turn.Sequence).ToArray(), nextCursor);
    }

    public static void ValidateState(ConversationArchiveState state)
    {
        if (state.SchemaVersion != RuntimeStateSchemas.ConversationArchive || state.Revision is < 0 or > 1 ||
            state.Revision == 0 && state.Segment is not null || state.Revision == 1 && state.Segment is null)
            throw new RuntimeStateIntegrityException("invalid conversation archive state");
        if (state.Segment is not null) ValidateSegment(state.Segment);
    }

    public static void ValidateDescriptor(ConversationArchiveDescriptor? archive, ConversationTurn[] inlineTurns)
    {
        if (archive is null) return;
        if (archive.ArchivedTurnCount < 1 || archive.ThroughSequence != archive.ArchivedTurnCount || archive.FirstTurnAt == default || archive.LastTurnAt == default ||
            !RuntimeStateKeys.IsScopeHash(archive.Digest) ||
            !RuntimeStateKeys.IsScopeHash(archive.HeadSegmentId) ||
            inlineTurns.Length != 0 && inlineTurns[0].Sequence != checked(archive.ThroughSequence + 1))
            throw new RuntimeStateIntegrityException("invalid conversation archive descriptor");
    }

    public static void ValidateSegment(ConversationArchiveSegment segment)
    {
        if (!RuntimeStateKeys.IsScopeHash(segment.SegmentId) || !RuntimeStateKeys.IsScopeHash(segment.ConversationScopeHash) ||
            segment.PreviousSegmentId is not null && !RuntimeStateKeys.IsScopeHash(segment.PreviousSegmentId) ||
            segment.PreviousDigest is not null && !RuntimeStateKeys.IsScopeHash(segment.PreviousDigest) ||
            (segment.PreviousSegmentId is null) != (segment.PreviousDigest is null) ||
            !RuntimeStateKeys.IsScopeHash(segment.Digest) || segment.Turns is null or { Length: 0 } ||
            segment.FromSequence < 1 || segment.ThroughSequence < segment.FromSequence ||
            segment.Turns[0].Sequence != segment.FromSequence ||
            segment.Turns[^1].Sequence != segment.ThroughSequence)
            throw new RuntimeStateIntegrityException("invalid conversation archive segment");
        for (var index = 1; index < segment.Turns.Length; index++)
        {
            if (segment.Turns[index].Sequence != checked(segment.Turns[index - 1].Sequence + 1))
                throw new RuntimeStateIntegrityException("conversation archive segment is not contiguous");
        }
        var expectedDigest = Digest(segment.PreviousDigest, segment.Turns);
        var expectedId = RuntimeStateKeys.ConversationArchiveSegment(segment.ConversationScopeHash, segment.PreviousSegmentId, segment.ThroughSequence, expectedDigest);
        if (!string.Equals(segment.Digest, expectedDigest, StringComparison.Ordinal) ||
            !string.Equals(segment.SegmentId, expectedId, StringComparison.Ordinal))
            throw new RuntimeStateIntegrityException("conversation archive segment digest is invalid");
    }

    public static bool SameSegment(ConversationArchiveSegment first, ConversationArchiveSegment second) =>
        string.Equals(first.SegmentId, second.SegmentId, StringComparison.Ordinal) &&
        string.Equals(first.ConversationScopeHash, second.ConversationScopeHash, StringComparison.Ordinal) &&
        string.Equals(first.PreviousSegmentId, second.PreviousSegmentId, StringComparison.Ordinal) &&
        string.Equals(first.PreviousDigest, second.PreviousDigest, StringComparison.Ordinal) &&
        string.Equals(first.Digest, second.Digest, StringComparison.Ordinal) &&
        first.FromSequence == second.FromSequence && first.ThroughSequence == second.ThroughSequence &&
        first.Turns.SequenceEqual(second.Turns);

    private static string Digest(string? previousDigest, ConversationTurn[] turns) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new ArchiveDigestPayload(previousDigest, turns), DigestJson)));

    private static void ValidateCursor(ConversationArchiveCursor cursor)
    {
        if (!RuntimeStateKeys.IsScopeHash(cursor.SegmentId) || !RuntimeStateKeys.IsScopeHash(cursor.Digest) || cursor.BeforeSequence < 1)
            throw new ArgumentException("Conversation archive cursor is invalid.", nameof(cursor));
    }

    private sealed record ArchiveDigestPayload(string? PreviousDigest, ConversationTurn[] Turns);
}
