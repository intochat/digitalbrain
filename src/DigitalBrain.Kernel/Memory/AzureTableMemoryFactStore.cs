using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using DigitalBrain.Kernel.Contracts;
namespace DigitalBrain.Kernel.Memory;

internal sealed class AzureTableMemoryFactStore : IMemoryFactStore
{
    public const string FactsTableName = "memoryfacts";
    private const int MaximumConflictAttempts = 8;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TableClient _table;
    public AzureTableMemoryFactStore(TableClient table)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        if (!string.Equals(_table.Name, FactsTableName, StringComparison.Ordinal))
            throw new ArgumentException($"Memory requires the '{FactsTableName}' table.", nameof(table));
    }
    public string TableName => _table.Name;
    public async Task<IReadOnlyList<MemoryFactSnapshot>> ListAsync(BrainOwnerId ownerId, int maximumCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
        var partition = Partition(ownerId);
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition} and RowKey ne {MemoryValues.CapacityRowKey}");
        var facts = new List<MemoryFactSnapshot>(Math.Min(maximumCount, MemoryService.MaximumFactsPerOwner));
        await foreach (var entity in _table.QueryAsync<MemoryFactEntity>(filter, maxPerPage: Math.Min(maximumCount, 1_000), cancellationToken: cancellationToken))
        {
            facts.Add(ToSnapshot(entity));
            if (facts.Count >= maximumCount)
                break;
        }
        return facts;
    }
    public async Task<MemoryFactSnapshot?> FindAsync(BrainOwnerId ownerId, string factId, CancellationToken cancellationToken = default)
    {
        var response = await _table.GetEntityIfExistsAsync<MemoryFactEntity>(Partition(ownerId), MemoryValues.FactId(factId, nameof(factId)), cancellationToken: cancellationToken);
        return response.HasValue ? ToSnapshot(response.Value!) : null;
    }
    public async Task<MemoryWriteStatus> CreateAsync(BrainOwnerId ownerId, MemoryFactSnapshot fact, int capacity, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        var partition = Partition(ownerId);
        var entity = ToEntity(partition, fact);
        for (var attempt = 0; attempt < MaximumConflictAttempts; attempt++)
        {
            var existing = await FindAsync(ownerId, fact.FactId, cancellationToken);
            if (existing is not null)
                return SameContent(existing, fact) ? MemoryWriteStatus.AlreadyPresent : throw new MemoryConflictException();
            var metadata = await _table.GetEntityIfExistsAsync<MemoryCapacityEntity>(partition, MemoryValues.CapacityRowKey, cancellationToken: cancellationToken);
            var metadataValue = metadata.HasValue ? metadata.Value! : null;
            if (metadataValue is not null && metadataValue.Count >= capacity)
                return MemoryWriteStatus.CapacityReached;
            var counter = metadataValue is not null ? metadataValue with { Count = metadataValue.Count + 1 } : new MemoryCapacityEntity(partition, 1);
            var actions = metadataValue is not null
                ? new[]
                {
                    new TableTransactionAction(TableTransactionActionType.UpdateReplace, counter, metadataValue.ETag),
                    new TableTransactionAction(TableTransactionActionType.Add, entity)
                }
                : new[]
                {
                    new TableTransactionAction(TableTransactionActionType.Add, counter),
                    new TableTransactionAction(TableTransactionActionType.Add, entity)
                };
            try
            {
                await _table.SubmitTransactionAsync(actions, cancellationToken);
                return MemoryWriteStatus.Created;
            }
            catch (RequestFailedException exception) when (exception.Status is 409 or 412)
            {
            }
        }
        throw new MemoryConflictException();
    }
    public async Task<MemoryFactSnapshot> ReplaceAsync(BrainOwnerId ownerId, MemoryFactSnapshot fact, string expectedETag, CancellationToken cancellationToken = default)
    {
        var partition = Partition(ownerId);
        try
        {
            var response = await _table.UpdateEntityAsync(ToEntity(partition, fact), new ETag(MemoryValues.ETag(expectedETag)), TableUpdateMode.Replace, cancellationToken);
            var updatedETag = response.Headers.ETag?.ToString()
                ?? throw new InvalidOperationException("The Memory replacement response did not include an ETag.");
            return fact with { Tags = fact.Tags.ToArray(), ETag = updatedETag };
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            throw new MemoryNotFoundException(fact.FactId);
        }
        catch (RequestFailedException exception) when (exception.Status == 412)
        {
            throw new MemoryConflictException();
        }
    }
    public async Task<bool> DeleteAsync(BrainOwnerId ownerId, string factId, string expectedETag, CancellationToken cancellationToken = default)
    {
        var partition = Partition(ownerId);
        factId = MemoryValues.FactId(factId, nameof(factId));
        expectedETag = MemoryValues.ETag(expectedETag);
        for (var attempt = 0; attempt < MaximumConflictAttempts; attempt++)
        {
            var fact = await _table.GetEntityIfExistsAsync<MemoryFactEntity>(partition, factId, cancellationToken: cancellationToken);
            if (!fact.HasValue)
                return false;
            var factValue = fact.Value!;
            if (!string.Equals(factValue.ETag.ToString(), expectedETag, StringComparison.Ordinal))
                throw new MemoryConflictException();
            var metadata = await _table.GetEntityIfExistsAsync<MemoryCapacityEntity>(partition, MemoryValues.CapacityRowKey, cancellationToken: cancellationToken);
            var metadataValue = metadata.HasValue ? metadata.Value! : null;
            if (metadataValue is null || metadataValue.Count <= 0)
                throw new InvalidOperationException("Memory capacity metadata is inconsistent.");
            var counter = metadataValue with { Count = metadataValue.Count - 1 };
            try
            {
                await _table.SubmitTransactionAsync(
                    [
                        new TableTransactionAction(TableTransactionActionType.UpdateReplace, counter, metadataValue.ETag),
                        new TableTransactionAction(TableTransactionActionType.Delete, factValue, new ETag(expectedETag))
                    ],
                    cancellationToken);
                return true;
            }
            catch (RequestFailedException exception) when (exception.Status is 409 or 412)
            {
                var current = await FindAsync(ownerId, factId, cancellationToken);
                if (current is null)
                    return false;
                if (!string.Equals(current.ETag, expectedETag, StringComparison.Ordinal))
                    throw new MemoryConflictException();
            }
        }
        throw new MemoryConflictException();
    }
    private static string Partition(BrainOwnerId ownerId) =>
        MemoryValues.Key(ownerId.Value, nameof(ownerId));
    private static MemoryFactEntity ToEntity(string partition, MemoryFactSnapshot fact) => new()
    {
        PartitionKey = partition,
        RowKey = MemoryValues.FactId(fact.FactId, nameof(fact)),
        Text = MemoryValues.Text(fact.Text),
        Tags = JsonSerializer.Serialize(MemoryValues.Tags(fact.Tags), JsonOptions),
        SourceActor = MemoryValues.Key(fact.SourceActor.Value, nameof(fact)),
        CreatedAt = fact.CreatedAt,
        UpdatedAt = fact.UpdatedAt
    };
    private static MemoryFactSnapshot ToSnapshot(MemoryFactEntity entity)
    {
        var tags = JsonSerializer.Deserialize<string[]>(entity.Tags, JsonOptions)
            ?? throw new InvalidOperationException("Memory tags are unavailable.");
        return new MemoryFactSnapshot(
            entity.RowKey,
            MemoryValues.Text(entity.Text),
            MemoryValues.Tags(tags),
            new ActorId(entity.SourceActor),
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.ETag.ToString());
    }
    private static bool SameContent(MemoryFactSnapshot left, MemoryFactSnapshot right) =>
        string.Equals(left.Text, right.Text, StringComparison.Ordinal) && left.Tags.SequenceEqual(right.Tags, StringComparer.Ordinal) &&
        left.SourceActor == right.SourceActor;
    private sealed class MemoryFactEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Tags { get; set; } = "[]";
        public string SourceActor { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
    private sealed record MemoryCapacityEntity : ITableEntity
    {
        public MemoryCapacityEntity()
        {
        }
        public MemoryCapacityEntity(string partitionKey, int count)
        {
            PartitionKey = partitionKey;
            RowKey = MemoryValues.CapacityRowKey;
            Count = count;
        }
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public int Count { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
