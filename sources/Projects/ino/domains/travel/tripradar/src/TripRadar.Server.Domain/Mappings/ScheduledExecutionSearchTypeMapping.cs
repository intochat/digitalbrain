using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Domain.Mappings;

public static class ScheduledExecutionSearchTypeMapping
{
    private static readonly Dictionary<ScheduledExecutionSearchType, ServiceType> _searchTypeToServiceTypeMap = new()
    {
        { ScheduledExecutionSearchType.Flights, ServiceType.Flight },
        { ScheduledExecutionSearchType.Hotels, ServiceType.Hotel },
        { ScheduledExecutionSearchType.Events, ServiceType.Event },
        { ScheduledExecutionSearchType.LocalPlaces, ServiceType.LocalPlaces }
    };

    public static ServiceType? ToServiceType(this ScheduledExecutionSearchType searchType) => _searchTypeToServiceTypeMap.GetValueOrDefault(searchType);
}
