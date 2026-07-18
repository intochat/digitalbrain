using Microsoft.Extensions.DependencyInjection;

namespace TripRadar.Server.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
{
    private static IServiceCollection ConfigureAutomapper(this IServiceCollection services) =>
        services.AddAutoMapper(_ => { }, typeof(ServiceCollectionExtensions));
}
