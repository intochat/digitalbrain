using DigitalBrain.Core.Runtime;
using Orleans;

namespace DigitalBrain.Kernel.Runtime;

public sealed class PlanInoToolGateway(
    IGrainFactory grainFactory,
    InoEffectPlanAuthority authority,
    IInoOperationCapability operations) : IInoToolGateway
{
    public bool TryAuthorizeMutation(InoToolRequest request, string actorScope, out InoApprovedTool tool)
    {
        tool = default!;
        if (request.Access != InoToolAccess.Mutation || !IsSupported(request.ToolId) ||
            !authority.TryValidate(request.Scope, actorScope, request.ToolId, request.SafeSummary, out _))
            return false;
        tool = new InoApprovedTool(request.ToolId, request.Scope, request.SafeSummary);
        return true;
    }

    public Task<InoToolEffectResult> ExecuteApprovedAsync(
        InoToolEffectRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported(request.ToolId) ||
            !authority.TryValidateToken(
                request.Scope,
                request.ActorScope,
                request.ToolId,
                out var planId,
                out var summaryDigest))
            return Task.FromResult(new InoToolEffectResult(
                InoToolEffectDisposition.Failed,
                "The approved action did not have a valid typed execution plan. No external action was performed."));
        var executionProof = authority.IssueExecutionProof(
            planId,
            request.ActorScope,
            request.OperationId,
            request.ToolId,
            request.EffectId,
            request.ProviderIdempotencyKey);
        return grainFactory.GetGrain<IInoEffectPlanNeuron>(planId)
            .ExecuteAsync(
                request.ActorScope,
                request.OperationId,
                request.ToolId,
                summaryDigest,
                request.EffectId,
                request.ProviderIdempotencyKey,
                executionProof,
                cancellationToken);
    }

    private bool IsSupported(string toolId) =>
        string.Equals(toolId, GmailTools.Send, StringComparison.Ordinal) || operations.Supports(toolId);
}
