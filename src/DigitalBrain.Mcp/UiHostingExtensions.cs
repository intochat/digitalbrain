using DigitalBrain.Kernel.Contracts.Runtime;
using Grpc.AspNetCore.Web;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace DigitalBrain.Mcp;

public static class UiHostingExtensions
{
    public const string CorsPolicy = "digitalbrain-runtime-ui-grpc-web";
    public static IServiceCollection AddUiTransport(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, RuntimeProfile profile)
    {
        services.AddGrpc(options =>
        {
            options.MaxReceiveMessageSize = 128 * 1024;
            options.MaxSendMessageSize = 2 * 1024 * 1024;
            options.EnableDetailedErrors = false;
        });
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy => ConfigureCors(policy, configuration, profile)));
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            var trustContainerAppsIngress = profile == RuntimeProfile.Production &&
                string.Equals(configuration["DigitalBrain:Runtime:ForwardedHeaders:TrustAzureContainerAppsIngress"], "true", StringComparison.OrdinalIgnoreCase);
            if (trustContainerAppsIngress)
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }
        });
        var bootstrap = UiBootstrapOptions.FromConfiguration(configuration, profile);
        var externalIdentity = UiExternalIdentityOptions.FromConfiguration(configuration, profile);
        services.AddSingleton(bootstrap);
        services.AddSingleton<UiBootstrapAuthenticator>();
        services.AddSingleton(externalIdentity);
        services.AddSingleton<UiExternalIdentityAuthenticator>();
        var authentication = services.AddAuthentication();
        if (externalIdentity.Enabled)
            authentication.AddJwtBearer(UiExternalIdentityOptions.AuthenticationScheme, externalIdentity.Configure);
        var defaultDelivery = UiDeliveryOptions.Default;
        var renewal = TimeSpan.TryParse(configuration["DigitalBrain:Runtime:Ui:ActionTokenRenewalInterval"], out var configuredRenewal)
            ? configuredRenewal
            : defaultDelivery.ActionTokenRenewalInterval;
        var revalidation = TimeSpan.TryParse(configuration["DigitalBrain:Runtime:Ui:AuthenticationRevalidationInterval"], out var configuredRevalidation)
            ? configuredRevalidation
            : defaultDelivery.AuthenticationRevalidationInterval;
        services.AddSingleton(new UiDeliveryOptions(renewal, revalidation).Validate());
        services.AddSingleton<RuntimeSurfaceFeed>();
        services.AddSingleton<SurfaceEnvelopeWriter>();
        services.AddSingleton<UiGrpcService>();
        services.AddHealthChecks().AddCheck<UiTransportHealthCheck>("runtime-ui-transport", tags: ["ready"]);
        return services;
    }
    public static WebApplication MapUiTransport(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = false });
        app.UseCors();
        app.MapGrpcService<UiGrpcService>().EnableGrpcWeb().RequireCors(CorsPolicy);
        return app;
    }
    private static void ConfigureCors(CorsPolicyBuilder policy, IConfiguration configuration, RuntimeProfile profile)
    {
        var origins = (configuration["DigitalBrain:Runtime:Ui:AllowedOrigins"] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (origins.Length > 0)
            policy.WithOrigins(origins);
        else if (profile != RuntimeProfile.Production)
            policy.AllowAnyOrigin();
        else
            policy.WithOrigins("https://invalid.digitalbrain.local");
        policy.AllowAnyMethod().AllowAnyHeader().WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding");
    }
}
public sealed class UiTransportHealthCheck(RuntimeSurfaceFeed feed, SurfaceEnvelopeWriter envelopeWriter, UiGrpcService service) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = feed;
        _ = envelopeWriter;
        _ = service;
        return Task.FromResult(HealthCheckResult.Healthy("UI transport composition is ready."));
    }
}
