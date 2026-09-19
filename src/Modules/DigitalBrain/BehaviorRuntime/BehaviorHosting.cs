using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Core;

public static class BehaviorHosting
{
    public static IServiceCollection AddBehavior<T>(this IServiceCollection services) where T : class, IBehavior
    {
        services.AddSingleton<IBehavior, T>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, BehaviorHost>());
        return services;
    }
}

internal sealed class BehaviorHost(IEnumerable<IBehavior> behaviors) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(behaviors.Select(behavior => behavior.RunAsync(stoppingToken)));
}
