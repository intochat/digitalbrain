using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Authentication;
using Orleans.Journaling;

namespace DigitalBrain.Modules.Sdk.Mcp;

public static class McpAuthorizationRail
{
    public static async Task EnsureAuthorizedAsync(
        IGrainFactory grains,
        OwnerId owner,
        IServiceProvider services,
        TimeProvider time,
        CommandId commandId,
        McpServerDefinition server,
        PrincipalTokenSlot tokenSlot,
        Func<ValueTask> commit,
        ActorContext actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(tokenSlot);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(actor);
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        var configuration = services.GetRequiredService<IConfiguration>();
        var protector = services.GetRequiredService<IDurablePayloadProtector>();
        var integration = McpTokenPresence.UserIntegration(server.Key, actor, [.. server.Scopes]);
        var purpose = integration.ProtectedTokenReference;
        var grainId = NeuronId.For<IMcpAuthorization>(owner, McpAuthorizationNeuron.InstanceName).ToGrainId();
        var authorization = grains.GetGrain<IMcpAuthorization>(grainId);
        var codes = grains.GetGrain<IMcpAuthorizationCodes>(grainId);
        var missingOrExpired = McpTokenPresence.IsMissingOrExpired(tokenSlot, protector, purpose, time);

        McpAuthorizationClaim? claim = null;
        try
        {
            claim = await authorization.Claim(commandId, actor, cancellationToken).ConfigureAwait(false);
        }
        catch (NeuronAuthorizationException)
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
                        codes,
                        services,
                        configuration,
                        server,
                        tokenSlot,
                        commit,
                        protector,
                        purpose,
                        commandId,
                        actor,
                        cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    throw new InvalidOperationException($"Authorization claim kind '{claim.Kind}' is unsupported.");
            }
        }

        if (!missingOrExpired)
        {
            return;
        }

        // Single PKCE mint path. Tokens are exchanged on the Completed claim above so the
        // MCP client library never opens a second authorization transaction.
        var required = await BeginNewAsync(
            authorization,
            configuration,
            commandId,
            server,
            actor,
            cancellationToken).ConfigureAwait(false);
        throw new McpAuthorizationRequiredException(required);
    }

    private static async Task ExchangeCompletedAsync(
        IMcpAuthorization authorization,
        IMcpAuthorizationCodes codes,
        IServiceProvider services,
        IConfiguration configuration,
        McpServerDefinition server,
        PrincipalTokenSlot tokenSlot,
        Func<ValueTask> commit,
        IDurablePayloadProtector protector,
        string purpose,
        CommandId commandId,
        ActorContext actor,
        CancellationToken cancellationToken)
    {
        // Prefer tokens already stored (idempotent retry after exchange).
        if (!McpTokenPresence.IsMissingOrExpired(
                tokenSlot,
                protector,
                purpose,
                services.GetService<TimeProvider>() ?? TimeProvider.System))
        {
            return;
        }

        // Recover the rail-minted state via idempotent Begin (command already recorded).
        var recovered = await authorization.Begin(
            new BeginMcpAuthorization(
                commandId,
                server.Key,
                server.DisplayName,
                new Uri("https://auth.digitalbrain.local/oauth/completed"),
                "unused-when-command-exists",
                actor),
            cancellationToken).ConfigureAwait(false);

        // Host-only take — never on ClientEntryPoint.
        var taken = await codes.TakeCompletedCode(recovered.State, cancellationToken).ConfigureAwait(false);
        if (taken is null || string.IsNullOrWhiteSpace(taken.Code))
        {
            throw new NeuronAuthorizationException(
                $"{server.DisplayName} authorization completed without a deliverable code; sign in again.");
        }

        if (taken.Actor is { } bound
            && bound.PrincipalId != actor.PrincipalId)
        {
            throw new NeuronAuthorizationException(
                $"{server.DisplayName} authorization is bound to another principal; sign in as the original user.");
        }

        TokenContainer tokens;
        if (services.GetService<IMcpTokenExchanger>() is { } exchanger)
        {
            tokens = await exchanger.ExchangeAsync(
                server,
                taken.Code,
                taken.CodeVerifier ?? string.Empty,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var httpClients = services.GetRequiredService<IHttpClientFactory>();
            tokens = await McpTokenExchange.ExchangeAuthorizationCodeAsync(
                server,
                configuration,
                httpClients,
                taken.Code,
                taken.CodeVerifier
                ?? throw new NeuronAuthorizationException(
                    $"{server.DisplayName} authorization is missing the PKCE verifier; sign in again."),
                cancellationToken).ConfigureAwait(false);
        }

        await McpTokenPresence.StoreAsync(
            tokenSlot,
            commit,
            protector,
            purpose,
            tokens,
            cancellationToken).ConfigureAwait(false);
    }

    private static Task<AuthorizationRequired> BeginNewAsync(
        IMcpAuthorization authorization,
        IConfiguration configuration,
        CommandId commandId,
        McpServerDefinition server,
        ActorContext actor,
        CancellationToken cancellationToken)
    {
        var state = Guid.NewGuid().ToString("N");
        var (verifier, challenge) = OAuthPkce.CreateS256Pair();
        var signInUrl = BuildPkceAuthorizeUrl(configuration, server, state, challenge);
        return authorization.Begin(
            new BeginMcpAuthorization(
                commandId,
                server.Key,
                server.DisplayName,
                signInUrl,
                state,
                actor,
                challenge,
                verifier),
            cancellationToken);
    }

    internal static Uri BuildPkceAuthorizeUrl(
        IConfiguration configuration,
        McpServerDefinition server,
        string state,
        string codeChallenge)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeChallenge);

        var clientId = configuration[$"{server.ConfigurationRoot}:ClientId"];
        var redirectUri = configuration[$"{server.ConfigurationRoot}:RedirectUri"];
        if (!string.IsNullOrWhiteSpace(clientId)
            && !string.IsNullOrWhiteSpace(redirectUri)
            && (string.Equals(server.Key, "salesforce", StringComparison.OrdinalIgnoreCase)
                || server.Key.Contains("salesforce", StringComparison.OrdinalIgnoreCase)))
        {
            var scope = string.Join(' ', server.Scopes);
            return new Uri(
                "https://login.salesforce.com/services/oauth2/authorize"
                + "?response_type=code"
                + $"&client_id={Uri.EscapeDataString(clientId)}"
                + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                + $"&scope={Uri.EscapeDataString(scope)}"
                + $"&state={Uri.EscapeDataString(state)}"
                + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
                + $"&code_challenge_method={OAuthPkce.ChallengeMethodS256}");
        }

        // Gmail / Google OAuth (official Gmail MCP). Same sole-mint PKCE shape as Salesforce.
        if (!string.IsNullOrWhiteSpace(clientId)
            && !string.IsNullOrWhiteSpace(redirectUri)
            && IsGoogleGmailServer(server.Key))
        {
            var scope = string.Join(' ', server.Scopes);
            return new Uri(
                "https://accounts.google.com/o/oauth2/v2/auth"
                + "?response_type=code"
                + $"&client_id={Uri.EscapeDataString(clientId)}"
                + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                + $"&scope={Uri.EscapeDataString(scope)}"
                + $"&state={Uri.EscapeDataString(state)}"
                + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
                + $"&code_challenge_method={OAuthPkce.ChallengeMethodS256}"
                + "&access_type=offline"
                + "&prompt=consent");
        }

        var authorizeBase = configuration[$"{server.ConfigurationRoot}:AuthorizeEndpoint"];
        if (!string.IsNullOrWhiteSpace(clientId)
            && !string.IsNullOrWhiteSpace(redirectUri)
            && !string.IsNullOrWhiteSpace(authorizeBase)
            && Uri.TryCreate(authorizeBase, UriKind.Absolute, out var authorizeUri))
        {
            var scope = string.Join(' ', server.Scopes);
            var separator = string.IsNullOrEmpty(authorizeUri.Query) ? "?" : "&";
            return new Uri(
                authorizeUri.AbsoluteUri.TrimEnd('?', '&')
                + separator
                + "response_type=code"
                + $"&client_id={Uri.EscapeDataString(clientId)}"
                + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                + $"&scope={Uri.EscapeDataString(scope)}"
                + $"&state={Uri.EscapeDataString(state)}"
                + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
                + $"&code_challenge_method={OAuthPkce.ChallengeMethodS256}");
        }

        var publicBase = configuration[McpRuntimeHosting.PublicSignInBaseKey];
        if (string.IsNullOrWhiteSpace(publicBase)
            && !string.IsNullOrWhiteSpace(redirectUri)
            && Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirect))
        {
            publicBase = redirect.GetLeftPart(UriPartial.Authority);
        }

        if (!string.IsNullOrWhiteSpace(publicBase)
            && Uri.TryCreate(publicBase.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
        {
            return new Uri(
                baseUri,
                "oauth/authorize"
                + $"?server={Uri.EscapeDataString(server.Key)}"
                + $"&state={Uri.EscapeDataString(state)}"
                + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
                + $"&code_challenge_method={OAuthPkce.ChallengeMethodS256}");
        }

        return new Uri(
            "https://auth.digitalbrain.local/oauth/authorize"
            + $"?server={Uri.EscapeDataString(server.Key)}"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
            + $"&code_challenge_method={OAuthPkce.ChallengeMethodS256}");
    }

    internal static bool IsGoogleGmailServer(string serverKey)
        => string.Equals(serverKey, "google.gmail", StringComparison.OrdinalIgnoreCase)
            || (serverKey.Contains("gmail", StringComparison.OrdinalIgnoreCase)
                && serverKey.Contains("google", StringComparison.OrdinalIgnoreCase));
}

// Replaceable token-exchange boundary; production registers the HTTP implementation below.
internal interface IMcpTokenExchanger
{
    Task<TokenContainer> ExchangeAsync(
        McpServerDefinition server,
        string code,
        string codeVerifier,
        CancellationToken cancellationToken);
}
