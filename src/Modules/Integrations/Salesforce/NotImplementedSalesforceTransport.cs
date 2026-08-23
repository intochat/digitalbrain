namespace DigitalBrain.Integrations.Salesforce;

public sealed class NotImplementedSalesforceTransport : ISalesforceTransport
{
    public Task<string> UpsertJsonAsync(string objectType, string payloadJson, CancellationToken cancellationToken)
        => throw new NotImplementedException("Wire MCP later");
}
