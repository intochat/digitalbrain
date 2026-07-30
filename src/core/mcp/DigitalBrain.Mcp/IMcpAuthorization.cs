using DigitalBrain.Abstractions;

namespace DigitalBrain.Mcp;

[ClientEntryPoint]
public partial interface IMcpAuthorization : INeuron
{
    [Alias(nameof(Begin))]
    Task<AuthorizationRequired> Begin(BeginMcpAuthorization request, CancellationToken cancellationToken = default);

    [Alias(nameof(DeliverCallback))]
    Task<McpAuthorizationCallbackDelivery> DeliverCallback(
        DeliverMcpAuthorizationCallback delivery,
        CancellationToken cancellationToken = default);

    [Alias(nameof(Claim))]
    Task<McpAuthorizationClaim> Claim(CommandId commandId, CancellationToken cancellationToken = default);

    [Alias(nameof(TakeCompletedCode))]
    Task<McpAuthorizationCodeResult?> TakeCompletedCode(string state, CancellationToken cancellationToken = default);
}
