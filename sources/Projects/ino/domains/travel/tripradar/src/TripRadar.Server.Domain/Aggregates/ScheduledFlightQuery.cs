using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.SeedWork;
using TripRadar.Server.Domain.ValueObjects;

namespace TripRadar.Server.Domain.Aggregates;

public class ScheduledFlightQuery : AggregateRoot<long>
{
    private ScheduledFlightQuery()
    {
    }

    public ScheduledFlightQuery(
        int departureAirportId,
        int destinationAirportId,
        long scheduledExecutionId,
        long userId,
        DateTime departureDate,
        DateTime? returnDate,
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
        ApplyRoute(departureAirportId, destinationAirportId, departureDate, returnDate, additionalParameters, selectedColumns);
        CreatedOn = DateTime.UtcNow;
    }

    public new long Id { get; private set; }

    public Guid UniqueId { get; private set; }

    public Airport DepartureAirport { get; private set; } = null!;
    public int DepartureAirportId { get; private set; }

    public Airport DestinationAirport { get; private set; } = null!;
    public int DestinationAirportId { get; private set; }

    public User User { get; private set; } = null!;
    public long UserId { get; private set; }

    public DateTime DepartureDate { get; private set; }

    public DateTime? ReturnDate { get; private set; }

    public DateTime CreatedOn { get; private set; }

    public DateTime? UpdatedOn { get; private set; }

    public string? AdditionalParameters { get; private set; }

    public IList<QueryColumn>? SelectedColumns { get; private set; }

    public long ScheduledExecutionId { get; private set; }
    public ScheduledExecution ScheduledExecution { get; private set; } = null!;

    public void Update(
        int departureAirportId,
        int destinationAirportId,
        DateTime departureDate,
        DateTime? returnDate,
        string? additionalParameters,
        IList<QueryColumn>? selectedColumns)
    {
        ApplyRoute(departureAirportId, destinationAirportId, departureDate, returnDate, additionalParameters, selectedColumns);
        UpdatedOn = DateTime.UtcNow;
    }

    private void ApplyRoute(
        int departureAirportId,
        int destinationAirportId,
        DateTime departureDate,
        DateTime? returnDate,
        string? additionalParameters,
        IList<QueryColumn>? selectedColumns)
    {
        if (departureAirportId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(departureAirportId), "Departure airport id must be greater than 0.");
        }

        if (destinationAirportId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationAirportId), "Destination airport id must be greater than 0.");
        }

        if (departureAirportId == destinationAirportId)
        {
            throw new ArgumentException("Departure and destination airports must be different.");
        }

        var normalizedDepartureDate = NormalizeUtc(departureDate);
        var normalizedReturnDate = NormalizeUtc(returnDate);
        if (normalizedReturnDate.HasValue && normalizedReturnDate.Value <= normalizedDepartureDate)
        {
            throw new ArgumentException("Return date must be after departure date.", nameof(returnDate));
        }

        DepartureAirportId = departureAirportId;
        DestinationAirportId = destinationAirportId;
        DepartureDate = normalizedDepartureDate;
        ReturnDate = normalizedReturnDate;
        AdditionalParameters = NormalizeOptional(additionalParameters);
        SelectedColumns = selectedColumns;
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? NormalizeUtc(DateTime? value) => value.HasValue ? NormalizeUtc(value.Value) : null;
}

