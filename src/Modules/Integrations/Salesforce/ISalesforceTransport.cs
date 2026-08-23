namespace DigitalBrain.Integrations.Salesforce;

public interface ISalesforceTransport
{
    Task<string> UpsertJsonAsync(string objectType, string payloadJson, CancellationToken cancellationToken);
}
