namespace TripRadar.Server.Infrastructure.Contracts.Scheduled;

/// <summary>
/// Service for parsing and evaluating cron expressions.
/// Extracts cron parsing logic from ScheduledQueryExecutionService to follow SRP.
/// </summary>
public interface ICronExpressionService
{
    /// <summary>
    /// Gets the next occurrence time for a cron schedule.
    /// </summary>
    /// <param name="schedule">The cron expression string.</param>
    /// <returns>The next occurrence time, or null if no valid occurrence exists.</returns>
    DateTime? GetNextOccurrence(string schedule);
}
