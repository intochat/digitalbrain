using Azure.Data.Tables;
using DigitalBrain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class DigitalBrainAspireExtensions
{
    private const string ClientSectionName = "DigitalBrain:Client";
    private const string SupportedContractVersion = "1";
    private const string AzureTableStorageProviderType = "AzureTableStorage";

    public static IHostApplicationBuilder AddDigitalBrainClient(
        this IHostApplicationBuilder builder,
        string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var existingRegistration = builder.Services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(DigitalBrainAspireClientRegistration))
            .Select(descriptor =>
                descriptor.ImplementationInstance as DigitalBrainAspireClientRegistration)
            .FirstOrDefault(registration => registration is not null);
        if (existingRegistration is not null)
        {
            if (!string.Equals(existingRegistration.Name, name, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "A host can register only one DigitalBrain client connection.");
            return builder;
        }

        if (!HasRestrictedConfiguration(builder.Configuration))
            AddDirectConnectionDefaults(builder.Configuration, name);

        var serviceKey = builder.Configuration["Orleans:Clustering:ServiceKey"];
        var resolvedServiceKey = string.IsNullOrWhiteSpace(serviceKey) ? name : serviceKey;
        var connectionHealth = new DigitalBrainClientConnectionHealthCheck();
        builder.Services.AddSingleton(new DigitalBrainAspireClientRegistration(name));
        builder.Services.AddSingleton(connectionHealth);
        builder.Services
            .AddHealthChecks()
            .Add(new HealthCheckRegistration(
                "digitalbrain-client-orleans",
                _ => connectionHealth,
                HealthStatus.Unhealthy,
                ["digitalbrain", "orleans"]));
        builder.Services
            .AddOptions<DigitalBrainClientOptions>()
            .Bind(builder.Configuration.GetSection(ClientSectionName))
            .Validate(
                options => string.Equals(options.Name, name, StringComparison.Ordinal),
                "The DigitalBrain client name must match the registered connection name.")
            .Validate(
                options => string.Equals(
                    options.ContractVersion,
                    SupportedContractVersion,
                    StringComparison.Ordinal),
                "The DigitalBrain client contract version is unsupported.")
            .Validate(
                _ => HasValidOrleansConfiguration(builder.Configuration),
                "The DigitalBrain Orleans client configuration is incomplete or unsupported.")
            .Validate(
                _ => HasValidConnection(builder.Configuration, resolvedServiceKey),
                "The DigitalBrain client connection is missing or malformed.")
            .ValidateOnStart();

        if (HasValidConnection(builder.Configuration, resolvedServiceKey))
        {
            builder.AddKeyedAzureTableServiceClient(resolvedServiceKey);
        }
        else
        {
            builder.Services.AddKeyedSingleton<TableServiceClient>(
                resolvedServiceKey,
                (services, _) =>
                {
                    _ = services
                        .GetRequiredService<IOptions<DigitalBrainClientOptions>>()
                        .Value;
                    return new TableServiceClient(
                        builder.Configuration.GetConnectionString(resolvedServiceKey)!);
                });
        }
        builder.UseOrleansClient(clientBuilder =>
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.AddClusterConnectionStatusObserver(connectionHealth);
            clientBuilder.AddDigitalBrainClient();
        });
        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource(DigitalBrainClientTelemetry.ActivitySourceName)
                .AddSource("Microsoft.Orleans.Runtime")
                .AddSource("Microsoft.Orleans.Application"))
            .WithMetrics(metrics => metrics
                .AddMeter(DigitalBrainClientTelemetry.MeterName)
                .AddMeter("Microsoft.Orleans"));
        return builder;
    }

    private static bool HasRestrictedConfiguration(IConfiguration configuration) =>
        HasValue(configuration["DigitalBrain:Client:Name"]) ||
        HasValue(configuration["DigitalBrain:Client:ContractVersion"]) ||
        HasValue(configuration["Orleans:ClusterId"]) ||
        HasValue(configuration["Orleans:ServiceId"]) ||
        HasValue(configuration["Orleans:Clustering:ProviderType"]) ||
        HasValue(configuration["Orleans:Clustering:ServiceKey"]);

    private static void AddDirectConnectionDefaults(
        IConfigurationManager configuration,
        string name) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Client:Name"] = name,
            ["DigitalBrain:Client:ContractVersion"] = SupportedContractVersion,
            ["Orleans:ClusterId"] = $"{name}-cluster",
            ["Orleans:ServiceId"] = $"{name}-service",
            ["Orleans:Clustering:ProviderType"] = AzureTableStorageProviderType,
            ["Orleans:Clustering:ServiceKey"] = name
        });

    private static bool HasValidOrleansConfiguration(IConfiguration configuration) =>
        HasValue(configuration["Orleans:ClusterId"]) &&
        HasValue(configuration["Orleans:ServiceId"]) &&
        string.Equals(
            configuration["Orleans:Clustering:ProviderType"],
            AzureTableStorageProviderType,
            StringComparison.Ordinal) &&
        HasValue(configuration["Orleans:Clustering:ServiceKey"]);

    private static bool HasValidConnection(
        IConfiguration configuration,
        string serviceKey)
    {
        var connectionString = configuration.GetConnectionString(serviceKey);
        if (!HasValue(connectionString))
            return false;
        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var serviceUri) &&
            (serviceUri.Scheme == Uri.UriSchemeHttps ||
             serviceUri.Scheme == Uri.UriSchemeHttp))
            return true;

        try
        {
            _ = new TableServiceClient(connectionString);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool HasValue(string? value) =>
        !string.IsNullOrWhiteSpace(value);

    private sealed record DigitalBrainAspireClientRegistration(string Name);
}
