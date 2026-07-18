using Microsoft.Extensions.DependencyInjection;
using TripRadar.Server.Application.Contracts.Providers;
using TripRadar.Server.Mocks.Builders;
using TripRadar.Server.Mocks.Clients;

namespace TripRadar.Server.Mocks.Extensions;

public static class ServiceCollectionExtensions
{
    public static void ConfigureTripRadarMocks(this IServiceCollection services)
    {
        services
            .AddSingleton<SerpApiGoogleFlightResponseBuilder>()
            .AddSingleton<SerpApiGoogleHotelResponseBuilder>()
            .AddSingleton<SerpApiGoogleEventsResponseBuilder>()
            .AddSingleton<SerpApiGoogleLocalResponseBuilder>()
            .AddSingleton<SerpApiGoogleMapsResponseBuilder>()
            .AddSingleton<SerpApiPlaceReviewsResponseBuilder>()
            .AddSingleton<SerpApiGoogleTravelExploreResponseBuilder>()
            .AddSingleton<SerpApiTripAdvisorSearchResponseBuilder>()
            .AddSingleton<SerpApiTripAdvisorPlaceResponseBuilder>()
            .AddSingleton<ISerpApiProvider, MockSerpApiProvider>();
    }
}
