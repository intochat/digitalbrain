namespace DigitalBrain.Infrastructure.Connectors.V2;

public sealed class GoogleV2OAuthAdapter(string clientId, string clientSecret, string redirectUri) : IProviderOAuthAdapter
{
    public string ProviderId => "google";
    public IReadOnlyList<ConnectorCapabilityDescriptor> Capabilities { get; } = [
        new("gmail.read", 2, "google", ["https://www.googleapis.com/auth/gmail.readonly"], ConnectorRiskClass.Read, false, true, "confidential"),
        new("gmail.send", 2, "google", ["https://www.googleapis.com/auth/gmail.send"], ConnectorRiskClass.ExternalSideEffect, true, false, "confidential")];
    public bool IsAllowedRedirectUri(string redirect) => Uri.TryCreate(redirect, UriKind.Absolute, out var actual) && Uri.TryCreate(redirectUri, UriKind.Absolute, out var expected) && string.Equals(actual.AbsoluteUri, expected.AbsoluteUri, StringComparison.Ordinal);
    public Uri CreateAuthorizationUri(OAuthAuthorizationRequest request) => new($"https://accounts.google.com/o/oauth2/v2/auth?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(request.RedirectUri)}&response_type=code&access_type=offline&prompt=consent&scope={Uri.EscapeDataString(string.Join(' ', request.Scopes))}&state={Uri.EscapeDataString(request.State)}&code_challenge={Uri.EscapeDataString(request.CodeChallenge)}&code_challenge_method=S256");
    public Task<ProviderCallResult<ProviderTokenSet>> ExchangeAsync(OAuthExchangeRequest request, CancellationToken cancellationToken) => Task.FromResult(ProviderCallResult<ProviderTokenSet>.Permanent("live Google exchange requires configured HTTP adapter"));
    public Task<ProviderCallResult<ProviderTokenSet>> RefreshAsync(OAuthRefreshRequest request, CancellationToken cancellationToken) => Task.FromResult(ProviderCallResult<ProviderTokenSet>.Permanent("live Google refresh requires configured HTTP adapter"));
    public Task<ProviderCallResult<bool>> RevokeAsync(OAuthRevokeRequest request, CancellationToken cancellationToken) => Task.FromResult(ProviderCallResult<bool>.Permanent("live Google revoke requires configured HTTP adapter"));
}

public sealed class SalesforceV2OAuthAdapter(string clientId, string clientSecret, string loginUrl, string redirectUri) : IProviderOAuthAdapter
{
    public string ProviderId => "salesforce";
    public IReadOnlyList<ConnectorCapabilityDescriptor> Capabilities { get; } = [new("salesforce.read", 2, "salesforce", ["api", "refresh_token"], ConnectorRiskClass.Read, false, true, "confidential")];
    public bool IsAllowedRedirectUri(string redirect) => string.Equals(redirect, redirectUri, StringComparison.Ordinal);
    public Uri CreateAuthorizationUri(OAuthAuthorizationRequest request) => new($"{loginUrl.TrimEnd('/')}/services/oauth2/authorize?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(request.RedirectUri)}&response_type=code&scope={Uri.EscapeDataString(string.Join(' ', request.Scopes))}&state={Uri.EscapeDataString(request.State)}&code_challenge={Uri.EscapeDataString(request.CodeChallenge)}&code_challenge_method=S256");
    public Task<ProviderCallResult<ProviderTokenSet>> ExchangeAsync(OAuthExchangeRequest request, CancellationToken cancellationToken) => Task.FromResult(ProviderCallResult<ProviderTokenSet>.Permanent("live Salesforce exchange requires configured HTTP adapter"));
    public Task<ProviderCallResult<ProviderTokenSet>> RefreshAsync(OAuthRefreshRequest request, CancellationToken cancellationToken) => Task.FromResult(ProviderCallResult<ProviderTokenSet>.Permanent("live Salesforce refresh requires configured HTTP adapter"));
    public Task<ProviderCallResult<bool>> RevokeAsync(OAuthRevokeRequest request, CancellationToken cancellationToken) => Task.FromResult(ProviderCallResult<bool>.Permanent("live Salesforce revoke requires configured HTTP adapter"));
}
