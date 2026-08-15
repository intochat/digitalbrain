using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

[ClientEntryPoint]
[Alias("mcp.authorization")]
[Description("MCP OAuth authorization neuron")]
public partial interface IMcpAuthorization : INeuron
{
    const string DefaultInstanceName = "mcp";

    [Alias(nameof(Begin))]
    Task<AuthorizationRequired> Begin(BeginMcpAuthorization request, CancellationToken cancellationToken = default);

    [Alias(nameof(BindCompletionTarget))]
    Task BindCompletionTarget(
        BindMcpAuthorizationCompletionTarget request,
        CancellationToken cancellationToken = default);

    [Alias(nameof(DeliverCallback))]
    Task<McpAuthorizationCallbackDelivery> DeliverCallback(
        DeliverMcpAuthorizationCallback delivery,
        CancellationToken cancellationToken = default);

    [Alias(nameof(Claim))]
    Task<McpAuthorizationClaim> Claim(
        CommandId commandId,
        ActorContext actor,
        CancellationToken cancellationToken = default);

    // Wave 4: pending OAuth is keyed by (serverKey, PrincipalId) so a regenerated
    // CommandId still finds the same PKCE transaction mid-conversation.
    [Alias(nameof(ClaimForServer))]
    Task<McpAuthorizationClaim> ClaimForServer(
        string serverKey,
        ActorContext actor,
        CancellationToken cancellationToken = default);
}
