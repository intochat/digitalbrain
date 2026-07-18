using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Infrastructure.Contracts.Scheduled;
using TripRadar.Server.Infrastructure.Services;
using TripRadar.Server.Infrastructure.Services.Strategies;

namespace TripRadar.Server.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
{
    private static IServiceCollection ConfigureSchedulingInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services
            .AddScoped<IScheduledQueryExecutionService, ScheduledQueryExecutionService>()
            .AddScoped<IScheduledExecutionDetailsQueryService, ScheduledExecutionDetailsQueryService>()
            .AddSingleton<IScheduledExecutionValidityService, ScheduledExecutionValidityService>()
            .AddScoped<IScheduledJobManager, HangfireScheduledJobManager>()
            .AddSingleton<ICronExpressionService, CronExpressionService>()
            .AddScoped<IScheduledExecutionStrategy, FlightExecutionStrategy>()
            .AddScoped<IScheduledExecutionStrategy, HotelExecutionStrategy>()
            .AddScoped<IScheduledExecutionStrategy, EventExecutionStrategy>()
            .AddScoped<IScheduledExecutionStrategy, LocalPlacesExecutionStrategy>();
}
