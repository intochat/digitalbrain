using DigitalBrain.DevTools;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DigitalBrain.Tests.DevTools;

public sealed class DigitalBrainDevUITests
{
    [Fact]
    public void Missing_explicit_owner_fails_before_agent_registration()
    {
        var builder = CreateBuilder(Environments.Development, owner: null);

        var error = Assert.Throws<InvalidOperationException>(() =>
            builder.AddDigitalBrainDevUI("brain"));

        Assert.Contains("owner", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(AIAgent));
    }

    [Fact]
    public void DevUI_discovers_exactly_the_three_owner_bound_role_agents()
    {
        var builder = CreateBuilder(Environments.Development, "owner-a");
        builder.AddDigitalBrainDevUI("brain");
        using var app = builder.Build();

        var ownerBinding = app.Services
            .GetRequiredService<DigitalBrainDevUIOwnerBinding>();
        Assert.False(ownerBinding.IsValidated);
        var agents = DigitalBrainDevUIAgentNames.All
            .Select(name => app.Services.GetRequiredKeyedService<AIAgent>(name))
            .ToArray();

        Assert.Equal(
            ["fast", "balanced", "reasoning"],
            agents.Select(agent => agent.Name!).ToArray());
        Assert.True(ownerBinding.IsValidated);
        Assert.Null(app.Services.GetRequiredService<BrainOwnerContext>().Current);
    }

    [Fact]
    public void DevUI_maps_ui_discovery_responses_and_conversations_with_shared_access_guards()
    {
        var builder = CreateBuilder(Environments.Development, "owner-a");
        builder.AddDigitalBrainDevUI("brain");
        using var app = builder.Build();

        app.MapDigitalBrainDevUI();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText == "/devui/{*path}");
        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText == "/v1/entities/");
        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText == "/v1/responses/");
        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText == "/v1/conversations/");
        Assert.All(
            endpoints.Where(endpoint =>
                endpoint.RoutePattern.RawText?.StartsWith("/devui", StringComparison.Ordinal) is true ||
                endpoint.RoutePattern.RawText?.StartsWith("/v1/entities", StringComparison.Ordinal) is true ||
                endpoint.RoutePattern.RawText?.StartsWith("/v1/responses", StringComparison.Ordinal) is true ||
                endpoint.RoutePattern.RawText?.StartsWith("/v1/conversations", StringComparison.Ordinal) is true),
            endpoint => Assert.Contains(
                endpoint.Metadata,
                metadata => metadata is DigitalBrainDevelopmentAccessMetadata
                {
                    LoopbackOnly: true
                }));
    }

    [Fact]
    public void Production_and_remote_access_are_separate_explicit_opt_ins()
    {
        var blocked = CreateBuilder(Environments.Production, "owner-a");
        Assert.Throws<InvalidOperationException>(() =>
            blocked.AddDigitalBrainDevUI("brain"));

        var production = CreateBuilder(Environments.Production, "owner-a");
        Assert.Throws<InvalidOperationException>(() =>
            production.AddDigitalBrainDevUI(
                "brain",
                options => options.AllowProduction = true));

        var authenticatedProduction = CreateBuilder(
            Environments.Production,
            "owner-a");
        authenticatedProduction.AddDigitalBrainDevUI(
            "brain",
            options =>
            {
                options.AllowProduction = true;
                options.AuthToken = "secret";
            });
        using var productionApp = authenticatedProduction.Build();
        productionApp.MapDigitalBrainDevUI();
        var metadata = ((IEndpointRouteBuilder)productionApp).DataSources
            .SelectMany(source => source.Endpoints)
            .SelectMany(endpoint => endpoint.Metadata)
            .OfType<DigitalBrainDevelopmentAccessMetadata>()
            .ToArray();
        Assert.NotEmpty(metadata);
        Assert.All(metadata, value => Assert.True(value.LoopbackOnly));

        var remoteWithoutAuth = CreateBuilder(Environments.Development, "owner-a");
        Assert.Throws<InvalidOperationException>(() =>
            remoteWithoutAuth.AddDigitalBrainDevUI(
                "brain",
                options => options.AllowRemoteAccess = true));

        var remoteWithAuthorization = CreateBuilder(
            Environments.Development,
            "owner-a");
        Assert.Throws<InvalidOperationException>(() =>
            remoteWithAuthorization.AddDigitalBrainDevUI(
                "brain",
                options =>
                {
                    options.AllowRemoteAccess = true;
                    options.ConfigureEndpoints = endpoints => endpoints.RequireAuthorization();
                }));

        var remoteWithToken = CreateBuilder(Environments.Development, "owner-a");
        remoteWithToken.AddDigitalBrainDevUI(
            "brain",
            options =>
            {
                options.AllowRemoteAccess = true;
                options.AuthToken = "secret";
            });
    }

    [Fact]
    public void DevTools_resolves_no_provider_credentials_or_kernel_storage()
    {
        var builder = CreateBuilder(Environments.Development, "owner-a");
        builder.AddDigitalBrainDevUI("brain");

        var services = string.Join(
            '\n',
            builder.Services
                .Where(descriptor =>
                    descriptor.ImplementationType?.Namespace?.StartsWith(
                        "OrleansCodeGen",
                        StringComparison.Ordinal) is not true)
                .SelectMany(descriptor => new[]
                {
                    descriptor.ServiceType.AssemblyQualifiedName,
                    descriptor.ImplementationType?.AssemblyQualifiedName,
                    descriptor.ImplementationInstance?.GetType().AssemblyQualifiedName
                })
                .Where(value => value is not null));
        Assert.DoesNotContain("OpenAIClient", services, StringComparison.Ordinal);
        Assert.DoesNotContain("Anthropic", services, StringComparison.Ordinal);
        Assert.DoesNotContain("Azure.AI.OpenAI", services, StringComparison.Ordinal);
        Assert.DoesNotContain("DigitalBrain.Kernel", services, StringComparison.Ordinal);
        Assert.DoesNotContain("Journaling", services, StringComparison.Ordinal);

        var references = typeof(DigitalBrainDevUIOptions).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();
        Assert.DoesNotContain("OpenAI", references);
        Assert.DoesNotContain("Anthropic", references);
        Assert.DoesNotContain("Azure.AI.OpenAI", references);
        Assert.DoesNotContain("DigitalBrain.Kernel", references);
    }

    private static WebApplicationBuilder CreateBuilder(
        string environmentName,
        string? owner)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:brain"] = StorageConnectionString(),
            [DigitalBrainDevUIOptions.DefaultOwnerConfigurationKey] = owner
        });
        return builder;
    }

    private static string StorageConnectionString() =>
        $"DefaultEndpointsProtocol=https;AccountName=devui;AccountKey={Convert.ToBase64String(new byte[32])};EndpointSuffix=core.windows.net";
}
