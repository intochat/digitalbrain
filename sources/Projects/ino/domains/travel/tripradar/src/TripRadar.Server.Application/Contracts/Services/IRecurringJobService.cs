namespace TripRadar.Server.Application.Contracts.Services;

public interface IRecurringJobService
{
    void ScheduleRecurringExecution(Guid uniqueId, string schedule, string? timeZoneCode, CancellationToken cancellationToken = default);

    void DeleteRecurringExecution(Guid scheduledExecutionUniqueId);
}
