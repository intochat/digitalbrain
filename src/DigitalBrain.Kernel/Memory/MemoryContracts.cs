using DigitalBrain.Features.Sdk;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Memory;

public enum MemoryWriteStatus
{
    Created,
    AlreadyPresent,
    CapacityReached
}

public sealed record MemoryFactSnapshot(
    string FactId,
    string Text,
    IReadOnlyList<string> Tags,
    ActorId SourceActor,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ETag);

public sealed record MemoryAuditRecord(
    BrainOwnerId OwnerId,
    ActorId ActorId,
    string Operation,
    string? FactId,
    string Outcome,
    string CorrelationId);

public interface IMemoryAuditSink
{
    ValueTask WriteAsync(MemoryAuditRecord record, CancellationToken cancellationToken = default);
}

public interface IMemoryFactStore
{
    Task<IReadOnlyList<MemoryFactSnapshot>> ListAsync(
        BrainOwnerId ownerId,
        int maximumCount,
        CancellationToken cancellationToken = default);

    Task<MemoryFactSnapshot?> FindAsync(
        BrainOwnerId ownerId,
        string factId,
        CancellationToken cancellationToken = default);

    Task<MemoryWriteStatus> CreateAsync(
        BrainOwnerId ownerId,
        MemoryFactSnapshot fact,
        int capacity,
        CancellationToken cancellationToken = default);

    Task<MemoryFactSnapshot> ReplaceAsync(
        BrainOwnerId ownerId,
        MemoryFactSnapshot fact,
        string expectedETag,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        BrainOwnerId ownerId,
        string factId,
        string expectedETag,
        CancellationToken cancellationToken = default);
}

public sealed class MemoryConflictException : InvalidOperationException
{
    public MemoryConflictException()
        : base("The Memory fact changed before the operation completed.")
    {
    }
}

public sealed class MemoryNotFoundException : KeyNotFoundException
{
    public MemoryNotFoundException(string factId)
        : base($"Memory fact '{factId}' was not found.")
    {
    }
}

internal static class MemoryValues
{
    internal const string CapacityRowKey = "!capacity";

    internal static string Key(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 256 || value.Any(character =>
                char.IsControl(character) || character is '/' or '\\' or '#' or '?') ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded Azure Table key is required.", parameterName);
        return value;
    }

    internal static string ETag(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 256 || value.Any(char.IsControl) || string.Equals(value, "*", StringComparison.Ordinal))
            throw new ArgumentException("A bounded ETag is required.", nameof(value));
        return value;
    }

    internal static string FactId(string value, string parameterName)
    {
        value = Key(value, parameterName);
        if (string.Equals(value, CapacityRowKey, StringComparison.Ordinal))
            throw new ArgumentException("A reserved Memory fact identifier cannot be used.", parameterName);
        return value;
    }

    internal static IReadOnlyList<string> Tags(IReadOnlyList<string> tags) =>
        new MemoryFact("validation", "validation", tags, DateTimeOffset.UnixEpoch).Tags;

    internal static string Text(string text) =>
        new MemoryFact("validation", text, [], DateTimeOffset.UnixEpoch).Text;
}
