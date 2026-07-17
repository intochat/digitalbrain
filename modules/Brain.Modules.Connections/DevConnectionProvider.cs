namespace Brain.Modules.Connections;

internal sealed class DevConnectionProvider : IConnectionProvider
{
    public string BuildAuthorizationUrl(string state) => $"https://dev.local/authorize?state={state}";

    public Task<ConnectionToken> ExchangeCodeAsync(string code, CancellationToken ct) =>
        Task.FromResult(new ConnectionToken("dev-access-token", "dev-refresh-token", DateTimeOffset.UtcNow.AddHours(1)));

    public Task<ProbeResult> ProbeAsync(ConnectionToken token, CancellationToken ct) =>
        Task.FromResult(new ProbeResult(ConnectionHealth.Healthy, "dev connection provider always healthy"));
}
