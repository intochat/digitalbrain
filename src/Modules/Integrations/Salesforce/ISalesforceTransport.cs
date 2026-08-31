namespace DigitalBrain.Integrations.Salesforce;

public interface ISalesforceTransport
{
    Task<string> GetUserInfoJsonAsync(CancellationToken cancellationToken);

    Task<string> QueryJsonAsync(string query, CancellationToken cancellationToken);

    Task<string> UpsertJsonAsync(string objectType, string payloadJson, CancellationToken cancellationToken);
}
