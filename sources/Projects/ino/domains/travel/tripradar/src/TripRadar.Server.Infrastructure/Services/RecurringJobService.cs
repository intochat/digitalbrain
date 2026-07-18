using Hangfire;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Infrastructure.Contracts.Scheduled;

namespace TripRadar.Server.Infrastructure.Services;

public class RecurringJobService(IRecurringJobManager recurringJobManager, ILogger<RecurringJobService> logger) : IRecurringJobService
{
    public void ScheduleRecurringExecution(Guid uniqueId, string schedule, string? timeZoneCode, CancellationToken cancellationToken = default)
    {
        var options = new RecurringJobOptions
        {
            TimeZone = ResolveTimeZoneOrDefault(timeZoneCode)
        };

        recurringJobManager.AddOrUpdate<IScheduledQueryExecutionService>(
            $"scheduled-execution-{uniqueId}",
            job => job.ExecuteQueryAsync(uniqueId, CancellationToken.None),
            schedule,
            options);
    }

    public void DeleteRecurringExecution(Guid scheduledExecutionUniqueId) =>
        recurringJobManager.RemoveIfExists($"scheduled-execution-{scheduledExecutionUniqueId}");

    private TimeZoneInfo ResolveTimeZoneOrDefault(string? timeZoneCode)
    {
        if (string.IsNullOrWhiteSpace(timeZoneCode))
        {
            return TimeZoneInfo.Utc;
        }

        var normalizedId = timeZoneCode.Trim();
        if (TryFindTimeZone(normalizedId, out var directTimeZone))
        {
            return directTimeZone;
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(normalizedId, out var windowsTimeZoneId) && TryFindTimeZone(windowsTimeZoneId, out var windowsTimeZone))
        {
            return windowsTimeZone;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(normalizedId, out var ianaTimeZoneId) && TryFindTimeZone(ianaTimeZoneId, out var ianaTimeZone))
        {
            return ianaTimeZone;
        }

        logger.LogWarning("Falling back to UTC for scheduled execution. Unsupported timezone id: {TimeZoneId}", normalizedId);
        return TimeZoneInfo.Utc;
    }

    private static bool TryFindTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
