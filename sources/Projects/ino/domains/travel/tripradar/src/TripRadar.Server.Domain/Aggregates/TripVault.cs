using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Aggregates;

public class TripVault : AggregateRoot<long>
{
    private TripVault()
    {
    }

    public TripVault(
        long ownerId,
        string name,
        string? description = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        if (ownerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ownerId), "Owner id must be greater than 0.");
        }

        UniqueId = Guid.NewGuid();
        OwnerId = ownerId;
        ApplyDetails(name, description, startDate, endDate);
        CreatedOn = DateTime.UtcNow;
    }

    public new long Id { get; private set; }

    public Guid UniqueId { get; private set; }

    public long OwnerId { get; private set; }

    public User Owner { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public DateTime? StartDate { get; private set; }

    public DateTime? EndDate { get; private set; }

    public DateTime CreatedOn { get; private set; }

    public DateTime? UpdatedOn { get; private set; }

    private List<TripQueryHistory> QueryHistoryInternal { get; set; } = [];

    public IReadOnlyCollection<TripQueryHistory> QueryHistory => QueryHistoryInternal.AsReadOnly();

    public void Update(string name, string? description, DateTime? startDate, DateTime? endDate)
    {
        ApplyDetails(name, description, startDate, endDate);
        UpdatedOn = DateTime.UtcNow;
    }

    public void AddItem(
        int serviceTypeId,
        string queryParametersJson,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        string? resultSummary = null)
    {
        var item = TripQueryHistory.Create(
            serviceTypeId,
            queryParametersJson,
            startDateTime,
            endDateTime,
            resultSummary);

        item.AttachToTripVault(this);
        QueryHistoryInternal.Add(item);
        UpdatedOn = DateTime.UtcNow;
    }

    public bool IsActiveAt(DateTime utcNow)
    {
        var normalizedUtcNow = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
        var currentDate = normalizedUtcNow.Date;

        if (StartDate.HasValue && currentDate < StartDate.Value.Date)
        {
            return false;
        }

        if (EndDate.HasValue && currentDate > EndDate.Value.Date)
        {
            return false;
        }

        return true;
    }

    public void RemoveItem(long itemId)
    {
        var item = QueryHistoryInternal.FirstOrDefault(i => i.Id == itemId);
        if (item == null)
        {
            return;
        }

        QueryHistoryInternal.Remove(item);
        UpdatedOn = DateTime.UtcNow;
    }

    public void RemoveItem(Guid itemUniqueId)
    {
        var item = QueryHistoryInternal.FirstOrDefault(i => i.UniqueId == itemUniqueId);
        if (item == null)
        {
            return;
        }

        QueryHistoryInternal.Remove(item);
        UpdatedOn = DateTime.UtcNow;
    }

    private void ApplyDetails(string name, string? description, DateTime? startDate, DateTime? endDate)
    {
        var normalizedStartDate = NormalizeUtc(startDate);
        var normalizedEndDate = NormalizeUtc(endDate);
        if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
        {
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));
        }

        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException($"{name} cannot be empty.", name) : name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        StartDate = normalizedStartDate;
        EndDate = normalizedEndDate;
    }

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue
            ? value.Value.Kind == DateTimeKind.Utc
                ? value.Value
                : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : null;
}
