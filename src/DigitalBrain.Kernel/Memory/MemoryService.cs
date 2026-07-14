using System.Text;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Kernel.Contracts;
namespace DigitalBrain.Kernel.Memory;

internal sealed class MemoryService
{
    public const int MaximumFactsPerOwner = 2_000;
    private readonly IMemoryFactStore _store;
    private readonly IMemoryAuditSink _audit;
    public MemoryService(IMemoryFactStore store, IMemoryAuditSink audit)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }
    public async Task<IReadOnlyList<MemoryFact>> RecallAsync(BrainOwnerId ownerId, ActorId actorId, MemoryRecallRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(ownerId, actorId, correlationId);
        var facts = await _store.ListAsync(ownerId, MaximumFactsPerOwner, cancellationToken);
        var queryTokens = Tokens(request.Query);
        var ranked = facts.Select(fact => new RankedFact(
                fact,
                request.Tags.Count(tag => fact.Tags.Contains(tag, StringComparer.Ordinal)),
                queryTokens.Intersect(Tokens(fact.Text), StringComparer.Ordinal).Count()))
            .OrderByDescending(fact => fact.ExactTags)
            .ThenByDescending(fact => fact.TokenOverlap)
            .ThenByDescending(fact => fact.Fact.UpdatedAt)
            .ThenBy(fact => fact.Fact.FactId, StringComparer.Ordinal)
            .Take(request.Limit)
            .Select(fact => new MemoryFact(fact.Fact.FactId, fact.Fact.Text, fact.Fact.Tags, fact.Fact.UpdatedAt))
            .ToArray();
        await Audit(ownerId, actorId, "recall", null, "Succeeded", correlationId, cancellationToken);
        return ranked;
    }
    public async Task<MemoryWriteStatus> RememberAsync(
        BrainOwnerId ownerId,
        ActorId actorId,
        MemoryRememberIntent intent,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        Validate(ownerId, actorId, correlationId);
        var factId = MemoryValues.FactId(intent.FactId, nameof(intent));
        var fact = new MemoryFactSnapshot(factId, intent.Text, intent.Tags.ToArray(), actorId, now, now, string.Empty);
        var status = await _store.CreateAsync(ownerId, fact, MaximumFactsPerOwner, cancellationToken);
        await Audit(ownerId, actorId, "remember", factId, status.ToString(), correlationId, cancellationToken);
        return status;
    }
    public async Task<MemoryFactSnapshot> InspectAsync(BrainOwnerId ownerId, ActorId actorId, string factId, string correlationId, CancellationToken cancellationToken = default)
    {
        Validate(ownerId, actorId, correlationId);
        factId = MemoryValues.FactId(factId, nameof(factId));
        var fact = await _store.FindAsync(ownerId, factId, cancellationToken) ?? throw new MemoryNotFoundException(factId);
        await Audit(ownerId, actorId, "inspect", factId, "Succeeded", correlationId, cancellationToken);
        return fact;
    }
    public async Task<IReadOnlyList<MemoryFactSnapshot>> ExportAsync(BrainOwnerId ownerId, ActorId actorId, string correlationId, CancellationToken cancellationToken = default)
    {
        Validate(ownerId, actorId, correlationId);
        var facts = await _store.ListAsync(ownerId, MaximumFactsPerOwner, cancellationToken);
        await Audit(ownerId, actorId, "export", null, "Succeeded", correlationId, cancellationToken);
        return facts.OrderBy(fact => fact.FactId, StringComparer.Ordinal).ToArray();
    }
    public async Task<MemoryFactSnapshot> CorrectAsync(
        BrainOwnerId ownerId,
        ActorId actorId,
        string factId,
        string text,
        IReadOnlyList<string> tags,
        string expectedETag,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        Validate(ownerId, actorId, correlationId);
        factId = MemoryValues.FactId(factId, nameof(factId));
        expectedETag = MemoryValues.ETag(expectedETag);
        var existing = await _store.FindAsync(ownerId, factId, cancellationToken) ?? throw new MemoryNotFoundException(factId);
        var replacement = existing with { Text = MemoryValues.Text(text), Tags = MemoryValues.Tags(tags), UpdatedAt = now };
        var updated = await _store.ReplaceAsync(ownerId, replacement, expectedETag, cancellationToken);
        await Audit(ownerId, actorId, "correct", factId, "Replaced", correlationId, cancellationToken);
        return updated;
    }
    public async Task<bool> ForgetAsync(BrainOwnerId ownerId, ActorId actorId, string factId, string expectedETag, string correlationId, CancellationToken cancellationToken = default)
    {
        Validate(ownerId, actorId, correlationId);
        factId = MemoryValues.FactId(factId, nameof(factId));
        expectedETag = MemoryValues.ETag(expectedETag);
        var deleted = await _store.DeleteAsync(ownerId, factId, expectedETag, cancellationToken);
        await Audit(ownerId, actorId, "forget", factId, deleted ? "Deleted" : "NotFound", correlationId, cancellationToken);
        return deleted;
    }
    private static HashSet<string> Tokens(string value)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var current = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(char.ToLowerInvariant(character));
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens;
    }
    private static void Validate(BrainOwnerId ownerId, ActorId actorId, string correlationId)
    {
        MemoryValues.Key(ownerId.Value, nameof(ownerId));
        MemoryValues.Key(actorId.Value, nameof(actorId));
        MemoryValues.Key(correlationId, nameof(correlationId));
    }
    private ValueTask Audit(BrainOwnerId ownerId, ActorId actorId, string operation, string? factId, string outcome, string correlationId, CancellationToken cancellationToken) =>
        _audit.WriteAsync(new MemoryAuditRecord(ownerId, actorId, operation, factId, outcome, correlationId), cancellationToken);
    private sealed record RankedFact(MemoryFactSnapshot Fact, int ExactTags, int TokenOverlap);
}
