using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.SDK.Stripe;

[ImplicitStreamSubscription(StripeWebhookNeuronType)]
internal sealed class StripeWebhookNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<StripeWebhookNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      IStripeWebhook,
      INeuronMetadata,
      IExternalNeuron,
      IHandle<IncomingWebhookEnvelope>
{
    [global::DigitalBrain.Runtime.Neurons.State.NeuronSetting("Stripe:WebhookSecret", isPrivate: true)]
    private string WebhookSecret { get; set; } = "";

    public const string StripeWebhookNeuronType = nameof(StripeWebhookNeuron);

    public static NeuronId Id => new("stripe/webhook");
    public static string Icon => "stripe";
    public static NeuronCapability Capabilities => NeuronCapability.External;

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        switch (synapse)
        {
            case IncomingWebhookEnvelope envelope:
                await HandleIncomingWebhookAsync(envelope);
                break;
        }
    }

    private async Task HandleIncomingWebhookAsync(IncomingWebhookEnvelope envelope)
    {
        var webhookSecret = WebhookSecret;

        try
        {
            if (!string.IsNullOrEmpty(webhookSecret))
            {
                if (string.IsNullOrEmpty(envelope.Signature))
                {
                    throw new Exception("Signature is missing");
                }

                string? t = null;
                string? v1 = null;
                foreach (var part in envelope.Signature.Split(','))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2)
                    {
                        var key = kv[0].Trim();
                        var val = kv[1].Trim();
                        if (key == "t") t = val;
                        else if (key == "v1") v1 = val;
                    }
                }

                if (string.IsNullOrEmpty(t) || string.IsNullOrEmpty(v1))
                {
                    throw new Exception("Signature is invalid: missing timestamp or hash");
                }

                var signedPayload = $"{t}.{envelope.PayloadJson}";
                using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(webhookSecret));
                var hashBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(signedPayload));
                var computedSig = Convert.ToHexString(hashBytes).ToLowerInvariant();

                if (computedSig != v1)
                {
                    throw new Exception("Signature is invalid: hash mismatch");
                }
            }

            using var doc = System.Text.Json.JsonDocument.Parse(envelope.PayloadJson);
            var root = doc.RootElement;
            var eventId = root.GetProperty("id").GetString() ?? throw new Exception("Event id is missing");
            var eventType = root.GetProperty("type").GetString() ?? throw new Exception("Event type is missing");

            // Successfully validated
            Counter("webhooks_verified").instrument.Add(1);

            await FireSynapseAsync(new WebhookVerified(EventType: eventType,
        EventId: eventId) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: envelope.CorrelationId,
            causationId: envelope.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: NeuronType,
            receiverNeuronId: envelope.CallerNeuronId,
            receiverNeuronType: envelope.CallerNeuronType ?? "External",
            timestamp: DateTimeOffset.UtcNow
        ) });

            if (eventType == "checkout.session.completed")
            {
                if (root.TryGetProperty("data", out var data) && data.TryGetProperty("object", out var obj))
                {
                    string? userId = null;
                    if (obj.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (metadata.TryGetProperty("UserId", out var uid)) userId = uid.GetString();
                        else if (metadata.TryGetProperty("user_id", out var uid2)) userId = uid2.GetString();
                    }
                    if (string.IsNullOrEmpty(userId) && obj.TryGetProperty("client_reference_id", out var crid))
                    {
                        userId = crid.GetString();
                    }

                    string? priceId = null;
                    if (obj.TryGetProperty("metadata", out var metadata2) && metadata2.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (metadata2.TryGetProperty("PriceId", out var pid)) priceId = pid.GetString();
                        else if (metadata2.TryGetProperty("price_id", out var pid2)) priceId = pid2.GetString();
                    }

                    string? subscriptionId = null;
                    if (obj.TryGetProperty("subscription", out var sub))
                    {
                        subscriptionId = sub.GetString();
                    }

                    if (!string.IsNullOrEmpty(userId))
                    {
                        Counter("subscriptions_started").instrument.Add(1);

                        await FireSynapseAsync(new SubscriptionActivated(UserId: userId,
        PriceId: priceId ?? "price_ess_m",
        SubscriptionId: subscriptionId ?? "sub_mock") { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: envelope.CorrelationId,
            causationId: envelope.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: NeuronType,
            receiverNeuronId: envelope.CallerNeuronId,
            receiverNeuronType: envelope.CallerNeuronType ?? "External",
            timestamp: DateTimeOffset.UtcNow
        ) });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stripe webhook handling failed");
            Counter("webhooks_rejected").instrument.Add(1);

            await FireSynapseAsync(new WebhookRejected(Reason: ex.Message) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: envelope.CorrelationId,
            causationId: envelope.SynapseId,
            callerNeuronId: Guid.Empty,
            callerNeuronType: NeuronType,
            receiverNeuronId: envelope.CallerNeuronId,
            receiverNeuronType: envelope.CallerNeuronType ?? "External",
            timestamp: DateTimeOffset.UtcNow
        ) });
        }
    }
}
