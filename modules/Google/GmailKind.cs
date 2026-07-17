using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Brain.Contracts;
using Brain.Kernel.Connections;
using Google.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Brain.Modules.Google;

public sealed class GmailKind(
    IGrainFactory grainFactory,
    IServiceProvider services) : INeuronKind
{
    private const string ConnectionInstanceId = "google-primary";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Kind => "gmail";
    public string[] Contracts =>
    [
        "gmail.read.v1",
        "gmail.propose-send.v1",
        "gmail.execute-send.v1",
        GoogleCapabilityIds.GmailMailboxRead,
        GoogleCapabilityIds.GmailMessageRead,
        GoogleCapabilityIds.GmailSendPropose,
        GoogleCapabilityIds.GmailSendExecute
    ];

    public ValueTask<KindResult> InvokeAsync(
        NeuronContext context,
        NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "gmail.read.v1" => HandleLegacyReadAsync(context, invocation.InputJson),
            "gmail.propose-send.v1" => HandleLegacyProposeSendAsync(invocation.InputJson),
            "gmail.execute-send.v1" => HandleExecuteSendAsync(context, invocation.InputJson),
            GoogleCapabilityIds.GmailMailboxRead => HandleReadMailboxAsync(context, invocation.InputJson),
            GoogleCapabilityIds.GmailMessageRead => HandleReadMessageAsync(context, invocation.InputJson),
            GoogleCapabilityIds.GmailSendPropose => HandleProposeSendAsync(invocation.InputJson),
            GoogleCapabilityIds.GmailSendExecute => HandleExecuteSendAsync(context, invocation.InputJson),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    public string Project(NeuronContext context, string projection) =>
        JsonSerializer.Serialize(new
        {
            reads = context.Journal.Count(entry =>
                entry.Kind is "gmail.read" or "gmail.mailbox-read" or "gmail.message-read"),
            sent = context.Journal.Count(entry => entry.Kind == "gmail.sent"),
            deliveryUnknown = context.Journal.Count(entry => entry.Kind == "gmail.delivery-unknown")
        }, JsonOptions);

    private async ValueTask<KindResult> HandleLegacyReadAsync(
        NeuronContext context,
        string inputJson)
    {
        var max = ParseOptionalMax(inputJson);
        var token = await LeaseTokenAsync(context);
        var provider = RequireProvider();

        string messagesJson;
        try
        {
            messagesJson = await provider.ListAsync(token, max ?? 10, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not BrainException)
        {
            throw new BrainException(BrainErrors.ProviderError, "Google mailbox read failed.");
        }

        var eventPayload = JsonSerializer.Serialize(new { requestedMax = max ?? 10 }, JsonOptions);
        return new KindResult(messagesJson, [("gmail.read", eventPayload)]);
    }

    private async ValueTask<KindResult> HandleReadMailboxAsync(
        NeuronContext context,
        string inputJson)
    {
        var request = DeserializeInput<GmailMailboxReadRequest>(inputJson);
        var token = await LeaseTokenAsync(context);
        var page = await RequireProvider().ReadMailboxAsync(token, request, CancellationToken.None);
        var eventPayload = JsonSerializer.Serialize(new
        {
            requestedLimit = request.Limit,
            returned = page.Messages.Count,
            hasContinuation = page.ContinuationToken is not null
        }, JsonOptions);
        return new KindResult(
            JsonSerializer.Serialize(page, JsonOptions),
            [("gmail.mailbox-read", eventPayload)]);
    }

    private async ValueTask<KindResult> HandleReadMessageAsync(
        NeuronContext context,
        string inputJson)
    {
        var request = DeserializeInput<GmailMessageReadRequest>(inputJson);
        var token = await LeaseTokenAsync(context);
        var message = await RequireProvider().ReadMessageAsync(token, request, CancellationToken.None);
        var eventPayload = JsonSerializer.Serialize(new
        {
            messageId = message.MessageId,
            receivedAt = message.ReceivedAt
        }, JsonOptions);
        return new KindResult(
            JsonSerializer.Serialize(message, JsonOptions),
            [("gmail.message-read", eventPayload)]);
    }

    private static ValueTask<KindResult> HandleLegacyProposeSendAsync(string inputJson)
    {
        var (recipient, subject, body) = ParseLegacySendRequest(inputJson);
        return Propose(new GmailSendProposal(
            recipient,
            subject,
            body,
            $"legacy-{Sha256Hex(inputJson)}"));
    }

    private static ValueTask<KindResult> HandleProposeSendAsync(string inputJson)
    {
        var request = DeserializeInput<GmailSendProposalRequest>(inputJson);
        return Propose(new GmailSendProposal(
            request.Recipient,
            request.Subject,
            request.Body,
            request.LogicalOperationKey));
    }

    private static ValueTask<KindResult> Propose(GmailSendProposal proposal)
    {
        var payload = JsonSerializer.Serialize(proposal, JsonOptions);
        var digest = Sha256Hex(payload);
        return ValueTask.FromResult(new KindResult(
            payload,
            [("gmail.send-proposed", payload)],
            new EffectProposal("gmail", payload, digest)));
    }

    private async ValueTask<KindResult> HandleExecuteSendAsync(
        NeuronContext context,
        string inputJson)
    {
        var effectKey = DeserializeInput<GmailSendExecutionRequest>(inputJson).EffectKey;
        if (!context.Synapses.Any(synapse =>
                synapse.Relation == SynapseRelation.Awaits &&
                synapse.TargetKey == effectKey))
            throw new BrainException(
                BrainErrors.EffectNotApproved,
                "effect was not proposed by this neuron");

        var effect = grainFactory.GetGrain<INeuron>(effectKey);
        var claimReceipt = await effect.InvokeAsync(new NeuronInvocation(
            "effect.claim-proof.v1",
            "{}",
            Guid.NewGuid().ToString("N"),
            context.Address.ToGrainKey()));
        var proof = JsonSerializer.Deserialize<ApprovedEffectProof>(
            claimReceipt.OutputJson,
            JsonOptions)!;
        if (!string.Equals(proof.EffectKey, effectKey, StringComparison.Ordinal))
            throw new BrainException(BrainErrors.EffectNotApproved, "effect proof does not match");

        var proposed = context.Journal.LastOrDefault(entry =>
            entry.Kind == "gmail.send-proposed" &&
            Sha256Hex(entry.PayloadJson) == proof.PayloadDigest)
            ?? throw new BrainException(
                "input.invalid",
                "no matching send proposal for claimed digest");
        var proposal = DeserializeInput<GmailSendProposal>(proposed.PayloadJson);
        var token = await LeaseTokenAsync(context);

        string providerMessageId;
        try
        {
            providerMessageId = await RequireProvider().SendAsync(
                token,
                proposal,
                CancellationToken.None);
        }
        catch (TimeoutException)
        {
            return DeliveryUnknown(proposal.LogicalOperationKey, proof.PayloadDigest);
        }
        catch (BrainException exception) when (exception.Code == BrainErrors.ProviderTimeout)
        {
            return DeliveryUnknown(proposal.LogicalOperationKey, proof.PayloadDigest);
        }
        catch (Exception exception) when (exception is not BrainException)
        {
            throw new BrainException(BrainErrors.ProviderError, "Google message send failed.");
        }

        var output = JsonSerializer.Serialize(
            new GmailSendResult(proof.PayloadDigest, providerMessageId),
            JsonOptions);
        var eventPayload = JsonSerializer.Serialize(new { providerMessageId }, JsonOptions);
        return new KindResult(output, [("gmail.sent", eventPayload)]);
    }

    private static KindResult DeliveryUnknown(string logicalOperationKey, string digest)
    {
        var output = JsonSerializer.Serialize(
            new GmailSendResult(digest, "delivery-unknown"),
            JsonOptions);
        var eventPayload = JsonSerializer.Serialize(
            new { logicalOperationKey, status = "delivery-unknown" },
            JsonOptions);
        return new KindResult(output, [("gmail.delivery-unknown", eventPayload)]);
    }

    private async ValueTask<ConnectionToken> LeaseTokenAsync(NeuronContext context)
    {
        var connectionKey = new NeuronAddress(
            context.Address.OwnerId,
            context.Address.SpaceId,
            $"connection/{ConnectionInstanceId}").ToGrainKey();
        var connection = grainFactory.GetGrain<INeuron>(connectionKey);
        var receipt = await connection.InvokeAsync(new NeuronInvocation(
            "connection.lease-token.v1",
            "{}",
            Guid.NewGuid().ToString("N"),
            context.Address.ToGrainKey()));
        return JsonSerializer.Deserialize<ConnectionToken>(
            receipt.OutputJson,
            JsonOptions)!;
    }

    private IGmailProvider RequireProvider() =>
        services.GetKeyedService<IGmailProvider>("google")
            ?? throw new BrainException(
                BrainErrors.ConnectionUnhealthy,
                "no Gmail provider registered");

    private static T DeserializeInput<T>(string inputJson)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(inputJson, JsonOptions)
                ?? throw new JsonException("JSON value was null.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new BrainException("input.invalid", "invalid Gmail request");
        }
    }

    private static int? ParseOptionalMax(string inputJson)
    {
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            var root = document.RootElement;
            return root.TryGetProperty("max", out var element) &&
                element.ValueKind == JsonValueKind.Number
                    ? element.GetInt32()
                    : null;
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }
    }

    private static (string Recipient, string Subject, string Body) ParseLegacySendRequest(
        string inputJson)
    {
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            var root = document.RootElement;
            return (
                RequireString(root, "to"),
                RequireString(root, "subject"),
                RequireString(root, "body"));
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }
    }

    private static string RequireString(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var element) ||
            element.ValueKind != JsonValueKind.String)
            throw new BrainException("input.invalid", $"{field} field is required");

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new BrainException("input.invalid", $"{field} cannot be empty");

        return value;
    }

    private static string Sha256Hex(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
