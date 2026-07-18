using System.Globalization;
using System.Text.Json;
using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Services;

public sealed class ScheduledExecutionValidityService : IScheduledExecutionValidityService
{
    public bool IsExecutableAtNextRun(ScheduledExecutionDetails details)
    {
        var searchType = ResolveSearchType(details.ServiceType);
        if (searchType is null)
        {
            return true;
        }

        var startDate = searchType switch
        {
            var type when Equals(type, ScheduledExecutionSearchType.Flights) => details.DepartureDate,
            var type when Equals(type, ScheduledExecutionSearchType.Hotels) => details.CheckInDate,
            var type when Equals(type, ScheduledExecutionSearchType.Events) => details.StartDate,
            _ => null
        };

        return IsExecutableAtNextRun(searchType, details.NextExecutionTime, startDate);
    }

    public bool IsExecutableAtNextRun(ScheduledExecutionSearchType searchType, DateTime nextExecutionTime, DateTime? startDate)
    {
        var normalizedNextExecutionTime = NormalizeUtc(nextExecutionTime);
        if (!Equals(searchType, ScheduledExecutionSearchType.LocalPlaces) && normalizedNextExecutionTime <= DateTime.UtcNow)
        {
            return false;
        }

        if (Equals(searchType, ScheduledExecutionSearchType.LocalPlaces) || !startDate.HasValue)
        {
            return true;
        }

        return NormalizeUtc(startDate.Value) >= normalizedNextExecutionTime;
    }

    public DateTime? ExtractEventStartDate(string? additionalParameters) =>
        ExtractDateTime(additionalParameters, "startDate", "start_date");

    public DateTime? ExtractEventEndDate(string? additionalParameters) =>
        ExtractDateTime(additionalParameters, "endDate", "end_date");

    private static ScheduledExecutionSearchType? ResolveSearchType(string? serviceType)
    {
        if (string.IsNullOrWhiteSpace(serviceType))
        {
            return null;
        }

        var normalized = serviceType.Trim();
        if (normalized.Contains("flight", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduledExecutionSearchType.Flights;
        }

        if (normalized.Contains("hotel", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduledExecutionSearchType.Hotels;
        }

        if (normalized.Contains("event", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduledExecutionSearchType.Events;
        }

        if (normalized.Contains("local", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduledExecutionSearchType.LocalPlaces;
        }

        return null;
    }

    private static DateTime? ExtractDateTime(string? additionalParameters, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(additionalParameters) || propertyNames.Length == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(additionalParameters);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!propertyNames.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => ParseDateTime(property.Value.GetString()),
                    _ => null
                };
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto.UtcDateTime;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            return DateTime.SpecifyKind(dateOnly.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        }

        return null;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
