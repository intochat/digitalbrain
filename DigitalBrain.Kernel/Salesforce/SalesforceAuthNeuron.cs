using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Kernel.Salesforce;

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

        var sessionId = signal.Props.TryGetValue("sessionId", out var value) ? value?.ToString() : null;
        var surface = SalesforceAuthSurfaces.CredentialForm(Self.Value, sessionId);

        await FireAsync(surface);
        ServiceProvider.GetService<HomeFeedBus>()?.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value));
    }

    private async Task StartOAuthAsync(IReadOnlyDictionary<string, object?> props)
    {
        var store = ServiceProvider.GetRequiredService<IPackConfigStore>();
        var existing = await store.GetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.PackName);
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

        var state = Guid.NewGuid().ToString("N");
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

        await store.SetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.PackName, values);

        // Pending PKCE state lives in its own pack, isolated from the credentials blob above. That blob
        // is also written by the credentials form and SalesforceAppConfigSeeder; sharing one slot meant a
        // concurrent write built from a stale snapshot could silently clobber the in-flight (state,
        // code_verifier) pair before the callback read it back, producing an intermittent
        // "invalid code verifier" failure from Salesforce.
        await store.SetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.OAuthPendingPackName, new Dictionary<string, string>
        {
            [SalesforceClientFactory.OAuthStateKey] = state,
            [SalesforceClientFactory.OAuthCodeVerifierKey] = codeVerifier
        });
        logger.LogWarning(
            "DIAG StartOAuthAsync stored pending state={StatePrefix}... verifierLen={VerifierLen} verifierPrefix={VerifierPrefix}...",
            state[..8], codeVerifier.Length, codeVerifier[..8]);

        var readBack = await store.GetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.OAuthPendingPackName);
        logger.LogWarning(
            "DIAG StartOAuthAsync immediate read-back keys=[{Keys}] hasState={HasState} hasVerifier={HasVerifier}",
            string.Join(",", readBack.Keys),
            readBack.ContainsKey(SalesforceClientFactory.OAuthStateKey),
            readBack.ContainsKey(SalesforceClientFactory.OAuthCodeVerifierKey));

        await Broadcast(new Signal(SalesforceSignals.AuthUrl, new Dictionary<string, object?>
        {
            ["provider"] = "salesforce",
            ["url"] = url
        }));
    }

    private async Task PublishCredentialFormAsync(IReadOnlyDictionary<string, object?> props, string message)
    {
        var sessionId = props.TryGetValue("sessionId", out var value) ? value?.ToString() : null;
        var surface = SalesforceAuthSurfaces.CredentialForm(Self.Value, sessionId, message);

        await FireAsync(surface);
        ServiceProvider.GetService<HomeFeedBus>()?.Broadcast(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value));
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
