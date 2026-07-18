using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Dashboard;

namespace DigitalBrain.DevTools;

public static class DigitalBrainDashboardExtensions
{
    public static IHostApplicationBuilder AddDigitalBrainDashboard(
        this IHostApplicationBuilder builder,
        string name,
        Action<DigitalBrainDashboardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var options = CreateOptions(
            builder.Environment,
            configure,
            requireProductionAuthentication: true);
        builder.AddDigitalBrainClient(name);
        builder.UseOrleansClient(client => client.AddDashboard(dashboard =>
        {
            dashboard.HideTrace = options.HideTrace;
            dashboard.CounterUpdateIntervalMs = options.CounterUpdateIntervalMs;
            dashboard.HistoryLength = options.HistoryLength;
        }));
        builder.Services.AddSingleton(new DigitalBrainDashboardRegistration(options));
        return builder;
    }

    public static IHostApplicationBuilder AddDigitalBrainDashboardSilo(
        this IHostApplicationBuilder builder,
        Action<DigitalBrainDashboardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = CreateOptions(
            builder.Environment,
            configure,
            requireProductionAuthentication: false);
        builder.UseOrleans(silo => silo.AddDashboard(dashboard =>
        {
            dashboard.HideTrace = options.HideTrace;
            dashboard.CounterUpdateIntervalMs = options.CounterUpdateIntervalMs;
            dashboard.HistoryLength = options.HistoryLength;
        }));
        return builder;
    }

    public static IEndpointRouteBuilder MapDigitalBrainDashboard(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider
            .GetRequiredService<DigitalBrainDashboardRegistration>()
            .Options;
        var mapped = endpoints.MapOrleansDashboard(options.RoutePrefix);
        ApplyAccessControl(mapped, options);
        return endpoints;
    }

    private static DigitalBrainDashboardOptions CreateOptions(
        IHostEnvironment environment,
        Action<DigitalBrainDashboardOptions>? configure,
        bool requireProductionAuthentication)
    {
        var options = new DigitalBrainDashboardOptions();
        configure?.Invoke(options);
        if (!environment.IsDevelopment() && !options.AllowProduction)
            throw new InvalidOperationException(
                "DigitalBrain Dashboard is disabled outside Development unless production access is explicitly enabled.");
        if (!environment.IsDevelopment() &&
            requireProductionAuthentication &&
            string.IsNullOrWhiteSpace(ResolveAuthToken(options.AuthToken)))
            throw new InvalidOperationException(
                "Production DigitalBrain Dashboard access requires an authentication token.");
        if (string.IsNullOrWhiteSpace(options.RoutePrefix) ||
            !options.RoutePrefix.StartsWith("/", StringComparison.Ordinal) ||
            options.RoutePrefix == "/")
            throw new InvalidOperationException(
                "DigitalBrain Dashboard requires a non-root absolute route prefix.");
        if (options.CounterUpdateIntervalMs < 1000)
            throw new InvalidOperationException(
                "DigitalBrain Dashboard counter updates cannot be more frequent than once per second.");
        if (options.HistoryLength <= 0)
            throw new InvalidOperationException(
                "DigitalBrain Dashboard history length must be positive.");
        if (options.AllowRemoteAccess &&
            string.IsNullOrWhiteSpace(ResolveAuthToken(options.AuthToken)))
            throw new InvalidOperationException(
                "Remote DigitalBrain Dashboard access requires an authentication token.");
        return options;
    }

    private static void ApplyAccessControl(
        RouteGroupBuilder mapped,
        DigitalBrainDashboardOptions options)
    {
        var authToken = ResolveAuthToken(options.AuthToken);
        mapped.AddEndpointFilter(
            new DigitalBrainDevelopmentAccessFilter(options.AllowRemoteAccess, authToken));
        mapped.WithMetadata(new DigitalBrainDevelopmentAccessMetadata(
            !options.AllowRemoteAccess,
            !string.IsNullOrWhiteSpace(authToken)));
        options.ConfigureEndpoints?.Invoke(mapped);
    }

    private static string? ResolveAuthToken(string? configuredToken) =>
        string.IsNullOrWhiteSpace(configuredToken)
            ? Environment.GetEnvironmentVariable("DEVUI_AUTH_TOKEN")
            : configuredToken;

    private sealed record DigitalBrainDashboardRegistration(
        DigitalBrainDashboardOptions Options);
}
