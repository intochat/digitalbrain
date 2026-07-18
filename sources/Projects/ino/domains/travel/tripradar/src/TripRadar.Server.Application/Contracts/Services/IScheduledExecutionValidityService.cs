using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IScheduledExecutionValidityService
{
    bool IsExecutableAtNextRun(ScheduledExecutionDetails details);

    bool IsExecutableAtNextRun(ScheduledExecutionSearchType searchType, DateTime nextExecutionTime, DateTime? startDate);

    DateTime? ExtractEventStartDate(string? additionalParameters);

    DateTime? ExtractEventEndDate(string? additionalParameters);
}
