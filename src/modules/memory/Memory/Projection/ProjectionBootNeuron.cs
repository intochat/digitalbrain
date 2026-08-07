using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Memory;

[GrainType("memory-projection-boot")]
internal sealed class ProjectionBootNeuron :
    Neuron,
    IHandle<DigitalBrainActivated>,
    IEmit<VectorProjectionReconciled>
{
    public async Task HandleAsync(DigitalBrainActivated synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (synapse.Owner != Id.Owner)
        {
            return;
        }

        var catalog = ServiceProvider.GetService<ActiveCapabilityCatalog>();
        var reconciler = ServiceProvider.GetService<ProjectionReconciler>();
        if (catalog is null || reconciler is null)
        {
            return;
        }

        var capabilities = await reconciler.ReconcileAsync(
            Id.Owner.Value,
            VectorMemoryNamespace.Capabilities,
            CapabilityProjection.FromCatalog(catalog),
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await EmitAsync(capabilities).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var behaviors = await reconciler.ReconcileAsync(
            Id.Owner.Value,
            VectorMemoryNamespace.Behaviors,
            BehaviorProjection.FromActiveCatalog(catalog),
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await EmitAsync(behaviors).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
