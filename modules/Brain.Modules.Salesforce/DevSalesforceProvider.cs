using Brain.Modules.Connections;

namespace Brain.Modules.Salesforce;

internal sealed class DevSalesforceProvider : ISalesforceProvider
{
    public Task<string> QueryAsync(ConnectionToken token, string soql, CancellationToken ct) =>
        Task.FromResult("""{"records":[]}""");

    public Task<string> UpdateAsync(ConnectionToken token, string payloadJson, CancellationToken ct) =>
        Task.FromResult("dev-record-id");
}
