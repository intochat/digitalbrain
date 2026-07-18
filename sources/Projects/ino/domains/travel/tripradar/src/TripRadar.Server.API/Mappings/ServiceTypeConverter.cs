using AutoMapper;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Mappings;

internal sealed class ServiceTypeConverter : ITypeConverter<ServiceType, Domain.Enums.ServiceType>
{
    public Domain.Enums.ServiceType Convert(ServiceType source, Domain.Enums.ServiceType destination, ResolutionContext context) =>
        source switch
        {
            ServiceType.Event => Domain.Enums.ServiceType.Event,
            ServiceType.Flight => Domain.Enums.ServiceType.Flight,
            ServiceType.Hotel => Domain.Enums.ServiceType.Hotel,
            ServiceType.LocalPlaces => Domain.Enums.ServiceType.LocalPlaces,
            ServiceType.Maps => Domain.Enums.ServiceType.Maps,
            ServiceType.PlaceReview => Domain.Enums.ServiceType.PlaceReview,
            ServiceType.FlightExplore => Domain.Enums.ServiceType.FlightExplore,
            ServiceType.TripAdvisorSearch => Domain.Enums.ServiceType.TripAdvisorSearch,
            ServiceType.TripAdvisorPlace => Domain.Enums.ServiceType.TripAdvisorPlace,
            ServiceType.OpenTableReview => Domain.Enums.ServiceType.OpenTableReview,
            ServiceType.YelpSearch => Domain.Enums.ServiceType.YelpSearch,
            ServiceType.YelpPlace => Domain.Enums.ServiceType.YelpPlace,
            ServiceType.YelpReviews => Domain.Enums.ServiceType.YelpReviews,
            ServiceType.YelpPlaceFullMenu => Domain.Enums.ServiceType.YelpPlaceFullMenu,
            ServiceType.MapsDirections => Domain.Enums.ServiceType.MapsDirections,
            ServiceType.MapsPlaceResults => Domain.Enums.ServiceType.MapsPlaceResults,
            ServiceType.GoogleLightSearch => Domain.Enums.ServiceType.GoogleLightSearch,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
}
