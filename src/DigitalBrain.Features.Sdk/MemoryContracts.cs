using System.Collections.ObjectModel;

namespace DigitalBrain.Features.Sdk;

public interface IMemoryRecall
{
    Task<IReadOnlyList<MemoryFact>> RecallAsync(MemoryRecallRequest request, CancellationToken cancellationToken = default);
}

public interface IMemoryRemember
{
    void Remember(MemoryRememberIntent intent);
}

public sealed class MemoryRecallRequest
{
    public MemoryRecallRequest(string query, IReadOnlyList<string> tags, int limit = 20)
    {
        Query = FeatureContractGuard.Utf8(query, nameof(query), 2_048);
        Tags = FeatureContractGuard.Tags(tags, nameof(tags));
        if (limit is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 20.");
        }

        Limit = limit;
    }

    public string Query { get; }
    public IReadOnlyList<string> Tags { get; }
    public int Limit { get; }
}

public sealed class MemoryFact
{
    public MemoryFact(string factId, string text, IReadOnlyList<string> tags, DateTimeOffset updatedAt)
    {
        FactId = FeatureContractGuard.MemoryFactId(factId, nameof(factId));
        Text = FeatureContractGuard.Utf8(text, nameof(text), 2_048);
        Tags = FeatureContractGuard.Tags(tags, nameof(tags));
        UpdatedAt = updatedAt;
    }

    public string FactId { get; }
    public string Text { get; }
    public IReadOnlyList<string> Tags { get; }
    public DateTimeOffset UpdatedAt { get; }
}

public sealed class MemoryRememberIntent
{
    public MemoryRememberIntent(string logicalOperationKey, string factId, string text, IReadOnlyList<string> tags)
    {
        LogicalOperationKey = FeatureContractGuard.Required(logicalOperationKey, nameof(logicalOperationKey), 256);
        FactId = FeatureContractGuard.MemoryFactId(factId, nameof(factId));
        Text = FeatureContractGuard.Utf8(text, nameof(text), 2_048);
        Tags = FeatureContractGuard.Tags(tags, nameof(tags));
    }

    public string LogicalOperationKey { get; }
    public string FactId { get; }
    public string Text { get; }
    public IReadOnlyList<string> Tags { get; }
}
