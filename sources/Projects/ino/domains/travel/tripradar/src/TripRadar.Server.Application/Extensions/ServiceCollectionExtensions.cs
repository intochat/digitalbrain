using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TripRadar.Server.Application.Behaviors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Application.Services;
using TripRadar.Server.Application.Services.Authentication;

namespace TripRadar.Server.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureApplicationLayer(this IServiceCollection services)
    {
        return services
            .ConfigureMediatR()
            .ConfigureMappings()
            .ConfigureHttpAccessor()
            .ConfigureMetrics()
            .ConfigureApplicationServices();
    }

    private static IServiceCollection ConfigureMediatR(this IServiceCollection services)
    {
        return services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(ServiceCollectionExtensions).Assembly);
            configuration.AutoRegisterRequestProcessors = true;
            configuration.AddOpenBehavior(typeof(UserValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(LocalizationValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(TokenConsumptionBehavior<,>));
            configuration.AddOpenBehavior(typeof(PostSuccessTokenConsumptionBehavior<,>));
            configuration.AddOpenBehavior(typeof(PostSuccessUsageEventBehavior<,>));
            configuration.AddOpenBehavior(typeof(QueryHistoryBehavior<,>));
        });
    }

    private static IServiceCollection ConfigureMappings(this IServiceCollection services)
    {
        return services
            .AddAutoMapper(_ => { }, typeof(ServiceCollectionExtensions))
            .AddValidatorsFromAssembly(typeof(ServiceCollectionExtensions).Assembly, includeInternalTypes: true);
    }

    private static IServiceCollection ConfigureHttpAccessor(this IServiceCollection services)
    {
        return services.AddHttpContextAccessor();
    }

    private static IServiceCollection ConfigureMetrics(this IServiceCollection services)
    {
        return services.AddSingleton<CountMetric>();
    }

    private static IServiceCollection ConfigureApplicationServices(this IServiceCollection services)
    {
        services
            .AddScoped<ICurrentUserContext, CurrentUserContext>()
            .AddScoped<IAuthenticatedUserResolver, AuthenticatedUserResolver>()
            .AddScoped<IFlightQueryOrchestrator, FlightQueryOrchestrator>()
            .AddScoped<IReferenceLookupValidator, ReferenceLookupValidator>()
            .AddScoped<ISerpApiQueryExecutor, SerpApiQueryExecutor>()
            .AddScoped<IScheduledExecutionQueryUpdater, ScheduledExecutionQueryUpdater>()
            .AddScoped<ITripVaultResolutionService, TripVaultResolutionService>()
            .AddScoped<IUserAccessValidator, UserAccessValidator>()
            .AddScoped<IUserProfileAssembler, UserProfileAssembler>()
            .AddScoped<IUserProfileReferenceDataResolver, UserProfileReferenceDataResolver>()
            .AddScoped<ILoginOrchestrator, LoginOrchestrator>()
            .AddScoped<IRefreshTokenOrchestrator, RefreshTokenOrchestrator>();

        RegisterCustomRequestValidators(services);
        return services;
    }

    private static void RegisterCustomRequestValidators(IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;
        var validatorTypeDefinition = typeof(ICustomRequestValidator<>);

        foreach (var implementationType in assembly.GetTypes())
        {
            if (implementationType.IsAbstract || implementationType.IsInterface)
            {
                continue;
            }

            var serviceTypes = implementationType
                .GetInterfaces()
                .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == validatorTypeDefinition)
                .ToArray();

            foreach (var serviceType in serviceTypes)
            {
                services.AddScoped(serviceType, implementationType);
            }
        }
    }
}

