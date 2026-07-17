using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Brain.Contracts;
using Brain.Modules.Connections;
using Microsoft.Extensions.DependencyInjection;

namespace Brain.Modules.Google;

public sealed class GmailKind(IGrainFactory grainFactory, IServiceProvider services) : INeuronKind
{
    private const string ConnectionInstanceId = "google-primary";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Kind => "gmail";
    public string[] Contracts => ["gmail.read.v1", "gmail.propose-send.v1", "gmail.execute-send.v1"];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "gmail.read.v1" => HandleReadAsync(context, invocation.InputJson),
            "gmail.propose-send.v1" => HandleProposeSendAsync(invocation.InputJson),
            "gmail.execute-send.v1" => HandleExecuteSendAsync(context, invocation.InputJson),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    public string Project(NeuronContext context, string projection) =>
        JsonSerializer.Serialize(new
        {
            reads = context.Journal.Count(e => e.Kind == "gmail.read"),
            sent = context.Journal.Count(e => e.Kind == "gmail.sent")
        }, JsonOptions);

    private async ValueTask<KindResult> HandleReadAsync(NeuronContext context, string inputJson)
    {
        var max = ParseOptionalMax(inputJson);
        var token = await LeaseTokenAsync(context);
        var provider = RequireProvider();

        string messagesJson;
        try
        {
            messagesJson = await provider.ListAsync(token, max ?? 10, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not BrainException)
        {
            throw new BrainException(BrainErrors.ProviderError, ex.Message);
        }

        var eventPayload = JsonSerializer.Serialize(new { requestedMax = max ?? 10 }, JsonOptions);
        return new KindResult(messagesJson, [("gmail.read", eventPayload)]);
    }

    private static ValueTask<KindResult> HandleProposeSendAsync(string inputJson)
    {
        var (to, subject, body) = ParseSendRequest(inputJson);
        var payload = JsonSerializer.Serialize(new { to, subject, body }, JsonOptions);
        var digest = Sha256Hex(payload);
        var output = JsonSerializer.Serialize(new { digest }, JsonOptions);

        return ValueTask.FromResult(new KindResult(
            output,
            [("gmail.send-proposed", payload)],
            new EffectProposal("gmail", payload, digest)));
    }

    private async ValueTask<KindResult> HandleExecuteSendAsync(NeuronContext context, string inputJson)
    {
        var effectKey = RequireStringField(inputJson, "effectKey");
        var effect = grainFactory.GetGrain<INeuron>(effectKey);
        var claimReceipt = await effect.InvokeAsync(new NeuronInvocation(
            "effect.claim-proof.v1", "{}", Guid.NewGuid().ToString("N"), context.Address.ToGrainKey()));
        var proof = JsonSerializer.Deserialize<ApprovedEffectProof>(claimReceipt.OutputJson, JsonOptions)!;

        var proposed = context.Journal.LastOrDefault(e =>
            e.Kind == "gmail.send-proposed" && Sha256Hex(e.PayloadJson) == proof.PayloadDigest)
            ?? throw new BrainException("input.invalid", "no matching send proposal for claimed digest");

        var token = await LeaseTokenAsync(context);
        var provider = RequireProvider();

        string providerMessageId;
        try
        {
            providerMessageId = await provider.SendAsync(token, proposed.PayloadJson, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not BrainException)
        {
            throw new BrainException(BrainErrors.ProviderError, ex.Message);
        }

        var eventPayload = JsonSerializer.Serialize(new { digest = proof.PayloadDigest, providerMessageId }, JsonOptions);
        return new KindResult(eventPayload, [("gmail.sent", eventPayload)]);
    }

    private async ValueTask<ConnectionToken> LeaseTokenAsync(NeuronContext context)
    {
        var connectionKey = new NeuronAddress(context.Address.OwnerId, context.Address.SpaceId, $"connection/{ConnectionInstanceId}").ToGrainKey();
        var connection = grainFactory.GetGrain<INeuron>(connectionKey);
        var receipt = await connection.InvokeAsync(new NeuronInvocation(
            "connection.lease-token.v1", "{}", Guid.NewGuid().ToString("N"), context.Address.ToGrainKey()));
        return JsonSerializer.Deserialize<ConnectionToken>(receipt.OutputJson, JsonOptions)!;
    }

    private IGmailProvider RequireProvider() =>
        services.GetKeyedService<IGmailProvider>("google")
            ?? throw new BrainException(BrainErrors.ConnectionUnhealthy, "no gmail provider registered");

    private static int? ParseOptionalMax(string inputJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(inputJson);
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }

        using (doc)
        {
            var root = doc.RootElement;
            return root.TryGetProperty("max", out var element) && element.ValueKind == JsonValueKind.Number
                ? element.GetInt32()
                : null;
        }
    }

    private static (string To, string Subject, string Body) ParseSendRequest(string inputJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(inputJson);
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }

        using (doc)
        {
            var root = doc.RootElement;
            return (RequireString(root, "to"), RequireString(root, "subject"), RequireString(root, "body"));
        }
    }

    private static string RequireStringField(string inputJson, string field)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(inputJson);
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }

        using (doc)
            return RequireString(doc.RootElement, field);
    }

    private static string RequireString(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var element) || element.ValueKind != JsonValueKind.String)
            throw new BrainException("input.invalid", $"{field} field is required");

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new BrainException("input.invalid", $"{field} cannot be empty");

        return value;
    }

    private static string Sha256Hex(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
