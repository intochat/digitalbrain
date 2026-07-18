namespace DigitalBrain.SDK.Google.Gmail;

public interface IGmailService
{
    Task<IReadOnlyList<GmailSender>> ListRecentSendersAsync(
        string userAccountId, int n, CancellationToken ct);
}
