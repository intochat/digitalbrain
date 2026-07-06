using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Kernel.Salesforce;

using DigitalBrain.Ui.Contracts;

[GrainType("digitalbrain.salesforce.auth.v1")]
public class SalesforceAuthNeuron(ILogger<SalesforceAuthNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), ISalesforceAuthNeuron
{
    public static AuthButtonSurface SignInSurface() => new(
        Provider: "salesforce",
        Label: "Connect Salesforce",
        Icon: "salesforce",
        Action: SalesforceSignals.AuthRequested);

    public async Task HandleAsync(Signal signal)
    {
        if (signal.Name != SalesforceSignals.AuthRequested)
            return;

        if (IsOAuthStart(signal.Props))
        {
            await StartOAuthAsync(signal.Props);
            return;
        }

        var clientId = signal.Props.TryGetValue("clientId", out var value) ? value?.ToString() : null;
        var surface = SalesforceAuthSurfaces.CredentialForm(Self.Value, clientId);

        await FireAsync(surface);
        if (ServiceProvider.GetService<HomeFeedBus>() is { } bus)
        {
            await bus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value));
        }
    }

    private async Task StartOAuthAsync(IReadOnlyDictionary<string, object?> props)
    {
        var store = ServiceProvider.GetRequiredService<IPackConfigStore>();
        var existing = await store.GetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName);
        var values = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

        CopyIfPresent(props, values, SalesforceClientFactory.ClientIdKey);
        CopyIfPresent(props, values, SalesforceClientFactory.ClientSecretKey);
        CopyIfPresent(props, values, SalesforceClientFactory.LoginUrlKey);
        CopyIfPresent(props, values, SalesforceClientFactory.ApiVersionKey);
        CopyIfPresent(props, values, SalesforceClientFactory.OAuthScopeKey);

        var configuredRedirectUri = ServiceProvider
            .GetService<IConfiguration>()?["DigitalBrain:Salesforce:RedirectUri"];
        if (!string.IsNullOrWhiteSpace(configuredRedirectUri))
            values[SalesforceClientFactory.RedirectUriKey] = configuredRedirectUri.Trim();
        else
            CopyIfPresent(props, values, SalesforceClientFactory.RedirectUriKey);

        if (!values.TryGetValue(SalesforceClientFactory.RedirectUriKey, out var redirectUri) ||
            string.IsNullOrWhiteSpace(redirectUri))
        {
            redirectUri = SalesforceClientFactory.DefaultRedirectUri;
            values[SalesforceClientFactory.RedirectUriKey] = redirectUri;
        }

        if (!SalesforceClientFactory.HasConnectedAppConfig(values))
        {
            await PublishCredentialFormAsync(props, SalesforceClientFactory.MissingConnectedAppConfigMessage);
            return;
        }

        var state = $"{Self.AsScope().UserId.Value}:{Guid.NewGuid():N}";
        var codeVerifier = SalesforceClientFactory.CreatePkceCodeVerifier();
        var codeChallenge = SalesforceClientFactory.CreatePkceCodeChallenge(codeVerifier);

        string url;
        try
        {
            url = SalesforceClientFactory.CreateAuthorizationUrl(values, redirectUri, state, codeChallenge);
        }
        catch (InvalidOperationException ex)
        {
            await PublishCredentialFormAsync(props, ex.Message);
            return;
        }

        await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, values);

        // Pending PKCE state lives under the caller's OWN per-user scope (I3/I4): each user's grain activation
        // is the single writer of its own pending slot, so two users starting OAuth concurrently never clobber
        // each other (the pre-S3 clobbering race this comment used to describe was between config-form writes and
        // OAuth-start writes to the SAME shared slot; per-user scoping removes that shared slot entirely).
        var userScope = PackConfigScopes.ForUser(Self.AsScope().UserId);
        await store.SetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, new Dictionary<string, string>
        {
            [SalesforceClientFactory.OAuthStateKey] = state,
            [SalesforceClientFactory.OAuthCodeVerifierKey] = codeVerifier
        });
        await Broadcast(new Signal(SalesforceSignals.AuthUrl, new Dictionary<string, object?>
        {
            ["provider"] = "salesforce",
            ["url"] = url
        }));
    }

    public async Task<SalesforceOAuthCallbackResult> CompleteOAuthAsync(SalesforceOAuthCallback callback)
    {
        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            return new SalesforceOAuthCallbackResult(
                false,
                "Salesforce login failed",
                $"{callback.Error}: {callback.ErrorDescription}".TrimEnd(':', ' '));
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            return new SalesforceOAuthCallbackResult(
                false,
                "Salesforce login failed",
                "The callback did not include an authorization code.");
        }

        var store = ServiceProvider.GetRequiredService<IPackConfigStore>();
        var userScope = PackConfigScopes.ForUser(Self.AsScope().UserId);
        var appValues = await store.GetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName);
        var pending = await store.GetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName);

        // No pending flow at all (e.g. the "salesforce-auth-unknown" routing sentinel, or any per-user
        // grain that never started a flow) must reject immediately rather than fall through to a token
        // exchange that has no PKCE code_verifier to present — an explicit CSRF gate, not an incidental
        // failure that only happens to occur because the real Salesforce token endpoint would reject it.
        if (!pending.TryGetValue(SalesforceClientFactory.OAuthStateKey, out var expectedState) ||
            string.IsNullOrWhiteSpace(expectedState))
        {
            return new SalesforceOAuthCallbackResult(
                false,
                "Salesforce login failed",
                "The callback state did not match the pending login.");
        }

        if (!string.Equals(expectedState, callback.State, StringComparison.Ordinal))
        {
            return new SalesforceOAuthCallbackResult(
                false,
                "Salesforce login failed",
                "The callback state did not match the pending login.");
        }

        var redirectUri = appValues.TryGetValue(SalesforceClientFactory.RedirectUriKey, out var storedRedirectUri)
            ? storedRedirectUri
            : callback.FallbackRedirectUri;

        try
        {
            var exchangeValues = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
            if (pending.TryGetValue(SalesforceClientFactory.OAuthCodeVerifierKey, out var pendingCodeVerifier))
                exchangeValues[SalesforceClientFactory.OAuthCodeVerifierKey] = pendingCodeVerifier;

            var handler = ServiceProvider.GetService<HttpMessageHandler>();
            var tokenValues = await SalesforceClientFactory.ExchangeAuthorizationCodeAsync(exchangeValues, callback.Code, redirectUri, handler);
            var userTokenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in tokenValues)
                userTokenValues[key] = value;

            await store.SetAsync(userScope, SalesforceClientFactory.PackName, userTokenValues);
            await store.SetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, new Dictionary<string, string>());

            await Broadcast(new Signal("PackConfigured", new Dictionary<string, object?>
            {
                ["pack"] = SalesforceClientFactory.PackName,
                ["scope"] = userScope
            }));
            await Broadcast(new Signal(SalesforceSignals.AuthCompleted, new Dictionary<string, object?>
            {
                ["provider"] = "salesforce",
                ["pack"] = SalesforceClientFactory.PackName,
                ["scope"] = userScope
            }));

            return new SalesforceOAuthCallbackResult(
                true,
                "Salesforce connected",
                "You can close this browser tab and return to DigitalBrain.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Salesforce OAuth callback failed.");
            return new SalesforceOAuthCallbackResult(false, "Salesforce login failed", ex.GetBaseException().Message);
        }
    }

    private async Task PublishCredentialFormAsync(IReadOnlyDictionary<string, object?> props, string message)
    {
        var clientId = props.TryGetValue("clientId", out var value) ? value?.ToString() : null;
        var surface = SalesforceAuthSurfaces.CredentialForm(Self.Value, clientId, message);

        await FireAsync(surface);
        if (ServiceProvider.GetService<HomeFeedBus>() is { } bus)
        {
            await bus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value));
        }
    }

    private static bool IsOAuthStart(IReadOnlyDictionary<string, object?> props) =>
        HasValue(props, SalesforceClientFactory.ClientIdKey) ||
        HasValue(props, SalesforceClientFactory.ClientSecretKey) ||
        HasValue(props, SalesforceClientFactory.RedirectUriKey) ||
        HasValue(props, "callbackPath");

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
