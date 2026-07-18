namespace DigitalBrain.Google;

public sealed class FakeGmailMcpClient : IGmailMcpClient
{
    private readonly List<string> _callOrder = [];
    private int _listCalls;
    private int _sendCalls;

    public IReadOnlyList<string> CallOrder => _callOrder;
    public int ListCalls => _listCalls;
    public int SendCalls => _sendCalls;
    public GmailMessageListResult ListResult { get; set; } = new(0, "empty");
    public GmailSendResult SendResult { get; set; } = new("fake-message-id");
    public Exception? ListException { get; set; }
    public Exception? SendException { get; set; }
    public Action? OnList { get; set; }
    public Action? OnSend { get; set; }

    public Task<GmailMessageListResult> ListMessagesAsync(string query, int maxResults, CancellationToken cancellationToken = default)
    {
        _callOrder.Add("provider.list");
        Interlocked.Increment(ref _listCalls);
        OnList?.Invoke();
        if (ListException is { } exception)
            throw exception;
        return Task.FromResult(ListResult);
    }

    public Task<GmailSendResult> SendMessageAsync(
        string to,
        string subject,
        string body,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        _callOrder.Add("provider.send");
        Interlocked.Increment(ref _sendCalls);
        OnSend?.Invoke();
        if (SendException is { } exception)
            throw exception;
        return Task.FromResult(SendResult);
    }

    public void Reset()
    {
        _callOrder.Clear();
        _listCalls = 0;
        _sendCalls = 0;
        ListResult = new(0, "empty");
        SendResult = new("fake-message-id");
        ListException = null;
        SendException = null;
        OnList = null;
        OnSend = null;
    }
}
