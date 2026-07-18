using DigitalBrain.Core.Runtime;
using DigitalBrain.Core.Synapses;
using DigitalBrain.Abstractions.Bundles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Kernel.Bundles;

// Announces each installed bundle on the global timeline once the silo is active, so the boot/install
// path is observable on the same tape as every other substrate event. Registered after Orleans so it
// starts once the cluster client can resolve the synapse stream provider.
public sealed class BundleInstallationAnnouncer(
    IReadOnlyList<BundleInstallation> installations,
    IClusterClient clusterClient,
    ILogger<BundleInstallationAnnouncer> logger,
    SynapseStreamOptions? streamOptions = null) : IHostedService
{
    private readonly SynapseStreamOptions _streamOptions = streamOptions ?? SynapseStreamOptions.Default;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var installation in installations.Where(installation => installation.Installed))
        {
            await SynapseTimelinePublisher.PublishAsync(
                clusterClient,
                new BundleInstalled(installation.BundleId.Value),
                _streamOptions,
                logger,
                cancellationToken);
            logger.LogInformation("Installed bundle {BundleId}.", installation.BundleId.Value);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
