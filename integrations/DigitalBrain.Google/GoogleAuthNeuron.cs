using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UiContracts = DigitalBrain.Ui.Contracts;

[GrainType("digitalbrain.google.auth.v1")]
public class GoogleAuthNeuron(ILogger<GoogleAuthNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IGoogleAuthNeuron
{
    public static UiContracts.AuthButtonSurface SignInSurface() => new(
        Provider: "google",
        Label: "Connect Google",
        Icon: "gmail",
        Action: GoogleSignals.AuthRequested);

    public async Task HandleAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        if (signal.Name != GoogleSignals.AuthRequested)
        {
            return;
        }

        // Always attempt to start OAuth flow (will use seeded app config or props).
        // This supports both direct button from INO and credential form paths.
        await StartOAuthAsync(signal.Props, cancellationToken);
    }

    private async Task StartOAuthAsync(IReadOnlyDictionary<string, object?> props, CancellationToken cancellationToken)
    {
        IPackConfigStore? store = null;
        try
        {
            store = ServiceProvider.GetService<IPackConfigStore>();
        }
        catch { }

        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (store is not null)
        {
            try
            {
                var existing = await store.GetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, cancellationToken);
                values = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { }
        }

        CopyIfPresent(props, values, GoogleClientFactory.ClientIdKey);
        CopyIfPresent(props, values, GoogleClientFactory.ClientSecretKey);
        CopyIfPresent(props, values, GoogleClientFactory.RedirectUriKey);

        var configuredRedirect = ServiceProvider.GetService<IConfiguration>()?["DigitalBrain:Google:RedirectUri"];
        if (!string.IsNullOrWhiteSpace(configuredRedirect))
        {
            values[GoogleClientFactory.RedirectUriKey] = configuredRedirect.Trim();
        }
        else
        {
            CopyIfPresent(props, values, GoogleClientFactory.RedirectUriKey);
        }

        if (!values.TryGetValue(GoogleClientFactory.RedirectUriKey, out var redirectUri) || string.IsNullOrWhiteSpace(redirectUri))
        {
            redirectUri = GoogleClientFactory.DefaultRedirectUri;
            values[GoogleClientFactory.RedirectUriKey] = redirectUri;
        }

        if (!GoogleClientFactory.HasConnectedAppConfig(values) || store is null)
        {
            // No config: emit credential form (port from Salesforce) instead of empty URL.
            // User fills client id/secret via form -> saves -> re-triggers OAuth.
            var message = "Google OAuth client configuration required. Enter Client ID and Secret from Google Cloud Console.";
            var form = GoogleAuthSurfaces.CredentialForm(Self.Value, clientId: null, message);
            await FireAsync(form, cancellationToken);
            return;
        }

        var state = $"{Self.AsScope().UserId.Value}:{Guid.NewGuid():N}";

        string authUrl;
        try
        {
            authUrl = GoogleClientFactory.CreateAuthorizationUrl(values, redirectUri, state);
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning(ex, "Failed to build Google auth URL.");
            return;
        }

        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, values, cancellationToken);

        var userScope = PackConfigScopes.ForUser(Self.AsScope().UserId);
        await store.SetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, new Dictionary<string, string>
        {
            [GoogleClientFactory.OAuthStateKey] = state
        }, cancellationToken);

        await Broadcast(new Signal(GoogleSignals.AuthUrl, new Dictionary<string, object?>
        {
            ["provider"] = "google",
            ["url"] = authUrl
        }), cancellationToken);
    }

    public async Task<GoogleOAuthCallbackResult> CompleteOAuthAsync(GoogleOAuthCallback callback, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            return new GoogleOAuthCallbackResult(false, "Google login failed", $"{callback.Error}: {callback.ErrorDescription}".TrimEnd(':', ' '));
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            return new GoogleOAuthCallbackResult(false, "Google login failed", "The callback did not include an authorization code.");
        }

        var store = ServiceProvider.GetRequiredService<IPackConfigStore>();
        var userScope = PackConfigScopes.ForUser(Self.AsScope().UserId);
        var appValues = await store.GetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, cancellationToken);
        var pending = await store.GetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, cancellationToken);

        if (!pending.TryGetValue(GoogleClientFactory.OAuthStateKey, out var expectedState) || string.IsNullOrWhiteSpace(expectedState))
        {
            return new GoogleOAuthCallbackResult(false, "Google login failed", "The callback state did not match the pending login.");
        }

        if (!string.Equals(expectedState, callback.State, StringComparison.Ordinal))
        {
            return new GoogleOAuthCallbackResult(false, "Google login failed", "The callback state did not match the pending login.");
        }

        var redirectUri = appValues.TryGetValue(GoogleClientFactory.RedirectUriKey, out var stored) ? stored : callback.FallbackRedirectUri;

        var existingUser = await store.GetAsync(userScope, GoogleClientFactory.PackName, cancellationToken);

        try
        {
            var tokenValues = await GoogleClientFactory.ExchangeAuthorizationCodeAsync(appValues, callback.Code, redirectUri, cancellationToken: cancellationToken);
            var userTokenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in tokenValues)
            {
                userTokenValues[k] = v;
            }

            // Merge client id/secret from app if present
            if (appValues.TryGetValue(GoogleClientFactory.ClientIdKey, out var cid))
            {
                userTokenValues[GoogleClientFactory.ClientIdKey] = cid;
            }

            if (appValues.TryGetValue(GoogleClientFactory.ClientSecretKey, out var cs))
            {
                userTokenValues[GoogleClientFactory.ClientSecretKey] = cs;
            }

            if (!userTokenValues.TryGetValue(GoogleClientFactory.RefreshTokenKey, out var newRt) || string.IsNullOrWhiteSpace(newRt))
            {
                if (existingUser.TryGetValue(GoogleClientFactory.RefreshTokenKey, out var priorRt) && !string.IsNullOrWhiteSpace(priorRt))
                {
                    userTokenValues[GoogleClientFactory.RefreshTokenKey] = priorRt;
                }
            }

            await store.SetAsync(userScope, GoogleClientFactory.PackName, userTokenValues, cancellationToken);
            await store.SetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, new Dictionary<string, string>(), cancellationToken);

            var authedUserId = Self.AsScope().UserId.Value;
            await Broadcast(new Signal("PackConfigured", new Dictionary<string, object?>
            {
                ["pack"] = GoogleClientFactory.PackName,
                ["userId"] = authedUserId,
                ["scope"] = userScope
            }), cancellationToken);
            await Broadcast(new Signal(GoogleSignals.AuthCompleted, new Dictionary<string, object?>
            {
                ["provider"] = "google",
                ["pack"] = GoogleClientFactory.PackName,
                ["userId"] = authedUserId,
                ["scope"] = userScope
            }), cancellationToken);

            return new GoogleOAuthCallbackResult(true, "Google connected", "You can close this browser tab and return to DigitalBrain.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Google OAuth callback failed.");
            return new GoogleOAuthCallbackResult(false, "Google login failed", ex.GetBaseException().Message);
        }
    }

    private static bool IsOAuthStart(IReadOnlyDictionary<string, object?> props) =>
        HasValue(props, GoogleClientFactory.ClientIdKey) ||
        HasValue(props, GoogleClientFactory.ClientSecretKey);

    private static bool HasValue(IReadOnlyDictionary<string, object?> props, string key) =>
        props.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value?.ToString());

    private static void CopyIfPresent(
        IReadOnlyDictionary<string, object?> props,
        IDictionary<string, string> values,
        string key)
    {
        if (props.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value?.ToString()))
        {
            values[key] = value.ToString()!.Trim();
        }
    }
}
