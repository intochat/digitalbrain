using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TripRadar.Server.Application.Contracts.Jobs;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Infrastructure.Jobs;
using TripRadar.Server.Infrastructure.Services;

namespace TripRadar.Server.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
{
    private static IServiceCollection ConfigureBackgroundJobsInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) =>
        services
            .AddScoped<IRecurringJobService, RecurringJobService>()
            .AddScoped<IResetTokensJob, ResetTokensJob>()
            .AddScoped<IDeferredDowngradeJob, DeferredDowngradeJob>()
            .AddScoped<ITripVaultQueryHistoryJob, TripVaultQueryHistoryJob>()
            .AddScoped<IMetterBillingJobs, MetterBillingJobs>()
            .AddScoped<ISubscriptionEmailJob, SubscriptionEmailJob>()
            .AddScoped<Domain.Events.IDomainEventHandler<Domain.Events.TokenConsumedDomainEvent>, TokenConsumedBackgroundJobHandler>();
}
