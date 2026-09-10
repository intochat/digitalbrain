using System.Text.Json;

namespace DigitalBrain.Salesforce;

internal sealed class FakeSalesforceProvider : ISalesforceProvider
{
    public Task<JsonElement> InvokeAsync(string tool, JsonElement arguments, string accessToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SalesforceTokenRefresh.ValidateToken(accessToken);
        var payload = tool switch
        {
            "getUserInfo" => """{"mode":"fake","user":"Salesforce fixture"}""",
            "soqlQuery" => """{"mode":"fake","records":[],"totalSize":0}""",
            "createRecord" or "updateRecord" => """{"mode":"fake","id":"record-intochat"}""",
            _ => throw new SalesforceUnavailableException("This Salesforce operation is not allowed."),
        };
        using var document = JsonDocument.Parse(payload);
        return Task.FromResult(document.RootElement.Clone());
    }

    public Task<string> ReadToolSchemaHashAsync(string tool, string accessToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SalesforceTokenRefresh.ValidateToken(accessToken);
        return Task.FromResult(SalesforceMcpProvider.NativeTools.Contains(tool, StringComparer.Ordinal)
            ? "fake-salesforce-schema-v1" : throw new SalesforceUnavailableException("This Salesforce operation is not allowed."));
    }
}
