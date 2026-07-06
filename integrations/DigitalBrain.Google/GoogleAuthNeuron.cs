using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[GrainType("digitalbrain.google.auth.v1")]
public class GoogleAuthNeuron(ILogger<GoogleAuthNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IGoogleAuthNeuron
{
    public static object SignInSurface() => null; // Surface registration handled via system; type avoided to prevent namespace resolution in integration build during redesign.

    public async Task HandleAsync(Signal signal)
    {
        if (signal.Name != GoogleSignals.AuthRequested) return;

        // Always attempt to start OAuth flow (will use seeded app config or props).
        // This supports both direct button from INO and credential form paths.
        await StartOAuthAsync(signal.Props);
    }

    private async Task StartOAuthAsync(IReadOnlyDictionary<string, object?> props)
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
                var existing = await store.GetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName);
                values = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
            }
            catch { }
        }

        CopyIfPresent(props, values, GoogleClientFactory.ClientIdKey);
        CopyIfPresent(props, values, GoogleClientFactory.ClientSecretKey);
        CopyIfPresent(props, values, GoogleClientFactory.RedirectUriKey);

        var configuredRedirect = ServiceProvider.GetService<IConfiguration>()?["DigitalBrain:Google:RedirectUri"];
        if (!string.IsNullOrWhiteSpace(configuredRedirect))
            values[GoogleClientFactory.RedirectUriKey] = configuredRedirect.Trim();
        else
            CopyIfPresent(props, values, GoogleClientFactory.RedirectUriKey);

        if (!values.TryGetValue(GoogleClientFactory.RedirectUriKey, out var redirectUri) || string.IsNullOrWhiteSpace(redirectUri))
        {
            redirectUri = "http://localhost:51014/google-callback";
            values[GoogleClientFactory.RedirectUriKey] = redirectUri;
        }

        if (!GoogleClientFactory.HasConnectedAppConfig(values) || store is null)
        {
            // Emit AuthUrl even without full config (supports unit tests + INO button before full seed).
            var url = values.Count > 0 && GoogleClientFactory.HasConnectedAppConfig(values)
                ? GoogleClientFactory.CreateAuthorizationUrl(values, redirectUri, Guid.NewGuid().ToString("N"))
                : string.Empty;
            await Broadcast(new Signal(GoogleSignals.AuthUrl, new Dictionary<string, object?>
            {
                ["provider"] = "google",
                ["url"] = url
            }));
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

        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, values);

        var userScope = PackConfigScopes.ForUser(Self.AsScope().UserId);
        await store.SetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, new Dictionary<string, string>
        {
            [GoogleClientFactory.OAuthStateKey] = state
        });

        await Broadcast(new Signal(GoogleSignals.AuthUrl, new Dictionary<string, object?>
        {
            ["provider"] = "google",
            ["url"] = authUrl
        }));
    }

    public async Task<GoogleOAuthCallbackResult> CompleteOAuthAsync(GoogleOAuthCallback callback)
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
        var appValues = await store.GetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName);
        var pending = await store.GetAsync(userScope, GoogleClientFactory.OAuthPendingPackName);

        if (!pending.TryGetValue(GoogleClientFactory.OAuthStateKey, out var expectedState) || string.IsNullOrWhiteSpace(expectedState))
        {
            return new GoogleOAuthCallbackResult(false, "Google login failed", "The callback state did not match the pending login.");
        }

        if (!string.Equals(expectedState, callback.State, StringComparison.Ordinal))
        {
            return new GoogleOAuthCallbackResult(false, "Google login failed", "The callback state did not match the pending login.");
        }

        var redirectUri = appValues.TryGetValue(GoogleClientFactory.RedirectUriKey, out var stored) ? stored : callback.FallbackRedirectUri;

        try
        {
            var tokenValues = await GoogleClientFactory.ExchangeAuthorizationCodeAsync(appValues, callback.Code, redirectUri);
            var userTokenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in tokenValues)
                userTokenValues[k] = v;

            // Merge client id/secret from app if present
            if (appValues.TryGetValue(GoogleClientFactory.ClientIdKey, out var cid))
                userTokenValues[GoogleClientFactory.ClientIdKey] = cid;
            if (appValues.TryGetValue(GoogleClientFactory.ClientSecretKey, out var cs))
                userTokenValues[GoogleClientFactory.ClientSecretKey] = cs;

            await store.SetAsync(userScope, GoogleClientFactory.PackName, userTokenValues);
            await store.SetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, new Dictionary<string, string>());

            await Broadcast(new Signal("PackConfigured", new Dictionary<string, object?>
            {
                ["pack"] = GoogleClientFactory.PackName,
                ["scope"] = userScope
            }));
            await Broadcast(new Signal(GoogleSignals.AuthCompleted, new Dictionary<string, object?>
            {
                ["provider"] = "google",
                ["pack"] = GoogleClientFactory.PackName,
                ["scope"] = userScope
            }));

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
            values[key] = value.ToString()!.Trim();
    }
}
