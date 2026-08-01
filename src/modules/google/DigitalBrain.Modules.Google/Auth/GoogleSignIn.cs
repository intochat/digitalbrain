using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util;
using Google.Apis.Util.Store;

namespace DigitalBrain.Google.Auth;

internal sealed class GoogleSignIn : IAsyncDisposable, IDisposable
{
    private readonly GoogleAuthorizationCodeFlow _flow;
    private bool _disposed;

    private GoogleSignIn(GoogleAuthorizationCodeFlow flow)
    {
        _flow = flow;
    }

    public static Uri BuildAuthorizeUrl(
        string clientId,
        string redirectUri,
        IEnumerable<string> scopes,
        string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        using var flow = new GoogleAuthorizationCodeFlow(new GoogleOAuthFlowInitializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = "unused-for-authorize-url",
            },
            Scopes = scopes,
        });

        var request = (GoogleAuthorizationCodeRequestUrl)flow.CreateAuthorizationCodeRequest(redirectUri);
        request.State = state;
        return request.Build();
    }

    public static GoogleSignIn Create(
        string clientId,
        string clientSecret,
        IEnumerable<string> scopes,
        IDataStore dataStore,
        string? tokenServerUrl = null,
        IClock? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(dataStore);

        var initializer = tokenServerUrl is null
            ? new GoogleOAuthFlowInitializer()
            : new GoogleOAuthFlowInitializer(
                GoogleAuthConsts.OidcAuthorizationUrl,
                tokenServerUrl,
                GoogleAuthConsts.RevokeTokenUrl);

        initializer.ClientSecrets = new ClientSecrets
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
        };
        initializer.Scopes = scopes;
        initializer.DataStore = dataStore;
        if (clock is not null)
        {
            initializer.Clock = clock;
        }

        return new GoogleSignIn(new GoogleAuthorizationCodeFlow(initializer));
    }

    public Task<TokenResponse> ExchangeAsync(
        string userKey,
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        cancellationToken.ThrowIfCancellationRequested();

        return _flow.ExchangeCodeForTokenAsync(userKey, code, redirectUri, cancellationToken);
    }

    public async Task<GmailService> CreateServiceAsync(
        string userKey,
        CancellationToken cancellationToken,
        string? baseUri = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);
        cancellationToken.ThrowIfCancellationRequested();

        var token = await _flow.LoadTokenAsync(userKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No stored Google token for user '{userKey}'.");

        var credential = new UserCredential(_flow, userKey, token);
        var initializer = new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "DigitalBrain",
        };
        if (!string.IsNullOrWhiteSpace(baseUri))
        {
            initializer.BaseUri = baseUri;
        }

        return new GmailService(initializer);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _flow.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
