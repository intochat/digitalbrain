using Brain.Modules.Connections;
using Brain.Modules.Google;

namespace Brain.Modules.Sdk;

public sealed class FakeGmailProvider : IGmailProvider
{
    private int _listCalls;
    private int _sendCalls;

    public int ListCalls => _listCalls;
    public int SendCalls => _sendCalls;

    public string ListResult { get; set; } = """{"messages":[]}""";
    public string SendResultProviderMessageId { get; set; } = "fake-message-id";
    public Exception? ListException { get; set; }
    public Exception? SendException { get; set; }

    public void Reset()
    {
        _listCalls = 0;
        _sendCalls = 0;
        ListResult = """{"messages":[]}""";
        SendResultProviderMessageId = "fake-message-id";
        ListException = null;
        SendException = null;
    }

    public Task<string> ListAsync(ConnectionToken token, int max, CancellationToken ct)
    {
        Interlocked.Increment(ref _listCalls);
        if (ListException is { } exception)
            throw exception;
        return Task.FromResult(ListResult);
    }

    public Task<string> SendAsync(ConnectionToken token, string payloadJson, CancellationToken ct)
    {
        Interlocked.Increment(ref _sendCalls);
        if (SendException is { } exception)
            throw exception;
        return Task.FromResult(SendResultProviderMessageId);
    }
}
