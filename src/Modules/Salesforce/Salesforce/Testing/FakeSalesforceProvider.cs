using System.Text.Json;
using System.Text.Json.Nodes;

namespace DigitalBrain.Salesforce;

internal sealed class FakeSalesforceProvider : ISalesforceProvider
{
    internal bool RejectSchema { get; set; }

    public Task<JsonElement> InvokeAsync(string tool, JsonElement arguments, string accessToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SalesforceTokenRefresh.ValidateToken(accessToken);
        var payload = tool switch
        {
            "getUserInfo" => """{"mode":"fake","user":"Salesforce fixture"}""",
            "getObjectSchema" => arguments.TryGetProperty("object-name", out var objectName)
                ? JsonSerializer.Serialize(new { objectName = objectName.GetString(), fields = new[] { new { name = "AccountId", type = "reference", referenceTo = "Account" } } })
                : """{"objects":[{"name":"Account"},{"name":"Contact"}],"relationships":[{"from":"Contact","to":"Account","field":"AccountId"}]}""",
            "soqlQuery" => """{"mode":"fake","records":[],"totalSize":0}""",
            "createRecord" or "updateRecord" => """{"mode":"fake","id":"record-intochat"}""",
            _ => throw new SalesforceUnavailableException("This Salesforce operation is not allowed."),
        };
        var result = JsonNode.Parse(payload)!.AsObject();
        // screened at the NativeTools boundary (AI module)
        result["untrustedData"] = true;
        return Task.FromResult(JsonSerializer.SerializeToElement(result));
    }

    public Task<string> ReadToolSchemaHashAsync(string tool, string accessToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SalesforceTokenRefresh.ValidateToken(accessToken);
        if (RejectSchema)
        {
            throw new SalesforceUnavailableException("The provider catalog schema is incompatible.");
        }
        return Task.FromResult(SalesforceMcpProvider.NativeTools.Contains(tool, StringComparer.Ordinal)
            ? "fake-salesforce-schema-v1" : throw new SalesforceUnavailableException("This Salesforce operation is not allowed."));
    }
}
