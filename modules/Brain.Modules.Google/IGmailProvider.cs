using Brain.Kernel.Connections;

namespace Brain.Modules.Google;

public interface IGmailProvider
{
    Task<string> ListAsync(ConnectionToken token, int max, CancellationToken ct);
    Task<string> SendAsync(ConnectionToken token, string payloadJson, CancellationToken ct);
}
