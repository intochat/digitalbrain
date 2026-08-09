using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

internal static class BindingRelay
{
    internal const string GrainTypeName = "relay";

    internal static NeuronId ForBinding(OwnerId owner, Guid bindingId)
        => new(GrainTypeName, owner, bindingId.ToString("D"));
}

[GrainType(BindingRelay.GrainTypeName)]
internal sealed class BindingRelayNeuron : Neuron
{
    protected override async Task OnUnboundSynapseAsync(Synapse synapse, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(Id.Name, out var bindingId))
        {
            throw new NeuronAuthorizationException(
                $"Relay '{Id}' is not named by a binding identity and refuses to carry synapses.");
        }

        var route = await LiveRouteAsync(bindingId).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)
            ?? throw new NeuronAuthorizationException(
                $"Relay '{Id}' has no live binding and refuses '{synapse.GetType().Name}'.");

        var carried = route.Transform is not { } transformName
            ? synapse
            : Adapted(TransformFor(transformName), synapse);

        await SendAsync(route.Target, carried).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task<SynapseRoute?> LiveRouteAsync(Guid bindingId)
    {
        var graph = ISynapseGraph.ForOwner(Id.Owner);

        using var bound = new CancellationTokenSource(DeliveryPolicy.RouteLookupTimeout);
        try
        {
            return await GrainFactory
                .GetGrain<ISynapseGraph>(graph.ToGrainId())
                .RouteOf(bindingId)
                .WaitAsync(bound.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (bound.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Relay '{Id}' binding lookup did not answer within {DeliveryPolicy.RouteLookupTimeout}.");
        }
    }

    private Synapse Adapted(ISynapseTransform transform, Synapse synapse)
    {
        try
        {
            return transform.Apply(synapse);
        }
        catch (Exception failure) when (failure is not NeuronAuthorizationException)
        {
            throw new NeuronAuthorizationException(
                $"Relay '{Id}' transform '{transform.Name}' failed on '{synapse.GetType().Name}' and the delivery is refused.",
                failure);
        }
    }

    private ISynapseTransform TransformFor(string transformName)
        => ServiceProvider
            .GetServices<ISynapseTransform>()
            .FirstOrDefault(transform => string.Equals(transform.Name, transformName, StringComparison.Ordinal))
            ?? (ISynapseTransform?)DeclarativeSynapseTransform.TryParse(transformName)
            ?? throw new NeuronAuthorizationException(
                $"Relay '{Id}' has no transform named '{transformName}' and refuses to guess.");
}
