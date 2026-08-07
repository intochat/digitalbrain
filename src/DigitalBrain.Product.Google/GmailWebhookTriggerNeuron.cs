using DigitalBrain.Product.Enrichment;
using DigitalBrain.Product.Webhooks;

namespace DigitalBrain.Product.Google;

/// <summary>
/// Google-specific adapter from the reusable verified-webhook module to account enrichment.
/// </summary>
public sealed class GmailWebhookTriggerNeuron(IGmailWebhookDeliveryReader reader) : Neuron<GmailWebhookTriggerState>,
    INeuron<WebhookDeliveryAccepted>,
    INeuron<WebhookDeliveryDuplicate>,
    INeuron<AccountEnrichmentRunAccepted>,
    INeuron<GmailWebhookStartDeliveryFailed>
{
    public const string Kind = "gmail-webhook-trigger";

    private readonly IGmailWebhookDeliveryReader reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public Task HandleAsync(WebhookDeliveryAccepted synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        return HandleDeliveryAsync(synapse, isDuplicate: false, cancellationToken);
    }

    public Task HandleAsync(WebhookDeliveryDuplicate synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        return HandleDeliveryAsync(
            new WebhookDeliveryAccepted(
                synapse.Provider,
                synapse.SubscriptionId,
                synapse.DeliveryId,
                synapse.CanonicalPayloadDigest),
            isDuplicate: true,
            cancellationToken);
    }

    public Task HandleAsync(AccountEnrichmentRunAccepted synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Equals(Origin.Source, new NeuronId(AccountEnrichmentNeuron.Kind, synapse.RunId)))
        {
            return Task.CompletedTask;
        }

        var state = State;
        var mapping = state.Deliveries.Values.SingleOrDefault(candidate =>
            string.Equals(candidate.Request?.RunId, synapse.RunId, StringComparison.Ordinal)
            && candidate.StartStatus is GmailWebhookStartStatus.StartOutstanding or GmailWebhookStartStatus.StartFailed);
        if (mapping is null)
        {
            return Task.CompletedTask;
        }

        Replace(state, mapping with { StartStatus = GmailWebhookStartStatus.Acknowledged });
        State = state;
        return Task.CompletedTask;
    }

    public Task HandleAsync(GmailWebhookStartDeliveryFailed synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Equals(Origin.Source, new NeuronId(GmailWebhookDeliveryFailureObserver.Kind, Id.Name))
            || !Equals(synapse.FailedStart.Source, Id)
            || !string.Equals(synapse.Receiver.Kind, AccountEnrichmentNeuron.Kind, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        var state = State;
        var mapping = state.Deliveries.Values.SingleOrDefault(candidate =>
            candidate.StartStatus == GmailWebhookStartStatus.StartOutstanding
            && string.Equals(candidate.Request?.RunId, synapse.Receiver.Name, StringComparison.Ordinal));
        if (mapping is null)
        {
            return Task.CompletedTask;
        }

        Replace(state, mapping with { StartStatus = GmailWebhookStartStatus.StartFailed });
        State = state;
        return Task.CompletedTask;
    }

    private async Task HandleDeliveryAsync(
        WebhookDeliveryAccepted delivery,
        bool isDuplicate,
        CancellationToken cancellationToken)
    {
        if (!IsTrustedGmailDelivery(delivery))
        {
            return;
        }

        var state = State;
        if (state.Deliveries.TryGetValue(delivery.DeliveryId, out var mapping))
        {
            if (!Matches(mapping, delivery)
                || !isDuplicate
                || mapping.StartStatus != GmailWebhookStartStatus.StartFailed)
            {
                return;
            }

            var retried = mapping with { StartStatus = GmailWebhookStartStatus.StartOutstanding };
            Replace(state, retried);
            State = state;
            EmitStart(retried);
            return;
        }

        var request = await reader.ReadOrReconcileAsync(delivery, cancellationToken);
        var hasMappedRun = request is not null && state.Deliveries.Values.Any(candidate =>
            string.Equals(candidate.Request?.RunId, request.RunId, StringComparison.Ordinal));
        var startStatus = request is null || hasMappedRun
            ? GmailWebhookStartStatus.Ignored
            : GmailWebhookStartStatus.StartOutstanding;

        var prepared = new GmailWebhookDeliveryMapping(
            delivery.Provider,
            delivery.SubscriptionId,
            delivery.DeliveryId,
            delivery.CanonicalPayloadDigest,
            request,
            startStatus);
        Replace(state, prepared);
        State = state;
        if (startStatus == GmailWebhookStartStatus.StartOutstanding)
        {
            EmitStart(prepared);
        }
    }

    private bool IsTrustedGmailDelivery(WebhookDeliveryAccepted delivery)
        => string.Equals(delivery.Provider, "gmail", StringComparison.Ordinal)
            && string.Equals(Id.Name, delivery.SubscriptionId, StringComparison.Ordinal)
            && Equals(Origin.Source, new NeuronId(WebhookIngressNeuron.Kind, delivery.SubscriptionId));

    private static bool Matches(GmailWebhookDeliveryMapping mapping, WebhookDeliveryAccepted delivery)
        => string.Equals(mapping.Provider, delivery.Provider, StringComparison.Ordinal)
            && string.Equals(mapping.SubscriptionId, delivery.SubscriptionId, StringComparison.Ordinal)
            && string.Equals(mapping.DeliveryId, delivery.DeliveryId, StringComparison.Ordinal)
            && string.Equals(mapping.CanonicalPayloadDigest, delivery.CanonicalPayloadDigest, StringComparison.Ordinal);

    private static void Replace(GmailWebhookTriggerState state, GmailWebhookDeliveryMapping mapping)
    {
        state.Deliveries = new Dictionary<string, GmailWebhookDeliveryMapping>(
            state.Deliveries,
            StringComparer.Ordinal)
        {
            [mapping.DeliveryId] = mapping,
        };
    }

    private void EmitStart(GmailWebhookDeliveryMapping mapping)
    {
        if (mapping.Request is not { } request)
        {
            return;
        }

        Emit(
            new AccountEnrichmentStarted(request),
            Dispatch.Direct(new NeuronId(AccountEnrichmentNeuron.Kind, request.RunId)));
    }
}
