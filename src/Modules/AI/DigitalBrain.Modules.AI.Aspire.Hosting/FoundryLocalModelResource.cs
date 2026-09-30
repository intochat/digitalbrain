using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.AI.Aspire.Hosting;

public sealed class FoundryLocalModelResource(string name, string modelId, IResource parent)
    : Resource(name), IResourceWithParent, IResourceWithWaitSupport
{
    public string ModelId { get; } = modelId;

    public IResource Parent { get; } = parent;

    internal bool IsReady { get; set; }

    internal int DownloadStarted;
}

internal static class FoundryLocalModelResourceExtensions
{
    internal static IResourceBuilder<FoundryLocalModelResource> AddFoundryLocalModel(
        this IDistributedApplicationBuilder builder,
        string name,
        string modelId,
        IResource parent)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(parent);

        var resource = new FoundryLocalModelResource(name, modelId, parent);
        var healthCheckName = $"{name}-ready";
        builder.Services.AddHealthChecks().Add(new HealthCheckRegistration(
            healthCheckName,
            _ => new FoundryLocalModelHealthCheck(resource),
            failureStatus: null,
            tags: null));

        builder.Services.AddHostedService(provider => new FoundryLocalModelDownloadService(
            resource,
            provider.GetRequiredService<ResourceNotificationService>(),
            provider.GetRequiredService<ResourceLoggerService>()));

        return builder.AddResource(resource)
            .ExcludeFromManifest()
            .WithParentRelationship(parent)
            .WithIconName("mic")
            .WithHealthCheck(healthCheckName)
            .OnInitializeResource(async (model, @event, cancellationToken) =>
            {
                var notification = @event.Services.GetRequiredService<ResourceNotificationService>();
                var log = @event.Services.GetRequiredService<ResourceLoggerService>().GetLogger(model);
                await FoundryLocalModelDownloadService.DownloadAsync(model, notification, log, cancellationToken)
                    .ConfigureAwait(false);
            })
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "FoundryLocalModel",
                CreationTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.NotStarted,
                Properties =
                [
                    new(CustomResourceKnownProperties.Source, modelId),
                ],
            });
    }
}

file sealed class FoundryLocalModelHealthCheck(FoundryLocalModelResource resource) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(
            resource.IsReady
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"{resource.ModelId} is still downloading."));
}

file sealed class FoundryLocalModelDownloadService(
    FoundryLocalModelResource resource,
    ResourceNotificationService notification,
    ResourceLoggerService loggers) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var log = loggers.GetLogger(resource);
        _ = DownloadAsync(resource, notification, log, cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static async Task DownloadAsync(
        FoundryLocalModelResource resource,
        ResourceNotificationService notification,
        ILogger log,
        CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref resource.DownloadStarted, 1) != 0)
        {
            return;
        }

        try
        {
            await notification.PublishUpdateAsync(resource, static state => state with
            {
                StartTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Starting,
            }).ConfigureAwait(false);

            await EnsureManager(log, cancellationToken).ConfigureAwait(false);
            var manager = FoundryLocalManager.Instance;

            await PublishState(notification, resource, "Registering execution providers", KnownResourceStateStyles.Info)
                .ConfigureAwait(false);
            await manager.DownloadAndRegisterEpsAsync(cancellationToken).ConfigureAwait(false);

            var catalog = await manager.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
            var model = await catalog.GetModelAsync(resource.ModelId).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Foundry Local catalog has no model '{resource.ModelId}'.");

            var lastPercent = -1;
            await model.DownloadAsync(progress =>
            {
                var percent = (int)progress;
                if (percent == lastPercent)
                {
                    return;
                }

                lastPercent = percent;
                log.LogInformation("Downloading {ModelId}: {Percent}%", model.Id, percent);
                _ = PublishState(
                    notification,
                    resource,
                    $"Downloading {percent}%",
                    KnownResourceStateStyles.Info);
            }).ConfigureAwait(false);

            resource.IsReady = true;
            await notification.PublishUpdateAsync(resource, static state => state with
            {
                State = KnownResourceStates.Running,
            }).ConfigureAwait(false);
            log.LogInformation("Foundry model cached: {ModelId}", model.Id);
        }
        catch (Exception exception)
        {
            log.LogError(exception, "Foundry model {ModelId} failed to download", resource.ModelId);
            await notification.PublishUpdateAsync(resource, static state => state with
            {
                State = KnownResourceStates.FailedToStart,
            }).ConfigureAwait(false);
        }
    }

    private static Task PublishState(
        ResourceNotificationService notification,
        FoundryLocalModelResource resource,
        string state,
        string style)
        => notification.PublishUpdateAsync(
            resource,
            snapshot => snapshot with { State = new ResourceStateSnapshot(state, style) });

    private static async Task EnsureManager(ILogger log, CancellationToken cancellationToken)
    {
        try
        {
            _ = FoundryLocalManager.Instance;
        }
        catch (FoundryLocalException)
        {
            log.LogInformation("Creating Foundry Local manager…");
            await FoundryLocalManager.CreateAsync(
                new Configuration { AppName = "digitalbrain" },
                log).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
