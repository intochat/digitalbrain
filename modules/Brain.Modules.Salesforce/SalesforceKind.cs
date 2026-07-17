using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Brain.Contracts;
using Brain.Modules.Connections;
using Microsoft.Extensions.DependencyInjection;

namespace Brain.Modules.Salesforce;

public sealed class SalesforceKind(IGrainFactory grainFactory, IServiceProvider services) : INeuronKind
{
    private const string ConnectionInstanceId = "salesforce-primary";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Kind => "salesforce";
    public string[] Contracts => ["salesforce.read.v1", "salesforce.propose-update.v1", "salesforce.execute-update.v1"];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "salesforce.read.v1" => HandleReadAsync(context, invocation.InputJson),
            "salesforce.propose-update.v1" => HandleProposeUpdateAsync(invocation.InputJson),
            "salesforce.execute-update.v1" => HandleExecuteUpdateAsync(context, invocation.InputJson),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    public string Project(NeuronContext context, string projection) =>
        JsonSerializer.Serialize(new
        {
            reads = context.Journal.Count(e => e.Kind == "salesforce.read"),
            updates = context.Journal.Count(e => e.Kind == "salesforce.updated")
        }, JsonOptions);

    private async ValueTask<KindResult> HandleReadAsync(NeuronContext context, string inputJson)
    {
        var query = RequireStringField(inputJson, "query");
        var token = await LeaseTokenAsync(context);
        var provider = RequireProvider();

        string resultJson;
        try
        {
            resultJson = await provider.QueryAsync(token, query, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not BrainException)
        {
            throw new BrainException(BrainErrors.ProviderError, ex.Message);
        }

        var eventPayload = JsonSerializer.Serialize(new { query }, JsonOptions);
        return new KindResult(resultJson, [("salesforce.read", eventPayload)]);
    }

    private static ValueTask<KindResult> HandleProposeUpdateAsync(string inputJson)
    {
        var (objectId, fields) = ParseUpdateRequest(inputJson);
        var payload = JsonSerializer.Serialize(new { objectId, fields }, JsonOptions);
        var digest = Sha256Hex(payload);
        var output = JsonSerializer.Serialize(new { digest }, JsonOptions);

        return ValueTask.FromResult(new KindResult(
            output,
            [("salesforce.update-proposed", payload)],
            new EffectProposal("salesforce", payload, digest)));
    }

    private async ValueTask<KindResult> HandleExecuteUpdateAsync(NeuronContext context, string inputJson)
    {
        var effectKey = RequireStringField(inputJson, "effectKey");
        if (!context.Synapses.Any(s => s.Relation == SynapseRelation.Awaits && s.TargetKey == effectKey))
            throw new BrainException(BrainErrors.EffectNotApproved, "effect was not proposed by this neuron");

        var effect = grainFactory.GetGrain<INeuron>(effectKey);
        var claimReceipt = await effect.InvokeAsync(new NeuronInvocation(
            "effect.claim-proof.v1", "{}", Guid.NewGuid().ToString("N"), context.Address.ToGrainKey()));
        var proof = JsonSerializer.Deserialize<ApprovedEffectProof>(claimReceipt.OutputJson, JsonOptions)!;

        var proposed = context.Journal.LastOrDefault(e =>
            e.Kind == "salesforce.update-proposed" && Sha256Hex(e.PayloadJson) == proof.PayloadDigest)
            ?? throw new BrainException("input.invalid", "no matching update proposal for claimed digest");

        var token = await LeaseTokenAsync(context);
        var provider = RequireProvider();

        string providerRecordId;
        try
        {
            providerRecordId = await provider.UpdateAsync(token, proposed.PayloadJson, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not BrainException)
        {
            throw new BrainException(BrainErrors.ProviderError, ex.Message);
        }

        var eventPayload = JsonSerializer.Serialize(new { digest = proof.PayloadDigest, providerRecordId }, JsonOptions);
        return new KindResult(eventPayload, [("salesforce.updated", eventPayload)]);
    }

    private async ValueTask<ConnectionToken> LeaseTokenAsync(NeuronContext context)
    {
        var connectionKey = new NeuronAddress(context.Address.OwnerId, context.Address.SpaceId, $"connection/{ConnectionInstanceId}").ToGrainKey();
        var connection = grainFactory.GetGrain<INeuron>(connectionKey);
        var receipt = await connection.InvokeAsync(new NeuronInvocation(
            "connection.lease-token.v1", "{}", Guid.NewGuid().ToString("N"), context.Address.ToGrainKey()));
        return JsonSerializer.Deserialize<ConnectionToken>(receipt.OutputJson, JsonOptions)!;
    }

    private ISalesforceProvider RequireProvider() =>
        services.GetKeyedService<ISalesforceProvider>("salesforce")
            ?? throw new BrainException(BrainErrors.ConnectionUnhealthy, "no salesforce provider registered");

    private static (string ObjectId, JsonElement Fields) ParseUpdateRequest(string inputJson)
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
            var objectId = RequireString(root, "objectId");
            if (!root.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
                throw new BrainException("input.invalid", "fields object is required");

            return (objectId, fields.Clone());
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
