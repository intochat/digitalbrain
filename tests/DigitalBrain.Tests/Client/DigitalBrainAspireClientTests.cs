using Azure.Data.Tables;
using DigitalBrain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Orleans.Configuration;
using Xunit;

namespace DigitalBrain.Tests.Client;

public sealed class DigitalBrainAspireClientTests
{
    [Fact]
    public void Restricted_Aspire_configuration_registers_the_official_Orleans_client()
    {
        var builder = CreateRestrictedBuilder("brain", "brainaccount");

        builder.AddDigitalBrainClient("brain");
        using var host = builder.Build();

        Assert.NotNull(host.Services.GetRequiredService<IClusterClient>());
        Assert.NotNull(host.Services.GetRequiredKeyedService<TableServiceClient>("brain-clustering"));
        var cluster = host.Services.GetRequiredService<IOptions<ClusterOptions>>().Value;
        Assert.Equal("brain-cluster", cluster.ClusterId);
        Assert.Equal("brain-service", cluster.ServiceId);
        var options = host.Services.GetRequiredService<IOptions<DigitalBrainClientOptions>>().Value;
        Assert.Equal("brain", options.Name);
        Assert.Equal("1", options.ContractVersion);
    }

    [Fact]
    public void Direct_named_connection_uses_the_same_official_client_path()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:brain"] = StorageConnectionString("directaccount")
        });

        builder.AddDigitalBrainClient("brain");
        using var host = builder.Build();

        Assert.NotNull(host.Services.GetRequiredKeyedService<TableServiceClient>("brain"));
        var cluster = host.Services.GetRequiredService<IOptions<ClusterOptions>>().Value;
        Assert.Equal("brain-cluster", cluster.ClusterId);
        Assert.Equal("brain-service", cluster.ServiceId);
        var options = host.Services.GetRequiredService<IOptions<DigitalBrainClientOptions>>().Value;
        Assert.Equal("brain", options.Name);
        Assert.Equal("1", options.ContractVersion);
    }

    [Fact]
    public void Direct_named_service_uri_uses_the_same_official_client_path()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:brain"] = "https://directaccount.table.core.windows.net"
        });

        builder.AddDigitalBrainClient("brain");
        using var host = builder.Build();

        Assert.Equal(
            new Uri("https://directaccount.table.core.windows.net"),
            host.Services.GetRequiredKeyedService<TableServiceClient>("brain").Uri);
    }

    [Fact]
    public async Task Missing_connection_data_fails_startup_validation()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddDigitalBrainClient("brain");
        using var host = builder.Build();

        var error = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync());

        Assert.Contains(nameof(DigitalBrainClientOptions), error.OptionsType.Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_connection_data_fails_startup_validation()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:brain"] = "not-a-table-connection"
        });
        builder.AddDigitalBrainClient("brain");
        using var host = builder.Build();

        var error = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync());

        Assert.Contains(
            error.Failures,
            failure => failure.Contains("connection", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Health_checks_and_provider_neutral_telemetry_are_registered()
    {
        var builder = CreateRestrictedBuilder("brain", "healthaccount");

        builder.AddDigitalBrainClient("brain");

        Assert.Contains(builder.Services, descriptor =>
            descriptor.ServiceType == typeof(HealthCheckService));
        Assert.Contains(builder.Services, descriptor =>
            descriptor.ServiceType == typeof(TracerProvider));
        Assert.Contains(builder.Services, descriptor =>
            descriptor.ServiceType == typeof(MeterProvider));
        using var host = builder.Build();
        var health = host.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value;
        Assert.Contains(health.Registrations, registration =>
            registration.Name.Contains("brain-clustering", StringComparison.Ordinal));
        Assert.Contains(health.Registrations, registration =>
            registration.Name == "digitalbrain-client-orleans");
    }

    [Fact]
    public async Task Orleans_connection_health_tracks_gateway_loss_and_recovery()
    {
        var builder = CreateRestrictedBuilder("brain", "statusaccount");
        builder.AddDigitalBrainClient("brain");
        using var host = builder.Build();
        var observer = host.Services
            .GetRequiredService<IClusterConnectionStatusObserver>();
        var health = host.Services.GetRequiredService<HealthCheckService>();

        Assert.Equal(
            HealthStatus.Degraded,
            (await ReadConnectionHealthAsync(health)).Status);

        observer.NotifyGatewayCountChanged(2, 0, true);
        Assert.Equal(
            HealthStatus.Healthy,
            (await ReadConnectionHealthAsync(health)).Status);

        observer.NotifyClusterConnectionLost();
        Assert.Equal(
            HealthStatus.Unhealthy,
            (await ReadConnectionHealthAsync(health)).Status);
    }

    [Fact]
    public void Repeated_registration_is_idempotent_and_conflicts_fail()
    {
        var builder = CreateRestrictedBuilder("brain", "repeataccount");
        builder.AddDigitalBrainClient("brain");
        var serviceCount = builder.Services.Count;

        Assert.Same(builder, builder.AddDigitalBrainClient("brain"));
        Assert.Equal(serviceCount, builder.Services.Count);
        Assert.Throws<InvalidOperationException>(
            () => builder.AddDigitalBrainClient("other"));
    }

    [Fact]
    public void Independent_hosts_keep_connection_and_options_state_isolated()
    {
        var firstBuilder = CreateRestrictedBuilder("first", "firstaccount");
        var secondBuilder = CreateRestrictedBuilder("second", "secondaccount");
        firstBuilder.AddDigitalBrainClient("first");
        secondBuilder.AddDigitalBrainClient("second");

        using var first = firstBuilder.Build();
        using var second = secondBuilder.Build();

        Assert.Equal(
            "first-cluster",
            first.Services.GetRequiredService<IOptions<ClusterOptions>>().Value.ClusterId);
        Assert.Equal(
            "second-cluster",
            second.Services.GetRequiredService<IOptions<ClusterOptions>>().Value.ClusterId);
        Assert.NotEqual(
            first.Services.GetRequiredKeyedService<TableServiceClient>("first-clustering").Uri,
            second.Services.GetRequiredKeyedService<TableServiceClient>("second-clustering").Uri);
    }

    [Fact]
    public void Client_service_graph_contains_no_provider_or_privileged_durability_services()
    {
        var builder = CreateRestrictedBuilder("brain", "pureaccount");

        builder.AddDigitalBrainClient("brain");

        var serviceTypes = builder.Services.SelectMany(descriptor => new[]
        {
            descriptor.ServiceType,
            descriptor.ImplementationType,
            descriptor.ImplementationInstance?.GetType()
        }).Where(type => type is not null)
            .Cast<Type>()
            .Where(type =>
                type.FullName?.StartsWith("OrleansCodeGen.", StringComparison.Ordinal) != true)
            .Distinct()
            .ToArray();
        var serviceGraph = string.Join(
            '\n',
            serviceTypes.Select(type => type.AssemblyQualifiedName));
        Assert.DoesNotContain("OpenAI", serviceGraph, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Anthropic", serviceGraph, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.Extensions.AI", serviceGraph, StringComparison.Ordinal);
        Assert.DoesNotContain("Embedding", serviceGraph, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Journaling", serviceGraph, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Reminder", serviceGraph, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DigitalBrain.Kernel", serviceGraph, StringComparison.Ordinal);

        var integrationReferences = string.Join(
            '\n',
            typeof(DigitalBrainClientOptions).Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name));
        Assert.DoesNotContain(
            "Journaling",
            integrationReferences,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Reminder",
            integrationReferences,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "DigitalBrain.Kernel",
            integrationReferences,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Aspire.Hosting.AppHost",
            integrationReferences,
            StringComparison.Ordinal);
    }

    private static HostApplicationBuilder CreateRestrictedBuilder(
        string name,
        string accountName)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Client:Name"] = name,
            ["DigitalBrain:Client:ContractVersion"] = "1",
            ["Orleans:ClusterId"] = $"{name}-cluster",
            ["Orleans:ServiceId"] = $"{name}-service",
            ["Orleans:Clustering:ProviderType"] = "AzureTableStorage",
            ["Orleans:Clustering:ServiceKey"] = $"{name}-clustering",
            [$"ConnectionStrings:{name}-clustering"] = StorageConnectionString(accountName)
        });
        return builder;
    }

    private static string StorageConnectionString(string accountName) =>
        $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={Convert.ToBase64String(new byte[32])};EndpointSuffix=core.windows.net";

    private static async Task<HealthReportEntry> ReadConnectionHealthAsync(
        HealthCheckService health)
    {
        var report = await health.CheckHealthAsync(registration =>
            registration.Name == "digitalbrain-client-orleans");
        return report.Entries["digitalbrain-client-orleans"];
    }
}
