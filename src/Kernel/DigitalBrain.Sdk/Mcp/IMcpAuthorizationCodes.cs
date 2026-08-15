using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

// Host-side one-shot code consumer. NOT a ClientEntryPoint — authorization codes
// and PKCE verifiers never cross the client grain surface. Same-owner grain-to-grain
// only (capability reification). Unattributed client callers are refused by the
// owner-bound call filter.
[Alias("mcp.authorization.codes")]
public interface IMcpAuthorizationCodes : INeuron
{
    [Alias(nameof(TakeCompletedCode))]
    Task<McpAuthorizationCodeResult?> TakeCompletedCode(string state, CancellationToken cancellationToken = default);
}
