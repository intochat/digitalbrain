using AutoMapper;
using TripRadar.Server.Domain.Enums;
using ApiServiceType = TripRadar.Server.API.Contracts.Enums.ServiceType;

namespace TripRadar.Server.API.Mappings;

internal sealed class ServiceTypeProfile : Profile
{
    public ServiceTypeProfile()
    {
        CreateMap<ApiServiceType, ServiceType>().ConvertUsing<ServiceTypeConverter>();
        CreateMap<ServiceType, ApiServiceType>().ConvertUsing<DomainServiceTypeConverter>();
    }
}

internal sealed class DomainServiceTypeConverter : ITypeConverter<ServiceType, ApiServiceType>
{
    public ApiServiceType Convert(ServiceType source, ApiServiceType destination, ResolutionContext context)
    {
        if (Equals(source, ServiceType.Event)) return ApiServiceType.Event;
        if (Equals(source, ServiceType.Flight)) return ApiServiceType.Flight;
        if (Equals(source, ServiceType.Hotel)) return ApiServiceType.Hotel;
        if (Equals(source, ServiceType.LocalPlaces)) return ApiServiceType.LocalPlaces;
        if (Equals(source, ServiceType.Maps)) return ApiServiceType.Maps;
        if (Equals(source, ServiceType.PlaceReview)) return ApiServiceType.PlaceReview;
        if (Equals(source, ServiceType.FlightExplore)) return ApiServiceType.FlightExplore;
        if (Equals(source, ServiceType.TripAdvisorSearch)) return ApiServiceType.TripAdvisorSearch;
        if (Equals(source, ServiceType.TripAdvisorPlace)) return ApiServiceType.TripAdvisorPlace;
        if (Equals(source, ServiceType.OpenTableReview)) return ApiServiceType.OpenTableReview;
        if (Equals(source, ServiceType.YelpSearch)) return ApiServiceType.YelpSearch;
        if (Equals(source, ServiceType.YelpPlace)) return ApiServiceType.YelpPlace;
        if (Equals(source, ServiceType.YelpReviews)) return ApiServiceType.YelpReviews;
        if (Equals(source, ServiceType.YelpPlaceFullMenu)) return ApiServiceType.YelpPlaceFullMenu;
        if (Equals(source, ServiceType.MapsDirections)) return ApiServiceType.MapsDirections;
        if (Equals(source, ServiceType.MapsPlaceResults)) return ApiServiceType.MapsPlaceResults;
        if (Equals(source, ServiceType.GoogleLightSearch)) return ApiServiceType.GoogleLightSearch;

        throw new ArgumentOutOfRangeException(nameof(source), source, null);
    }
}
