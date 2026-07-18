using TripRadar.Server.Domain.Enums;
using DomainServiceType = TripRadar.Server.Domain.Enums.ServiceType;

namespace TripRadar.Server.Domain.Mappings;

public static class ServiceTypeToProviderMapping
{
    private static readonly Dictionary<string, ProvidersType> _serviceToProviderMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { DomainServiceType.Flight.Name, ProvidersType.SerpApi },
        { DomainServiceType.Hotel.Name, ProvidersType.SerpApi },
        { DomainServiceType.Event.Name, ProvidersType.SerpApi },
        { DomainServiceType.LocalPlaces.Name, ProvidersType.SerpApi },
        { DomainServiceType.Maps.Name, ProvidersType.SerpApi },
        { DomainServiceType.PlaceReview.Name, ProvidersType.SerpApi },
        { DomainServiceType.FlightExplore.Name, ProvidersType.SerpApi },
        { DomainServiceType.TripAdvisorSearch.Name, ProvidersType.SerpApi },
        { DomainServiceType.TripAdvisorPlace.Name, ProvidersType.SerpApi },
        { DomainServiceType.OpenTableReview.Name, ProvidersType.SerpApi },
        { DomainServiceType.YouTubeSearch.Name, ProvidersType.SerpApi },
        { DomainServiceType.YelpSearch.Name, ProvidersType.SerpApi },
        { DomainServiceType.YelpPlace.Name, ProvidersType.SerpApi },
        { DomainServiceType.YelpReviews.Name, ProvidersType.SerpApi },
        { DomainServiceType.YelpPlaceFullMenu.Name, ProvidersType.SerpApi },
        { DomainServiceType.MapsDirections.Name, ProvidersType.SerpApi },
        { DomainServiceType.MapsPlaceResults.Name, ProvidersType.SerpApi },
        { DomainServiceType.GoogleLightSearch.Name, ProvidersType.SerpApi }
    };


    /// <summary>
    /// Gets the provider name for feature flag checks.
    /// </summary>
    public static string? GetProviderName(string serviceTypeName) => _serviceToProviderMap.GetValueOrDefault(serviceTypeName)?.Name;
}
