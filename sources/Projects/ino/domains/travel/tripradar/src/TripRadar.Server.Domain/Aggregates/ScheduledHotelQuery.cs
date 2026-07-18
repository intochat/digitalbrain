using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.SeedWork;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Domain.ValueObjects;

namespace TripRadar.Server.Domain.Aggregates;

public class ScheduledHotelQuery : AggregateRoot<long>
{
    private ScheduledHotelQuery()
    {
    }

    public ScheduledHotelQuery(
        string location,
        long scheduledExecutionId,
        long userId,
        DateTime checkInDate,
        DateTime checkOutDate,
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
        UserId = userId;
        ScheduledExecutionId = scheduledExecutionId;
        ApplyDetails(location, checkInDate, checkOutDate, additionalParameters, selectedColumns);
        CreatedOn = DateTime.UtcNow;
    }

    public new long Id { get; private set; }

    public Guid UniqueId { get; private set; }

    public string Location { get; private set; } = null!;

    public User User { get; private set; } = null!;
    public long UserId { get; private set; }

    public DateTime CheckInDate { get; private set; }

    public DateTime CheckOutDate { get; private set; }

    public DateTime CreatedOn { get; private set; }

    public DateTime? UpdatedOn { get; private set; }

    public string? AdditionalParameters { get; private set; }

    public IList<QueryColumn>? SelectedColumns { get; private set; }

    public long ScheduledExecutionId { get; private set; }
    public ScheduledExecution ScheduledExecution { get; private set; } = null!;

    public void Update(
        string location,
        DateTime checkInDate,
        DateTime checkOutDate,
        string? additionalParameters,
        IList<QueryColumn>? selectedColumns)
    {
        ApplyDetails(location, checkInDate, checkOutDate, additionalParameters, selectedColumns);
        UpdatedOn = DateTime.UtcNow;
    }

    private void ApplyDetails(
        string location,
        DateTime checkInDate,
        DateTime checkOutDate,
        string? additionalParameters,
        IList<QueryColumn>? selectedColumns)
    {
        var normalizedCheckInDate = NormalizeUtc(checkInDate);
        var normalizedCheckOutDate = NormalizeUtc(checkOutDate);
        if (normalizedCheckOutDate <= normalizedCheckInDate)
        {
            throw new ArgumentException("Check-out date must be after check-in date.", nameof(checkOutDate));
        }

        Location = NormalizeRequired(location, nameof(location));
        CheckInDate = normalizedCheckInDate;
        CheckOutDate = normalizedCheckOutDate;
        AdditionalParameters = NormalizeOptional(additionalParameters);
        SelectedColumns = selectedColumns;
    }

    private static string NormalizeRequired(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

