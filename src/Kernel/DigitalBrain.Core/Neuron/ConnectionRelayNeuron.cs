using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

internal static class ConnectionRelay
{
    internal const string GrainTypeName = "relay";

    internal static NeuronId ForConnection(OwnerId owner, Guid connectionId)
        => new(GrainTypeName, owner, connectionId.ToString("D"));
}

[GrainType(ConnectionRelay.GrainTypeName)]
internal sealed class ConnectionRelayNeuron : Neuron
{
    protected override async Task OnUnboundSynapseAsync(Synapse synapse, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(Id.Name, out var connectionId))
        {
            throw new NeuronAuthorizationException(
                $"Relay '{Id}' is not named by a connection identity and refuses to carry synapses.");
        }

        var connection = await LiveConnectionAsync(connectionId).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)
            ?? throw new NeuronAuthorizationException(
                $"Relay '{Id}' has no live connection and refuses '{synapse.GetType().Name}'.");

        var carried = connection.Transform is not { } transformName
            ? synapse
            : Adapted(TransformFor(transformName), synapse);

        await SendAsync(connection.Target, carried).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task<SynapseConnection?> LiveConnectionAsync(Guid connectionId)
    {
        var graph = ISynapseGraph.ForOwner(Id.Owner);

        using var bound = new CancellationTokenSource(DeliveryPolicy.ConnectionLookupTimeout);
        try
        {
            return await GrainFactory
                .GetGrain<ISynapseGraph>(graph.ToGrainId())
                .ConnectionOf(connectionId)
                .WaitAsync(bound.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (bound.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Relay '{Id}' connection lookup did not answer within {DeliveryPolicy.ConnectionLookupTimeout}.");
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
