namespace DigitalBrain.SDK.Google.Auth;

public interface IGoogleAuthBroker
{
    Task<bool> HasStoredTokenAsync(
        string userAccountId, IReadOnlyCollection<string> scopes, CancellationToken ct);

    Task AuthorizeAsync(
        string userAccountId, IReadOnlyCollection<string> scopes, CancellationToken ct);
}
