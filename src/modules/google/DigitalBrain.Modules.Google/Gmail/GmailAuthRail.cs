using DigitalBrain.Abstractions;
using DigitalBrain.Google.Auth;
using DigitalBrain.Mcp;
using DigitalBrain.Security;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Google;

internal static class GmailAuthRail
{
    internal const string ServerKey = "google.gmail";
    internal const string ServerDisplayName = "DigitalBrain Gmail";
    internal const string ReadonlyScope = "https://www.googleapis.com/auth/gmail.readonly";
    internal const string PendingStatesName = "google.gmail.oauth.pending";

    internal static async Task EnsureAuthorizedAsync(
        IGrainFactory grains,
        OwnerId owner,
        IServiceProvider services,
        CommandId commandId,
        IDurableValue<byte[]> tokenState,
        IDurableDictionary<Guid, string> pendingStates,
        Func<ValueTask> commit,
        string durableIdentity,
        string userKey,
        CancellationToken cancellationToken,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tokenState);
        ArgumentNullException.ThrowIfNull(pendingStates);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var configuration = services.GetRequiredService<IConfiguration>();
        var protector = services.GetRequiredService<IDurablePayloadProtector>();
        var store = new DurableGoogleTokenStore(
            tokenState,
            commit,
            protector,
            DurableGoogleTokenStore.Purpose(ServerKey, durableIdentity));
        var authorization = grains.GetGrain<IMcpAuthorization>(
            NeuronId.For<IMcpAuthorization>(owner, McpAuthorizationNeuron.InstanceName).ToGrainId());

        McpAuthorizationClaim? claim = null;
        try
        {
            claim = await authorization.Claim(commandId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }

        if (claim is not null)
        {
            switch (claim.Kind)
            {
                case McpAuthorizationClaimKind.Required:
                    throw new McpAuthorizationRequiredException(
                        claim.Required
                        ?? throw new InvalidOperationException("Authorization claim is missing the required fact."));
                case McpAuthorizationClaimKind.Denied:
                    throw new McpAuthorizationDeniedException(
                        claim.Denied
                        ?? throw new InvalidOperationException("Authorization claim is missing the denied fact."));
                case McpAuthorizationClaimKind.Completed:
                    await ExchangeCompletedAsync(
                        authorization,
                        configuration,
                        store,
                        pendingStates,
                        commandId,
                        userKey,
                        commit,
                        ResolveClock(services, time),
                        cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    throw new InvalidOperationException($"Authorization claim kind '{claim.Kind}' is unsupported.");
            }
        }

        if (await HasUsableRefreshTokenAsync(store, userKey).ConfigureAwait(false))
        {
            return;
        }

        var oauth = GoogleOAuthOptions.Read(configuration);
        var state = Guid.NewGuid().ToString("N");
        pendingStates[commandId.Value] = state;
        await commit().ConfigureAwait(false);

        var signInUrl = GoogleSignIn.BuildAuthorizeUrl(
            oauth.ClientId,
            oauth.RedirectUri.AbsoluteUri,
            [ReadonlyScope],
            state);
        var required = await authorization.Begin(
            new BeginMcpAuthorization(
                commandId,
                ServerKey,
                ServerDisplayName,
                signInUrl,
                state),
            cancellationToken).ConfigureAwait(false);
        throw new McpAuthorizationRequiredException(required);
    }

    internal static async Task<GoogleSignIn> CreateSignInAsync(
        IServiceProvider services,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string durableIdentity,
        CancellationToken cancellationToken,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tokenState);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableIdentity);
        cancellationToken.ThrowIfCancellationRequested();

        var configuration = services.GetRequiredService<IConfiguration>();
        var oauth = GoogleOAuthOptions.Read(configuration);
        var protector = services.GetRequiredService<IDurablePayloadProtector>();
        var store = new DurableGoogleTokenStore(
            tokenState,
            commit,
            protector,
            DurableGoogleTokenStore.Purpose(ServerKey, durableIdentity));
        var tokenServerUrl = configuration[$"{GoogleOAuthOptions.ConfigurationRoot}:TokenServerUrl"];
        return GoogleSignIn.Create(
            oauth.ClientId,
            oauth.ClientSecret,
            [ReadonlyScope],
            store,
            string.IsNullOrWhiteSpace(tokenServerUrl) ? null : tokenServerUrl,
            ResolveClock(services, time));
    }

    private static TimeProviderClock? ResolveClock(IServiceProvider services, TimeProvider? preferredTime)
    {
        var time = preferredTime
            ?? services.GetService<TimeProvider>();
        return time is null ? null : new TimeProviderClock(time);
    }

    private sealed class TimeProviderClock(TimeProvider time) : IClock
    {
        public DateTime Now => time.GetLocalNow().DateTime;

        public DateTime UtcNow => time.GetUtcNow().UtcDateTime;
    }

    private static async Task ExchangeCompletedAsync(
        IMcpAuthorization authorization,
        IConfiguration configuration,
        DurableGoogleTokenStore store,
        IDurableDictionary<Guid, string> pendingStates,
        CommandId commandId,
        string userKey,
        Func<ValueTask> commit,
        IClock? clock,
        CancellationToken cancellationToken)
    {
        if (!pendingStates.TryGetValue(commandId.Value, out var state) || string.IsNullOrWhiteSpace(state))
        {
            throw new InvalidOperationException(
                "Gmail authorization completed without a stored OAuth state for the command.");
        }

        var code = await authorization.TakeCompletedCode(state, cancellationToken).ConfigureAwait(false);
        if (code is null || string.IsNullOrWhiteSpace(code.Code))
        {
            throw new InvalidOperationException(
                "Gmail authorization completed without a deliverable authorization code.");
        }

        var oauth = GoogleOAuthOptions.Read(configuration);
        var tokenServerUrl = configuration[$"{GoogleOAuthOptions.ConfigurationRoot}:TokenServerUrl"];
        await using var signIn = GoogleSignIn.Create(
            oauth.ClientId,
            oauth.ClientSecret,
            [ReadonlyScope],
            store,
            string.IsNullOrWhiteSpace(tokenServerUrl) ? null : tokenServerUrl,
            clock);
        await signIn.ExchangeAsync(
            userKey,
            code.Code,
            oauth.RedirectUri.AbsoluteUri,
            cancellationToken).ConfigureAwait(false);

        pendingStates.Remove(commandId.Value);
        await commit().ConfigureAwait(false);
    }

    private static async Task<bool> HasUsableRefreshTokenAsync(DurableGoogleTokenStore store, string userKey)
    {
        var token = await store.GetAsync<TokenResponse>(userKey).ConfigureAwait(false);
        return token is not null && !string.IsNullOrWhiteSpace(token.RefreshToken);
    }
}
