namespace DigitalBrain.Integrations.Salesforce;

public sealed class FakeSalesforceTransport : ISalesforceTransport
{
    public Task<string> QueryJsonAsync(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            """{"totalSize":1,"records":[{"Id":"001INTOCHAT","Name":"IntoChat","Website":"https://intochat.io","Description":"Verified customer conversation platform.","DescriptionVerified":true}]}""");
    }

    public Task<string> UpsertJsonAsync(string objectType, string payloadJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("""{"id":"001INTOCHAT","success":true,"created":false}""");
    }
}
