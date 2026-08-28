namespace DigitalBrain.Integrations.Salesforce;

public interface ISalesforceTransport
{
    Task<string> QueryJsonAsync(string query, CancellationToken cancellationToken);

    Task<string> UpsertJsonAsync(string objectType, string payloadJson, CancellationToken cancellationToken);
}
