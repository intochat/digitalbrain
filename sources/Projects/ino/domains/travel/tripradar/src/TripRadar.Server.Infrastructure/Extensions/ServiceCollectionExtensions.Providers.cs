using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.Contracts.Providers;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Filters;
using TripRadar.Server.Infrastructure.Providers.Kiwi;
using TripRadar.Server.Infrastructure.Providers.Kiwi.Settings;
using TripRadar.Server.Infrastructure.Providers.SerpApi.Client;
using TripRadar.Server.Infrastructure.Providers.SerpApi.Settings;
using TripRadar.Server.Infrastructure.Services.Handlers;
using TripRadar.Server.Infrastructure.Services.Providers.SerpApi;
using TripRadar.Server.Infrastructure.Settings;
using TripRadar.Server.Mocks.Extensions;

namespace TripRadar.Server.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
{
    private static IServiceCollection ConfigureExternalServiceProviders(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var mockSettings = configuration.GetSection("MockApi").Get<MockApi>() ?? new MockApi();
        var useSerpApiMock = environment.IsDevelopment() && mockSettings.SerpApi;

        if (useSerpApiMock)
        {
            services.ConfigureTripRadarMocks();
        }
        else
        {
            services.AddHttpClient<ISerpApiProvider, SerpApiProvider>((serviceProvider, client) =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<SerpApiSettings>>().Value;
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds));
            });
        }

        services.AddHttpClient<IFlightPriceCalendarProvider, KiwiFlightPriceCalendarProvider>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<KiwiCalendarSettings>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds));
        });

        services.AddScoped<ISerpApiProviderService, SerpApiProviderService>();
        services.AddScoped<ISearchResponseFilter<GetEventResponseDTO>, EventResponseFilter>();
        services.AddScoped<ISearchResponseFilter<GetFlightResponseDTO>, FlightResponseFilter>();
        services.AddScoped<ISearchResponseFilter<GetHotelResponseDTO>, HotelResponseFilter>();
        services.AddScoped<ISearchResponseFilter<GetLocalPlacesResponseDTO>, LocalPlacesResponseFilter>();
        services.AddScoped<EventSerpApiHandler>();
        services.AddScoped<FlightSerpApiHandler>();
        services.AddScoped<LocalPlacesSerpApiHandler>();
        services.AddScoped<HotelSerpApiHandler>();
        return services;
    }
}
