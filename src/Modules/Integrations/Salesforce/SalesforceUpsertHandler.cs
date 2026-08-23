using System.Text.Json;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Execution;

namespace DigitalBrain.Integrations.Salesforce;

public sealed class SalesforceUpsertHandler(ISalesforceTransport transport) : ICapabilityHandler
{
    public CapabilityId Id { get; } = CapabilityId.Parse("salesforce.upsert");

    public async Task<ContextDelta> InvokeAsync(
        ExecutionId executionId,
        OwnerId owner,
        string requestJson,
        CancellationToken cancellationToken)
    {
        _ = executionId;
        _ = owner;
        var request = ParseRequest(requestJson);
        var json = await transport.UpsertJsonAsync(request.ObjectType, request.PayloadJson, cancellationToken)
            .ConfigureAwait(false);
        return new ContextDelta(
            new ContextPath("salesforce.upsert"),
            SchemaHash: "salesforce.upsert.v1",
            PayloadJson: json,
            BlobRef: null);
    }

    private static SalesforceUpsertRequest ParseRequest(string requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return SalesforceUpsertRequest.Default;
        }

        try
        {
            using var document = JsonDocument.Parse(requestJson);
            var objectType = "Lead";
            if (document.RootElement.TryGetProperty("objectType", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(typeElement.GetString()))
            {
                objectType = typeElement.GetString()!;
            }

            return new SalesforceUpsertRequest(objectType, requestJson);
        }
        catch (JsonException)
        {
            return new SalesforceUpsertRequest("Lead", requestJson);
        }
    }

    private sealed record SalesforceUpsertRequest(string ObjectType, string PayloadJson)
    {
        public static SalesforceUpsertRequest Default { get; } = new("Lead", "{}");
    }
}
