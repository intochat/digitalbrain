using Brain.Modules.Connections;

namespace Brain.Modules.Sdk;

public sealed class FakeConnectionProvider : IConnectionProvider
{
    private int _buildAuthorizationUrlCalls;
    private int _exchangeCodeCalls;
    private int _probeCalls;

    public int BuildAuthorizationUrlCalls => _buildAuthorizationUrlCalls;
    public int ExchangeCodeCalls => _exchangeCodeCalls;
    public int ProbeCalls => _probeCalls;

    public ConnectionToken ExchangeResult { get; set; } = new("fake-access-token", "fake-refresh-token", DateTimeOffset.UtcNow.AddHours(1));
    public ProbeResult NextProbeResult { get; set; } = new(ConnectionHealth.Healthy, "ok");
    public Exception? ExchangeCodeException { get; set; }
    public Exception? ProbeException { get; set; }

    public void Reset()
    {
        _buildAuthorizationUrlCalls = 0;
        _exchangeCodeCalls = 0;
        _probeCalls = 0;
        ExchangeResult = new("fake-access-token", "fake-refresh-token", DateTimeOffset.UtcNow.AddHours(1));
        NextProbeResult = new(ConnectionHealth.Healthy, "ok");
        ExchangeCodeException = null;
        ProbeException = null;
    }

    public string BuildAuthorizationUrl(string state)
    {
        Interlocked.Increment(ref _buildAuthorizationUrlCalls);
        return $"https://fake.example/authorize?state={state}";
    }

    public Task<ConnectionToken> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        Interlocked.Increment(ref _exchangeCodeCalls);
        if (ExchangeCodeException is { } exception)
            throw exception;
        return Task.FromResult(ExchangeResult);
    }

    public Task<ProbeResult> ProbeAsync(ConnectionToken token, CancellationToken ct)
    {
        Interlocked.Increment(ref _probeCalls);
        if (ProbeException is { } exception)
            throw exception;
        return Task.FromResult(NextProbeResult);
    }
}
