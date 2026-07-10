using DigitalBrain.Core.V2;
using Grpc.AspNetCore.Web;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Mcp;

public static class V2UiHostingExtensions
{
    public const string CorsPolicy = "digitalbrain-v2-ui-grpc-web";

    public static IServiceCollection AddV2UiTransport(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        V2RuntimeProfile profile)
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
            // KnownNetworks/KnownProxies retain the framework's loopback-only defaults. An
            // untrusted remote peer therefore cannot assert X-Forwarded-Proto=https.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
        });

        var bootstrap = V2UiBootstrapOptions.FromConfiguration(configuration);
        // Empty is a valid fail-closed production configuration: BootstrapSession always denies while an
        // external issuer can still mint exact-audience sessions through the shared V2 token service.
        services.AddSingleton(bootstrap);
        services.AddSingleton<V2UiBootstrapAuthenticator>();
        var defaultDelivery = V2UiDeliveryOptions.Default;
        var renewal = TimeSpan.TryParse(configuration["DigitalBrain:V2:Ui:ActionTokenRenewalInterval"], out var configuredRenewal)
            ? configuredRenewal
            : defaultDelivery.ActionTokenRenewalInterval;
        var revalidation = TimeSpan.TryParse(configuration["DigitalBrain:V2:Ui:AuthenticationRevalidationInterval"], out var configuredRevalidation)
            ? configuredRevalidation
            : defaultDelivery.AuthenticationRevalidationInterval;
        services.AddSingleton(new V2UiDeliveryOptions(renewal, revalidation).Validate());

        var configuredPath = configuration["DigitalBrain:V2:Ui:FeedStorePath"];
        if (profile == V2RuntimeProfile.Production && string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException("Production V2 UI requires a durable feed store path.");
        var feedPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "v2-data", "ui-feed.jsonl")
            : configuredPath;
        var feedIntegrityKey = V2UiFeedIntegrityKeyProvider.Resolve(configuration, profile, feedPath);
        services.AddSingleton<IV2PrivateFeedStore>(_ => new V2PrivateFeedStore(feedPath, integrityKey: feedIntegrityKey));
        services.AddSingleton<V2ActionExecutor>();
        services.AddSingleton<V2WorkspaceSurfaceProducer>();
        services.AddSingleton<V2SurfaceEnvelopeWriter>();
        services.AddSingleton<V2UiGrpcService>();
        services.AddHealthChecks().AddCheck<V2UiTransportHealthCheck>("v2-ui-transport", tags: ["ready"]);
        return services;
    }

    public static WebApplication MapV2UiTransport(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/digitalbrain.v2.ui.DigitalBrainV2Ui") && !context.Request.IsHttps)
            {
                context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                return;
            }
            await next().ConfigureAwait(false);
        });
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = false });
        app.UseCors();
        app.MapGrpcService<V2UiGrpcService>()
            .EnableGrpcWeb()
            .RequireCors(CorsPolicy);
        return app;
    }

    private static void ConfigureCors(CorsPolicyBuilder policy, IConfiguration configuration, V2RuntimeProfile profile)
    {
        var origins = (configuration["DigitalBrain:V2:Ui:AllowedOrigins"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (origins.Length > 0)
            policy.WithOrigins(origins);
        else if (profile != V2RuntimeProfile.Production)
            policy.AllowAnyOrigin();
        else
            policy.WithOrigins("https://invalid.digitalbrain.local");
        policy.AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding");
    }

}

public static class V2UiFeedIntegrityKeyProvider
{
    public static byte[] Resolve(IConfiguration configuration, V2RuntimeProfile profile, string feedPath)
    {
        _ = profile;
        _ = feedPath;
        var configured = configuration["DigitalBrain:V2:Ui:FeedIntegrityKey"];
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("V2 UI requires an explicit stable feed integrity key in every runtime profile.");
        var source = Decode(configured);
        return HMACSHA256.HashData(source, Encoding.UTF8.GetBytes("digitalbrain-v2-ui-feed-integrity-v1"));
    }

    private static byte[] Decode(string encoded)
    {
        byte[] source;
        try { source = Convert.FromBase64String(encoded); }
        catch (FormatException exception) { throw new InvalidOperationException("V2 UI feed integrity key material must be base64.", exception); }
        if (source.Length < 32) throw new InvalidOperationException("V2 UI feed integrity key material must be at least 256 bits.");
        return source;
    }
}

public sealed class V2UiTransportHealthCheck(
    IV2PrivateFeedStore feed,
    V2WorkspaceSurfaceProducer producer,
    V2SurfaceEnvelopeWriter envelopeWriter,
    V2UiGrpcService service) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Constructor resolution eagerly opens/validates the durable feed and the complete transport graph.
        _ = feed;
        _ = producer;
        _ = envelopeWriter;
        _ = service;
        return Task.FromResult(HealthCheckResult.Healthy("V2 UI transport composition is ready."));
    }
}
