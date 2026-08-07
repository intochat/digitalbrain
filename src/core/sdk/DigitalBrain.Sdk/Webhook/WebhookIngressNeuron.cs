using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Sdk.Webhook;

[GrainType("webhook-ingress")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the silo from GrainType metadata.")]
internal sealed class WebhookIngressNeuron :
    Neuron,
    IHandle<VerifiedWebhookDeliveryReceived>,
    IEmit<WebhookDeliveryAccepted>,
    IEmit<WebhookDeliveryDuplicate>,
    IEmit<WebhookDeliveryConflict>
{
    private const string StateName = "sdk.webhook.ingress";

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<WebhookIngressState> _states;

    public WebhookIngressNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<WebhookIngressState>>();
    }

    public async Task HandleAsync(VerifiedWebhookDeliveryReceived synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        // Subscription identity is the grain instance name (one ingress grain per subscription).
        if (!string.Equals(Id.Name, synapse.SubscriptionId, StringComparison.Ordinal))
        {
            return;
        }

        var digests = Load();
        if (!digests.TryGetValue(synapse.DeliveryId, out var existingDigest))
        {
            digests[synapse.DeliveryId] = synapse.CanonicalPayloadDigest;
            Store(digests);
            await EmitAsync(new WebhookDeliveryAccepted(
                synapse.Provider,
                synapse.SubscriptionId,
                synapse.DeliveryId,
                synapse.CanonicalPayloadDigest)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (string.Equals(existingDigest, synapse.CanonicalPayloadDigest, StringComparison.Ordinal))
        {
            await EmitAsync(new WebhookDeliveryDuplicate(
                synapse.Provider,
                synapse.SubscriptionId,
                synapse.DeliveryId,
                synapse.CanonicalPayloadDigest)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        await EmitAsync(new WebhookDeliveryConflict(
            synapse.Provider,
            synapse.SubscriptionId,
            synapse.DeliveryId,
            synapse.CanonicalPayloadDigest)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private Dictionary<string, string> Load()
    {
        if (_state.Value is not { Length: > 0 } bytes)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var snapshot = _states.Deserialize(bytes);
        return new Dictionary<string, string>(snapshot.CanonicalPayloadDigests, StringComparer.Ordinal);
    }

    private void Store(Dictionary<string, string> digests)
    {
        _state.Value = _states.SerializeToArray(
            new WebhookIngressState(
                new Dictionary<string, string>(digests, StringComparer.Ordinal)));
    }
}

[GenerateSerializer]
[Alias("db.webhook.ingress-state")]
internal sealed record WebhookIngressState(
    [property: Id(0)] Dictionary<string, string> CanonicalPayloadDigests);
