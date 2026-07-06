// DigitalBrain.TestKit/InoTestHarness.cs
// Minimal harness to make InoInteractResult-based verification easy and consistent
// with the MCP common contract (see docs/ino-mcp-contract-progress.md).
//
// Usage in tests:
//   var result = await InoTestHarness.Interact(inoGrain, "tell me a joke", clientId: "my-test");
//   Assert.Contains("funny", result.ResponseText);
//   Assert.DoesNotContain("I'll start", result.ResponseText);

using DigitalBrain.Core;

namespace DigitalBrain.TestKit;

public static class InoTestHarness
{
    /// <summary>
    /// Convenience wrapper around IInoNeuron.InteractAsync using the shared contract.
    /// See docs/ino-mcp-contract-progress.md for usage patterns and verification strategy.
    /// </summary>
    public static async Task<InoInteractResult> Interact(
        IInoNeuron ino,
        string prompt,
        string? clientId = "test-harness",
        string? workspaceId = null,
        bool includeProposals = true)
    {
        var req = new InoInteractRequest(prompt, clientId, workspaceId, includeProposals, true);
        return await ino.InteractAsync(req);
    }

    // Assertion helpers live in test code (xUnit Assert) to keep TestKit dependency-free.
    // Example in tests:
    //   var result = await InoTestHarness.Interact(...);
    //   Assert.Contains("joke", result.ResponseText);
    //   Assert.DoesNotContain("I'll start", result.ResponseText);
}
