using Brain.Modules.Connections;
using Brain.Modules.Salesforce;

namespace Brain.Modules.Sdk;

public sealed class FakeSalesforceProvider : ISalesforceProvider
{
    private int _queryCalls;
    private int _updateCalls;

    public int QueryCalls => _queryCalls;
    public int UpdateCalls => _updateCalls;

    public string QueryResult { get; set; } = """{"records":[]}""";
    public string UpdateResultProviderRecordId { get; set; } = "fake-record-id";
    public Exception? QueryException { get; set; }
    public Exception? UpdateException { get; set; }

    public void Reset()
    {
        _queryCalls = 0;
        _updateCalls = 0;
        QueryResult = """{"records":[]}""";
        UpdateResultProviderRecordId = "fake-record-id";
        QueryException = null;
        UpdateException = null;
    }

    public Task<string> QueryAsync(ConnectionToken token, string soql, CancellationToken ct)
    {
        Interlocked.Increment(ref _queryCalls);
        if (QueryException is { } exception)
            throw exception;
        return Task.FromResult(QueryResult);
    }

    public Task<string> UpdateAsync(ConnectionToken token, string payloadJson, CancellationToken ct)
    {
        Interlocked.Increment(ref _updateCalls);
        if (UpdateException is { } exception)
            throw exception;
        return Task.FromResult(UpdateResultProviderRecordId);
    }
}
