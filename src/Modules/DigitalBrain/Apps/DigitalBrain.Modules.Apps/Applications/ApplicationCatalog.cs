using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Apps;

public sealed class ApplicationCatalog(IEnumerable<AppDefinition> applications, IGrainFactory grains)
{
    public IReadOnlyList<AppDefinition> Applications { get; } = [.. applications];

    public async Task Start(string application, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var definition = Applications.SingleOrDefault(app => app.Name == application)
            ?? throw new ArgumentException($"No application '{application}' is registered.", nameof(application));
        var start = new ApplicationStart(key, grains);
        foreach (var step in definition.Starts) { await step(start); }
    }
}

public static class ApplicationHosting
{
    public static IServiceCollection AddApplication(this IServiceCollection services, AppDefinition application)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(application);
        foreach (var configure in application.Services) { configure(services); }
        services.AddSingleton(application);
        services.TryAddSingleton<ApplicationCatalog>();
        return services;
    }

    public static ISiloBuilder AddApplication(this ISiloBuilder silo, AppDefinition application)
    {
        ArgumentNullException.ThrowIfNull(silo);
        ArgumentNullException.ThrowIfNull(application);
        silo.Services.AddApplication(application);
        return silo;
    }
}
