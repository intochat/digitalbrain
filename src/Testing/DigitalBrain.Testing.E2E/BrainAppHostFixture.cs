using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using DigitalBrain.Abstractions;
using DigitalBrain.Aspire;
using DigitalBrain.Client;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Xunit;

namespace DigitalBrain.Testing.E2E;

public class BrainAppHostFixture<TAppHost> : IAsyncLifetime
    where TAppHost : class
{
    private const int DiagnosticLogLineCount = 40;

    private ResourceLogCollector? _logCollector;
    private IHost? _scriptHost;
    private IGrainFactory? _grains;

    public DistributedApplication App { get; private set; } = null!;

    public virtual BrainE2EOptions Configure() => new();

    public async ValueTask InitializeAsync()
    {
        var options = Configure();
        var appBuilder = await DistributedApplicationTestingBuilder
            .CreateAsync<TAppHost>(options.Args)
            .ConfigureAwait(false);

        StubParameters(appBuilder);
        IsolateContainers(appBuilder);
        RandomizeProxiedPorts(appBuilder);
        ArmExplicitStart(appBuilder, options.ExplicitStart);
        ArmBrainTestMode(appBuilder);

        App = await appBuilder.BuildAsync().ConfigureAwait(false);
        _logCollector = new ResourceLogCollector(App.Services.GetRequiredService<ResourceLoggerService>(), options.ExpectedHealthy);
        await App.StartAsync().ConfigureAwait(false);

        await WaitForExpectedHealthyAsync(options).ConfigureAwait(false);

        _scriptHost = await ConnectScriptHostAsync().ConfigureAwait(false);
        _grains = _scriptHost.Services.GetRequiredService<IGrainFactory>();
    }

    public async ValueTask DisposeAsync()
    {
        if (_logCollector is not null)
        {
            await _logCollector.DisposeAsync().ConfigureAwait(false);
        }

        if (_scriptHost is not null)
        {
            await _scriptHost.StopAsync().ConfigureAwait(false);
            _scriptHost.Dispose();
        }

        if (App is not null)
        {
            await App.DisposeAsync().ConfigureAwait(false);
        }
    }

    public IDigitalBrain BrainFor(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (_grains is null)
        {
            throw new InvalidOperationException(
                $"{nameof(BrainFor)} was called before {nameof(InitializeAsync)} completed.");
        }

        return DigitalBrainClient.Connect(_grains, owner);
    }

    public async Task<BrainSession> OpenSessionAsync()
    {
        var ownerHex = Guid.NewGuid().ToString("N")[..8];
        var brain = BrainFor($"e2e-{ownerHex}");
        await brain.ActivateAsync().ConfigureAwait(false);
        return new BrainSession(brain);
    }

    public HttpClient CreateHttpClient(string resource, string? endpointName = null)
        => App.CreateHttpClient(resource, endpointName);

    private static void StubParameters(IDistributedApplicationTestingBuilder appBuilder)
    {
        foreach (var parameter in appBuilder.Resources.OfType<ParameterResource>())
        {
            appBuilder.Configuration[$"Parameters:{parameter.Name}"] = "test";
        }
    }

    // TripRadar's container-isolation pattern: never reuse a container or its volume state
    // across test runs. WithLifetime(Session) already performs the annotation replace.
    private static void IsolateContainers(IDistributedApplicationTestingBuilder appBuilder)
    {
        foreach (var container in appBuilder.Resources.OfType<ContainerResource>())
        {
            appBuilder.CreateResourceBuilder(container).WithLifetime(ContainerLifetime.Session);

            if (container.TryGetContainerMounts(out var mounts))
            {
                foreach (var mount in mounts.ToList())
                {
                    container.Annotations.Remove(mount);
                }
            }
        }
    }

    // The IsProxied guard is what spares the kernel's unproxied HTTP endpoint (port 5080);
    // callers reach resources through App.CreateHttpClient, never the literal port.
    private static void RandomizeProxiedPorts(IDistributedApplicationTestingBuilder appBuilder)
    {
        foreach (var resource in appBuilder.Resources)
        {
            if (!resource.TryGetEndpoints(out var endpoints))
            {
                continue;
            }

            foreach (var endpoint in endpoints)
            {
                if (endpoint.IsProxied && endpoint.Port is not null)
                {
                    endpoint.Port = null;
                }
            }
        }
    }

    private static void ArmExplicitStart(IDistributedApplicationTestingBuilder appBuilder, IReadOnlyList<string> explicitStart)
    {
        foreach (var resourceName in explicitStart)
        {
            if (appBuilder.Resources.TryGetByName(resourceName, out var resource))
            {
                appBuilder.CreateResourceBuilder(resource).WithExplicitStart();
            }
        }
    }

    private static void ArmBrainTestMode(IDistributedApplicationTestingBuilder appBuilder)
    {
        foreach (var project in appBuilder.Resources.OfType<ProjectResource>())
        {
            appBuilder.CreateResourceBuilder(project).WithBrainTestMode();
        }
    }

    private async Task WaitForExpectedHealthyAsync(BrainE2EOptions options)
    {
        var healthWaits = options.ExpectedHealthy
            .Select(resourceName => App.ResourceNotifications
                .WaitForResourceHealthyAsync(resourceName)
                .WaitAsync(options.HealthTimeout))
            .ToArray();

        try
        {
            await Task.WhenAll(healthWaits).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new TimeoutException(DescribeUnhealthyResources(options.ExpectedHealthy), ex);
        }
    }

    private string DescribeUnhealthyResources(IReadOnlyList<string> expectedHealthy)
    {
        var report = new StringBuilder()
            .AppendLine($"Not every expected resource became healthy: {string.Join(", ", expectedHealthy)}.");

        foreach (var resourceName in expectedHealthy)
        {
            report.AppendLine($"--- {resourceName} ---");

            if (App.ResourceNotifications.TryGetCurrentState(resourceName, out var resourceEvent))
            {
                var snapshot = resourceEvent.Snapshot;
                report.AppendLine(
                    $"State={snapshot.State?.Text ?? "(none)"} "
                    + $"ExitCode={snapshot.ExitCode?.ToString() ?? "(none)"} "
                    + $"HealthStatus={snapshot.HealthStatus?.ToString() ?? "(none)"}");
            }
            else
            {
                report.AppendLine("No resource state has been published yet.");
            }

            var lastLines = _logCollector?.LastLines(resourceName, DiagnosticLogLineCount) ?? [];
            if (lastLines.Count == 0)
            {
                report.AppendLine("(no logs captured)");
            }
            else
            {
                foreach (var line in lastLines)
                {
                    report.AppendLine(line);
                }
            }
        }

        return report.ToString();
    }

    // DigitalBrainClient.ConnectAsync(args) can't be reused as-is: it binds one owner per host
    // and hands back an IDigitalBrain, not the shared IGrainFactory BrainFor needs for many
    // owners. This mirrors its host-construction shell, reusing RequireStorage and
    // AddDigitalBrainClient so the Orleans wiring itself stays in one place.
    private async Task<IHost> ConnectScriptHostAsync()
    {
        var clustering = await App.GetConnectionStringAsync(DigitalBrainNames.Clustering).ConfigureAwait(false);
        var streams = await App.GetConnectionStringAsync(DigitalBrainNames.Streams).ConfigureAwait(false);

        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Configuration[$"ConnectionStrings:{DigitalBrainNames.Clustering}"] = clustering;
        hostBuilder.Configuration[$"ConnectionStrings:{DigitalBrainNames.Streams}"] = streams;

        var storage = DigitalBrainScriptHost.RequireStorage(hostBuilder.Configuration);
        hostBuilder.Configuration[$"ConnectionStrings:{DigitalBrainNames.Streams}"] = storage.Streams;
        hostBuilder.AddDigitalBrainClient(activateOnStart: false);

        var host = hostBuilder.Build();
        await host.StartAsync().ConfigureAwait(false);
        return host;
    }
}
