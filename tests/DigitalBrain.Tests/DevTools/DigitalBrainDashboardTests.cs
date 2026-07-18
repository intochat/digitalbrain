using System.Net;
using DigitalBrain.DevTools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DigitalBrain.Tests.DevTools;

public sealed class DigitalBrainDashboardTests
{
    [Fact]
    public void Standalone_dashboard_registers_the_official_restricted_client()
    {
        var builder = CreateWebBuilder(Environments.Development);

        builder.AddDigitalBrainDashboard("brain");
        using var app = builder.Build();

        Assert.NotNull(app.Services.GetRequiredService<IClusterClient>());
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType.FullName?.Contains(
                "Orleans.Dashboard",
                StringComparison.Ordinal) is true);
    }

    [Fact]
    public void Dashboard_maps_the_official_routes_under_a_non_root_prefix()
    {
        var builder = CreateWebBuilder(Environments.Development);
        builder.AddDigitalBrainDashboard("brain");
        using var app = builder.Build();

        app.MapDigitalBrainDashboard();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        Assert.Contains(endpoints, endpoint => endpoint.RoutePattern.RawText == "/dashboard/");
        Assert.Contains(endpoints, endpoint => endpoint.RoutePattern.RawText == "/dashboard/version");
        Assert.All(
            endpoints.Where(endpoint =>
                endpoint.RoutePattern.RawText?.StartsWith("/dashboard", StringComparison.Ordinal) is true),
            endpoint => Assert.Contains(
                endpoint.Metadata,
                metadata => metadata is DigitalBrainDevelopmentAccessMetadata
                {
                    LoopbackOnly: true
                }));
    }

    [Fact]
    public void Dashboard_silo_registration_adds_the_official_cluster_half()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });

        builder.AddDigitalBrainDashboardSilo();

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType.FullName?.Contains(
                "Dashboard",
                StringComparison.Ordinal) is true);
    }

    [Fact]
    public void Production_requires_an_explicit_opt_in_and_remains_loopback_by_default()
    {
        var blocked = CreateWebBuilder(Environments.Production);

        Assert.Throws<InvalidOperationException>(() =>
            blocked.AddDigitalBrainDashboard("brain"));

        var missingToken = CreateWebBuilder(Environments.Production);
        Assert.Throws<InvalidOperationException>(() =>
            missingToken.AddDigitalBrainDashboard(
                "brain",
                options => options.AllowProduction = true));

        var allowed = CreateWebBuilder(Environments.Production);
        allowed.AddDigitalBrainDashboard(
            "brain",
            options =>
            {
                options.AllowProduction = true;
                options.AuthToken = "secret";
            });
        using var app = allowed.Build();
        app.MapDigitalBrainDashboard();

        var metadata = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .SelectMany(endpoint => endpoint.Metadata)
            .OfType<DigitalBrainDevelopmentAccessMetadata>()
            .ToArray();
        Assert.NotEmpty(metadata);
        Assert.All(metadata, value => Assert.True(value.LoopbackOnly));
    }

    [Fact]
    public async Task Dashboard_access_filter_rejects_remote_callers()
    {
        var filter = new DigitalBrainDevelopmentAccessFilter(
            allowRemoteAccess: false,
            authToken: null);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        var invocation = EndpointFilterInvocationContext.Create(http);

        var result = await filter.InvokeAsync(
            invocation,
            _ => ValueTask.FromResult<object?>("allowed"));

        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }

    [Fact]
    public async Task Forwarded_callers_never_qualify_as_loopback()
    {
        var filter = new DigitalBrainDevelopmentAccessFilter(
            allowRemoteAccess: false,
            authToken: null);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Loopback;
        http.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
        var invocation = EndpointFilterInvocationContext.Create(http);

        var result = await filter.InvokeAsync(
            invocation,
            _ => ValueTask.FromResult<object?>("allowed"));

        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }

    [Fact]
    public void Remote_access_requires_a_token_even_with_endpoint_authorization()
    {
        var blocked = CreateWebBuilder(Environments.Development);
        Assert.Throws<InvalidOperationException>(() =>
            blocked.AddDigitalBrainDashboard(
                "brain",
                options => options.AllowRemoteAccess = true));

        var authorized = CreateWebBuilder(Environments.Development);
        Assert.Throws<InvalidOperationException>(() =>
            authorized.AddDigitalBrainDashboard(
                "brain",
                options =>
                {
                    options.AllowRemoteAccess = true;
                    options.ConfigureEndpoints = endpoints => endpoints.RequireAuthorization();
                }));

        var tokenProtected = CreateWebBuilder(Environments.Development);
        tokenProtected.AddDigitalBrainDashboard(
            "brain",
            options =>
            {
                options.AllowRemoteAccess = true;
                options.AuthToken = "secret";
            });
    }

    [Fact]
    public async Task Remote_access_with_a_token_rejects_missing_credentials()
    {
        var filter = new DigitalBrainDevelopmentAccessFilter(
            allowRemoteAccess: true,
            authToken: "secret");
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        var invocation = EndpointFilterInvocationContext.Create(http);

        var result = await filter.InvokeAsync(
            invocation,
            _ => ValueTask.FromResult<object?>("allowed"));

        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, status.StatusCode);

        http.Request.Headers.Authorization = "Bearer secret";
        var allowed = await filter.InvokeAsync(
            invocation,
            _ => ValueTask.FromResult<object?>("allowed"));
        Assert.Equal("allowed", allowed);
    }

    private static WebApplicationBuilder CreateWebBuilder(string environmentName)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:brain"] = StorageConnectionString()
        });
        return builder;
    }

    private static string StorageConnectionString() =>
        $"DefaultEndpointsProtocol=https;AccountName=devtools;AccountKey={Convert.ToBase64String(new byte[32])};EndpointSuffix=core.windows.net";
}
