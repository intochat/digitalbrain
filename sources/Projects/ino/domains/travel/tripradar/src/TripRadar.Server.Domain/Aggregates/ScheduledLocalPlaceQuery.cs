using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.SeedWork;
using TripRadar.Server.Domain.ValueObjects;

namespace TripRadar.Server.Domain.Aggregates;

public class ScheduledLocalPlaceQuery : AggregateRoot<long>
{
    private ScheduledLocalPlaceQuery()
    {
    }

    public ScheduledLocalPlaceQuery(
        string searchQuery,
        long scheduledExecutionId,
        long userId,
        string? additionalParameters = null,
        IList<QueryColumn>? selectedColumns = null)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId), "User id must be greater than 0.");
        }

        if (scheduledExecutionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scheduledExecutionId), "Scheduled execution id must be greater than 0.");
        }

        UniqueId = Guid.NewGuid();
        SearchQuery = NormalizeRequired(searchQuery, nameof(searchQuery));
        UserId = userId;
        ScheduledExecutionId = scheduledExecutionId;
        CreatedOn = DateTime.UtcNow;
        AdditionalParameters = NormalizeOptional(additionalParameters);
        SelectedColumns = selectedColumns;
    }

    public new long Id { get; private set; }

    public Guid UniqueId { get; private set; }

    public string SearchQuery { get; private set; } = null!;

    public User User { get; private set; } = null!;
    public long UserId { get; private set; }

    public DateTime CreatedOn { get; private set; }

    public DateTime? UpdatedOn { get; private set; }

    public string? AdditionalParameters { get; private set; }

    public IList<QueryColumn>? SelectedColumns { get; private set; }

    public long ScheduledExecutionId { get; private set; }
    public ScheduledExecution ScheduledExecution { get; private set; } = null!;

    public void Update(string searchQuery, string? additionalParameters, IList<QueryColumn>? selectedColumns)
    {
        SearchQuery = NormalizeRequired(searchQuery, nameof(searchQuery));
        AdditionalParameters = NormalizeOptional(additionalParameters);
        SelectedColumns = selectedColumns;
        UpdatedOn = DateTime.UtcNow;
    }

    private static string NormalizeRequired(string value, string paramName) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{paramName} cannot be empty.", paramName) : value.Trim();

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

