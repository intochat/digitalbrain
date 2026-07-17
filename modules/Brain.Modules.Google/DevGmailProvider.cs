using Brain.Kernel.Connections;

namespace Brain.Modules.Google;

internal sealed class DevGmailProvider : IGmailProvider
{
    public Task<string> ListAsync(ConnectionToken token, int max, CancellationToken ct) =>
        Task.FromResult("""{"messages":[]}""");

    public Task<string> SendAsync(ConnectionToken token, string payloadJson, CancellationToken ct) =>
        Task.FromResult("dev-message-id");
}
