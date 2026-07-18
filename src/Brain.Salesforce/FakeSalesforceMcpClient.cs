namespace DigitalBrain.Salesforce;

public sealed class FakeSalesforceMcpClient : ISalesforceMcpClient
{
    private readonly List<string> _callOrder = [];
    private int _queryCalls;
    private int _updateCalls;

    public IReadOnlyList<string> CallOrder => _callOrder;
    public int QueryCalls => _queryCalls;
    public int UpdateCalls => _updateCalls;
    public SalesforceQueryResult QueryResult { get; set; } = new(0, "empty");
    public SalesforceUpdateResult UpdateResult { get; set; } = new("fake-record-id");
    public Exception? QueryException { get; set; }
    public Exception? UpdateException { get; set; }
    public Action? OnQuery { get; set; }
    public Action? OnUpdate { get; set; }

    public Task<SalesforceQueryResult> QueryRecordsAsync(string soql, CancellationToken cancellationToken = default)
    {
        _callOrder.Add("provider.query");
        Interlocked.Increment(ref _queryCalls);
        OnQuery?.Invoke();
        if (QueryException is { } exception)
            throw exception;
        return Task.FromResult(QueryResult);
    }

    public Task<SalesforceUpdateResult> UpdateRecordAsync(
        string objectType,
        string recordId,
        IReadOnlyDictionary<string, string> fields,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        _callOrder.Add("provider.update");
        Interlocked.Increment(ref _updateCalls);
        OnUpdate?.Invoke();
        if (UpdateException is { } exception)
            throw exception;
        return Task.FromResult(UpdateResult);
    }

    public void Reset()
    {
        _callOrder.Clear();
        _queryCalls = 0;
        _updateCalls = 0;
        QueryResult = new(0, "empty");
        UpdateResult = new("fake-record-id");
        QueryException = null;
        UpdateException = null;
        OnQuery = null;
        OnUpdate = null;
    }
}
