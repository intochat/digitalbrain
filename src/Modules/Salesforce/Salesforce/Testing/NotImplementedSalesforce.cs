namespace DigitalBrain.Salesforce;

internal sealed class NotImplementedSalesforce : ISalesforce
{
    public Task<string> GetUserInfoJsonAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Salesforce hosted MCP is not configured. Supply its endpoint and access token.");

    public Task<string> QueryJsonAsync(string query, CancellationToken cancellationToken)
        => throw new NotImplementedException("Wire Salesforce MCP first");

    public Task<string> UpsertJsonAsync(string objectType, string payloadJson, CancellationToken cancellationToken)
        => throw new NotImplementedException("Wire MCP later");
}
