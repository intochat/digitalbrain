using DigitalBrain.Abstractions;
using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Authentication;
using Orleans.Journaling;

namespace DigitalBrain.Mcp;

internal static class McpAuthorizationRail
{
    internal static async Task EnsureAuthorizedAsync(
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
        if (!string.Equals(
                configuration[McpRuntimeHosting.AuthorizationModeKey],
                McpRuntimeHosting.EdgeMode,
                StringComparison.Ordinal))
        {
            return;
        }

        var protector = services.GetRequiredService<IDurablePayloadProtector>();
        var purpose = McpTokenPresence.Purpose(server.Key, durableIdentity);
        var authorization = grains.GetGrain<IMcpAuthorization>(
            NeuronId.For<IMcpAuthorization>(owner, McpAuthorizationNeuron.InstanceName).ToGrainId());

        if (!McpTokenPresence.IsMissingOrExpired(tokenState, protector, purpose, time))
        {
            return;
        }

        McpAuthorizationClaim claim;
        try
        {
            claim = await authorization.Claim(commandId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            var required = await BeginNewAsync(
                authorization,
                configuration,
                commandId,
                server,
                cancellationToken);
            throw new McpAuthorizationRequiredException(required);
        }

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
                await McpTokenPresence.StoreAsync(
                    tokenState,
                    commit,
                    protector,
                    purpose,
                    new TokenContainer
                    {
                        TokenType = "Bearer",
                        AccessToken = $"authorized:{commandId}:{server.Key}",
                        ObtainedAt = time.GetUtcNow(),
                        ExpiresIn = 3600,
                    },
                    cancellationToken);
                return;
            default:
                throw new InvalidOperationException($"Authorization claim kind '{claim.Kind}' is unsupported.");
        }
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
        var publicBase = configuration[McpRuntimeHosting.PublicSignInBaseKey];
        if (!string.IsNullOrWhiteSpace(publicBase)
            && Uri.TryCreate(publicBase.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
        {
            return new Uri(
                baseUri,
                $"oauth/mcp/authorize?server={Uri.EscapeDataString(server.Key)}&state={Uri.EscapeDataString(state)}");
        }

        return new Uri(
            $"https://auth.digitalbrain.local/oauth/mcp/authorize?server={Uri.EscapeDataString(server.Key)}&state={Uri.EscapeDataString(state)}");
    }
}
