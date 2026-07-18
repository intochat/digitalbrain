using Microsoft.Extensions.DependencyInjection;
using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Managers;

namespace TripRadar.MiniApp.Client.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddMiniAppClientInfrastructure(this IServiceCollection services, Uri baseAddress)
    {
        services.AddScoped(_ => new HttpClient { BaseAddress = baseAddress });
        services.AddScoped<TripRadarApiClient>();

        services.AddScoped<IAirportManager, AirportManager>();
        services.AddScoped<IFlightManager, FlightManager>();
        services.AddScoped<IFlightExploreManager, FlightExploreManager>();
        services.AddScoped<IHotelManager, HotelManager>();
        services.AddScoped<IPriceCalendarManager, PriceCalendarManager>();
        services.AddScoped<IPriceTrackingManager, PriceTrackingManager>();
        services.AddScoped<IUserManager, UserManager>();
    }
}