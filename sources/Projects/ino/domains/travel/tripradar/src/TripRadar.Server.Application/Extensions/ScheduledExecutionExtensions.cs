using TripRadar.Server.Application.Constants.ScheduledExecutions;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Extensions;

public static class ScheduledExecutionExtensions
{
    public static ScheduledExecutionSearchType? GetSearchType(this ScheduledExecution scheduledExecution)
    {
        if (string.IsNullOrWhiteSpace(scheduledExecution.Name))
        {
            return null;
        }

        if (scheduledExecution.Name.Contains(ScheduledExecutionConstants.ScheduledFlight))
        {
            return ScheduledExecutionSearchType.Flights;
        }

        if (scheduledExecution.Name.Contains(ScheduledExecutionConstants.ScheduledHotel))
        {
            return ScheduledExecutionSearchType.Hotels;
        }

        if (scheduledExecution.Name.Contains(ScheduledExecutionConstants.ScheduledEvent))
        {
            return ScheduledExecutionSearchType.Events;
        }

        if (scheduledExecution.Name.Contains(ScheduledExecutionConstants.ScheduledLocalPlaces))
        {
            return ScheduledExecutionSearchType.LocalPlaces;
        }

        return ScheduledExecutionSearchType.Flights;
    }
}
