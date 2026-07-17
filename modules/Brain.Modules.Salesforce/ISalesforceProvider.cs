using Brain.Modules.Connections;

namespace Brain.Modules.Salesforce;

public interface ISalesforceProvider
{
    Task<string> QueryAsync(ConnectionToken token, string soql, CancellationToken ct);
    Task<string> UpdateAsync(ConnectionToken token, string payloadJson, CancellationToken ct);
}
