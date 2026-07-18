using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Contracts.Scheduled;

/// <summary>
/// Strategy interface for handling scheduled query executions.
/// Implementing the Strategy Pattern to eliminate OCP violations in ScheduledQueryExecutionService.
/// </summary>
/// <remarks>
/// Each implementation handles a specific search type, making the system open for extension
/// but closed for modification. New search types can be added by creating new strategy implementations.
/// </remarks>
public interface IScheduledExecutionStrategy
{
    /// <summary>
    /// Determines if this strategy can handle the given search type.
    /// </summary>
    /// <param name="searchType">The type of scheduled execution search.</param>
    /// <returns>True if this strategy can handle the search type.</returns>
    bool CanHandle(ScheduledExecutionSearchType searchType);

    /// <summary>
    /// Executes the scheduled query for the given execution.
    /// </summary>
    /// <param name="scheduledExecution">The scheduled execution to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the execution was successful and tokens should be consumed.</returns>
    Task<bool> ExecuteAsync(ScheduledExecution scheduledExecution, CancellationToken cancellationToken = default);
}
