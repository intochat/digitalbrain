using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string durableIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(tokenState);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableIdentity);
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        var configuration = services.GetRequiredService<IConfiguration>();
        var protector = services.GetRequiredService<IDurablePayloadProtector>();
        var purpose = McpTokenPresence.Purpose(server.Key, durableIdentity);
        var authorization = grains.GetGrain<IMcpAuthorization>(
            NeuronId.For<IMcpAuthorization>(owner, McpAuthorizationNeuron.InstanceName).ToGrainId());
        var missingOrExpired = McpTokenPresence.IsMissingOrExpired(tokenState, protector, purpose, time);

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
                    // Code is ready for the MCP client OAuth exchange on the next CreateAsync.
                    return;
                default:
                    throw new InvalidOperationException($"Authorization claim kind '{claim.Kind}' is unsupported.");
            }
        }

        if (!missingOrExpired)
        {
            return;
        }

        // No usable token: open a settled sign-in instead of falling through to a
        // 401/retry storm. Begin emits AuthorizationRequired (SSE + main chat button).
        var required = await BeginNewAsync(
            authorization,
            configuration,
            commandId,
            server,
            cancellationToken).ConfigureAwait(false);
        throw new McpAuthorizationRequiredException(required);
    }

    private static Task<AuthorizationRequired> BeginNewAsync(
        IMcpAuthorization authorization,
        IConfiguration configuration,
        CommandId commandId,
        McpServerDefinition server,
        CancellationToken cancellationToken)
    {
        var state = Guid.NewGuid().ToString("N");
        var signInUrl = SignInUrl(configuration, server, state);
        return authorization.Begin(
            new BeginMcpAuthorization(
                commandId,
                server.Key,
                server.DisplayName,
                signInUrl,
                state),
            cancellationToken);
    }

    private static Uri SignInUrl(IConfiguration configuration, McpServerDefinition server, string state)
    {
        var clientId = configuration[$"{server.ConfigurationRoot}:ClientId"];
        var redirectUri = configuration[$"{server.ConfigurationRoot}:RedirectUri"];
        if (!string.IsNullOrWhiteSpace(clientId)
            && !string.IsNullOrWhiteSpace(redirectUri)
            && string.Equals(server.Key, "salesforce", StringComparison.OrdinalIgnoreCase))
        {
            var scope = string.Join(' ', server.Scopes);
            return new Uri(
                "https://login.salesforce.com/services/oauth2/authorize"
                + "?response_type=code"
                + $"&client_id={Uri.EscapeDataString(clientId)}"
                + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                + $"&scope={Uri.EscapeDataString(scope)}"
                + $"&state={Uri.EscapeDataString(state)}");
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
                $"oauth/authorize?server={Uri.EscapeDataString(server.Key)}&state={Uri.EscapeDataString(state)}");
        }

        return new Uri(
            $"https://auth.digitalbrain.local/oauth/authorize?server={Uri.EscapeDataString(server.Key)}&state={Uri.EscapeDataString(state)}");
    }
}
