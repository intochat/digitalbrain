using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Brain.Gateway;

public static class GatewayHosting
{
    public static WebApplicationBuilder AddGatewayServices(this WebApplicationBuilder builder)
    {
        builder.UseOrleansClient(client =>
        {
            if (builder.Configuration.GetValue("Orleans:UseLocalhostClustering", false))
            {
                client.UseLocalhostClustering(
                    clusterId: builder.Configuration["Orleans:ClusterId"] ?? "dev",
                    serviceId: builder.Configuration["Orleans:ServiceId"] ?? "dev");
            }
        });

        AddGatewayApplicationServices(builder.Services, builder.Configuration);
        return builder;
    }

    public static IServiceCollection AddGatewayApplicationServices(IServiceCollection services, IConfiguration configuration)
    {
        var feedSection = configuration.GetSection(GatewayFeedOptions.SectionName);
        services.Configure<GatewayFeedOptions>(feedSection);
        var feedOptions = feedSection.Get<GatewayFeedOptions>() ?? new GatewayFeedOptions();
        feedOptions.EnsureValid();

        services.AddSingleton<ITypedNeuronLookup, ClusterTypedNeuronLookup>();
        services.AddSingleton<ISurfaceOwnerResolver, TypedSurfaceOwnerResolver>();
        services.AddSingleton<IUiFeedGrainAccessor, ClusterUiFeedGrainAccessor>();
        services.AddSingleton<IDurableFeed, OrleansDurableFeed>();
        services.AddSingleton<ILiveFeedSubscriptionFactory, OrleansLiveFeedSubscriptionFactory>();
        services.AddSingleton<UiGatewayService>();
        return services;
    }
}
