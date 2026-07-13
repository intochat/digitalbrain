using DigitalBrain.Core.Runtime;

namespace DigitalBrain.Kernel.Runtime;

/// <summary>
/// Production starts closed: a mutation must be supplied by a registered typed gateway before it can be
/// proposed or executed. This prevents an agent descriptor from becoming an implicit provider call.
/// </summary>
public sealed class ClosedInoToolGateway : IInoToolGateway
{
    public bool TryAuthorizeMutation(InoToolRequest request, string actorScope, out InoApprovedTool tool)
    {
        tool = default!;
        return false;
    }

    public Task<InoToolEffectResult> ExecuteApprovedAsync(
        InoToolEffectRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new InoToolEffectResult(
            InoToolEffectDisposition.Failed,
            "No trusted typed tool is configured for this action. No external action was performed."));
}
