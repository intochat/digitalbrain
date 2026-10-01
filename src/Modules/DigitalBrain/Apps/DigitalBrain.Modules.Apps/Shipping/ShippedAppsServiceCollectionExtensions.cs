using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Apps;

public static class ShippedAppsServiceCollectionExtensions
{
    public static IServiceCollection AddShippedApps(this IServiceCollection services, Assembly assembly, string resourcePrefix, string publisher)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);
        _ = PackageId.Create(publisher, "source");
        services.AddSingleton<IShippedAppSource>(new EmbeddedShippedAppSource(assembly, resourcePrefix, publisher));
        services.AddHostedService<ShippedAppPublisher>();
        return services;
    }
}
