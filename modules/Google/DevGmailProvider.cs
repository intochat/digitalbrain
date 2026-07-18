using Brain.Kernel.Connections;

namespace Brain.Modules.Google;

internal sealed class DevGmailProvider : IGmailProvider, IConnectionProvider
{
    public string BuildAuthorizationUrl(string state) =>
        $"https://accounts.google.com/o/oauth2/v2/auth?state={Uri.EscapeDataString(state)}";

    public Task<ConnectionToken> ExchangeCodeAsync(string code, CancellationToken ct) =>
        Task.FromResult(new ConnectionToken(
            "development-access-token",
            "development-refresh-token",
            DateTimeOffset.UtcNow.AddHours(1)));

    public Task<ProbeResult> ProbeAsync(ConnectionToken token, CancellationToken ct) =>
        Task.FromResult(new ProbeResult(ConnectionHealth.Healthy, "development provider"));

    public Task<string> ListAsync(ConnectionToken token, int max, CancellationToken ct) =>
        Task.FromResult("""{"messages":[]}""");

    public Task<string> SendAsync(ConnectionToken token, string payloadJson, CancellationToken ct) =>
        Task.FromResult("dev-message-id");
}
