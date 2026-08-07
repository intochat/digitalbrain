using DigitalBrain.Product.Webhooks;
using DigitalBrain.Testing;

namespace DigitalBrain.Product.Tests.Webhooks;

/// <summary>
/// Covers the durable receipt boundary independently of any provider-specific trigger.
/// </summary>
public sealed class WebhookIngressTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(VerifiedWebhookDeliveryReceived).Assembly)
            .RegisterIngress<VerifiedWebhookDeliveryReceived>()
            .RegisterNeuron<WebhookIngressNeuron>(WebhookIngressNeuron.Kind);

    [Fact]
    public async Task RetainsAcceptedDeliveryDigestAcrossReactivationAndRejectsConflict()
    {
        const string scope = "workspace/webhook-durability";
        const string subscriptionId = "gmail/subscription-durable";
        const string deliveryId = "gmail-delivery-durable";
        var webhook = OpenWorkspace(scope, subscriptionId, typeof(VerifiedWebhookDeliveryReceived));
        var receiver = new NeuronId(WebhookIngressNeuron.Kind, subscriptionId);
        var accepted = new VerifiedWebhookDeliveryReceived(
            "gmail",
            subscriptionId,
            deliveryId,
            new string('a', 64));

        await webhook.Publisher.PublishAsync(accepted, Cancellation);
        _ = await WaitForJournalAsync(
            webhook,
            receiver,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(WebhookDeliveryAccepted).FullName),
            "the first accepted delivery",
            Cancellation);

        await DeactivateAsync([receiver], Cancellation);
        await webhook.Publisher.PublishAsync(accepted, Cancellation);
        _ = await WaitForJournalAsync(
            webhook,
            receiver,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(WebhookDeliveryDuplicate).FullName),
            "the duplicate delivery after reactivation",
            Cancellation);

        await webhook.Publisher.PublishAsync(
            new VerifiedWebhookDeliveryReceived(
                "gmail",
                subscriptionId,
                deliveryId,
                new string('b', 64)),
            Cancellation);
        var page = await WaitForJournalAsync(
            webhook,
            receiver,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(WebhookDeliveryConflict).FullName),
            "the conflicting delivery receipt",
            Cancellation);

        Assert.Equal(
            1,
            page.Records.Count(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(WebhookDeliveryAccepted).FullName));
    }
}
