namespace DigitalBrain.Integrations.Salesforce;

public sealed class FakeSalesforceTransport : ISalesforceTransport
{
    public Task<string> UpsertJsonAsync(string objectType, string payloadJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("""{"recordId":"SF1","created":true}""");
    }
}
