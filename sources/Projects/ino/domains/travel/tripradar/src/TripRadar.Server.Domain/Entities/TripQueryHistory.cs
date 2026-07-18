using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Entities;

public class TripQueryHistory : Entity<long>
{
    private TripQueryHistory()
    {
    }

    private TripQueryHistory(
        int serviceTypeId,
        string queryParametersJson,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        string? resultSummary = null)
    {
        UniqueId = Guid.NewGuid();
        ServiceTypeId = serviceTypeId > 0
            ? serviceTypeId
            : throw new ArgumentOutOfRangeException(nameof(serviceTypeId), "Service type id must be greater than 0.");
        QueryParametersJson = NormalizeRequired(queryParametersJson, nameof(queryParametersJson));
        StartDateTime = NormalizeUtc(startDateTime);
        EndDateTime = NormalizeUtc(endDateTime);
        ResultSummary = NormalizeOptional(resultSummary);
        ValidateDateRange(StartDateTime, EndDateTime);
        CreatedOn = DateTime.UtcNow;
    }

    public static TripQueryHistory Create(
        int serviceTypeId,
        string queryParametersJson,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        string? resultSummary = null)
    {
        return new TripQueryHistory(serviceTypeId, queryParametersJson, startDateTime, endDateTime, resultSummary);
    }

    public new long Id { get; private set; }

    public Guid UniqueId { get; private set; }

    public long TripVaultId { get; private set; }

    public TripVault TripVault { get; private set; } = null!;

    public int ServiceTypeId { get; private set; }

    public string QueryParametersJson { get; private set; } = null!;

    public DateTime? StartDateTime { get; private set; }

    public DateTime? EndDateTime { get; private set; }

    public string? ResultSummary { get; private set; }

    public DateTime CreatedOn { get; private set; }

    internal void AttachToTripVault(TripVault tripVault)
    {
        ArgumentNullException.ThrowIfNull(tripVault);

        TripVault = tripVault;
        if (tripVault.Id > 0)
        {
            TripVaultId = tripVault.Id;
        }
    }

    private static void ValidateDateRange(DateTime? startDateTime, DateTime? endDateTime)
    {
        if (startDateTime.HasValue && endDateTime.HasValue && endDateTime.Value < startDateTime.Value)
        {
            throw new ArgumentException("End date must be on or after start date.", nameof(endDateTime));
        }
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

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue
            ? value.Value.Kind == DateTimeKind.Utc
                ? value.Value
                : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : null;
}
