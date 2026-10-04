using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform;
using DigitalBrain.Platform.Identity;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;

namespace DigitalBrain.Testing.Module;

// The module tier's HTTP edge: the product's own pipeline (UsePlatformHttp → IHttpModule routes →
// platform routes) hosted on a loopback Kestrel port against the in-process cluster. Endpoint
// services that modules registered on the silo resolve through a per-request fallback into the
// silo's provider, so a module's endpoints run here exactly as composed, without a container boot.
internal static class ModuleHttpEdge
{
    public static async Task<WebApplication> StartAsync(ModuleBrain brain, IReadOnlyList<ModuleDefinition> modules,
        ModuleOptions options, string masterKey, HashSet<Type> siloServiceTypes, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        TestLogging.Apply(builder.Configuration);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DigitalBrainNames.MasterKeyConfigurationKey] = masterKey,
            ["DigitalBrain:Auth:Posture"] = "Open",
        });
        foreach (var definition in modules) { builder.Configuration.AddInMemoryCollection(definition.Configuration); }
        builder.Configuration.AddInMemoryCollection(options.Execution.PrivateConfiguration);
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Production co-hosts the web pipeline in the silo's container, so endpoint grain calls
        // carry silo provenance, not an external client's. The edge mirrors that by serving the
        // silo's own factory and brain client.
        var silo = brain.SiloServices;
        builder.Services.AddSingleton(silo.GetRequiredService<IDigitalBrain>());
        builder.Services.AddSingleton(silo.GetRequiredService<IGrainFactory>());
        foreach (var surface in silo.GetServices<IHttpSurface>()) { builder.Services.AddSingleton(surface); }
        builder.Services.AddIdentity();
        // Minimal-API parameter binding decides "service or body?" at map time, and the container
        // special-cases IServiceProviderIsService, so silo services must be real registrations
        // here: every silo service type the web container does not define forwards to the silo.
        var webServiceTypes = builder.Services.Select(descriptor => descriptor.ServiceType).ToHashSet();
        foreach (var serviceType in siloServiceTypes)
        {
            if (serviceType.IsGenericTypeDefinition || webServiceTypes.Contains(serviceType)) { continue; }
            builder.Services.AddTransient(serviceType, _ => silo.GetRequiredService(serviceType));
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            await using var scope = silo.CreateAsyncScope();
            context.RequestServices = new FallbackServiceProvider(context.RequestServices, scope.ServiceProvider);
            await next(context);
        });
        app.UsePlatformHttp();
        foreach (var definition in modules)
        {
            if (definition.CreateModule() is IHttpModule http) { http.Configure(app); }
        }
        app.MapDigitalBrainPlatform();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private sealed class FallbackServiceProvider(IServiceProvider primary, IServiceProvider silo)
        : IServiceProvider, ISupportRequiredService, IKeyedServiceProvider
    {
        public object? GetService(Type serviceType) => primary.GetService(serviceType) ?? silo.GetService(serviceType);

        public object GetRequiredService(Type serviceType)
            => GetService(serviceType)
                ?? throw new InvalidOperationException($"No service for type '{serviceType}' in the web edge or the silo.");

        public object? GetKeyedService(Type serviceType, object? serviceKey)
            => (primary as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey)
                ?? (silo as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);

        public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
            => GetKeyedService(serviceType, serviceKey)
                ?? throw new InvalidOperationException($"No keyed service for type '{serviceType}' ('{serviceKey}').");
    }
}
