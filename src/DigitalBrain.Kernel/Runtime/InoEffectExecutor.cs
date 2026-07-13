using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Capabilities;
using Orleans;

namespace DigitalBrain.Kernel.Runtime;

public sealed class InoEffectExecutor : IInoEffectExecutor
{
    private readonly IGrainFactory _grainFactory;
    private readonly InoEffectPlanAuthority _authority;
    private readonly IReadOnlySet<string> _tools;

    public InoEffectExecutor(
        IGrainFactory grainFactory,
        InoEffectPlanAuthority authority,
        IEnumerable<IInoEffectHandler> handlers)
    {
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        ArgumentNullException.ThrowIfNull(handlers);
        var tools = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentException.ThrowIfNullOrWhiteSpace(handler.ToolId);
            if (!tools.Add(handler.ToolId))
                throw new InvalidOperationException($"Effect handler '{handler.ToolId}' is registered more than once.");
        }
        _tools = tools;
    }

    public bool TryAuthorizeMutation(InoToolRequest request, string actorScope, out InoApprovedTool tool)
    {
        tool = default!;
        if (request.Access != InoToolAccess.Mutation || !_tools.Contains(request.ToolId) ||
            !_authority.TryValidate(request.Scope, actorScope, request.ToolId, request.SafeSummary, out _))
            return false;
        tool = new InoApprovedTool(request.ToolId, request.Scope, request.SafeSummary);
        return true;
    }

    public Task<InoToolEffectResult> ExecuteAsync(
        InoToolEffectRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tools.Contains(request.ToolId) ||
            !_authority.TryValidateToken(
                request.Scope,
                request.ActorScope,
                request.ToolId,
                out var planId,
                out var summaryDigest))
            return Task.FromResult(new InoToolEffectResult(
                InoToolEffectDisposition.Failed,
                "The approved action did not have a valid typed execution plan. No external action was performed."));
        var executionProof = _authority.IssueExecutionProof(
            planId,
            request.ActorScope,
            request.OperationId,
            request.ToolId,
            request.EffectId,
            request.ProviderIdempotencyKey);
        return _grainFactory.GetGrain<IInoEffectPlanNeuron>(planId).ExecuteAsync(
            request.ActorScope,
            request.OperationId,
            request.ToolId,
            summaryDigest,
            request.EffectId,
            request.ProviderIdempotencyKey,
            executionProof,
            cancellationToken);
    }
}

public sealed class DisabledInoEffectExecutor : IInoEffectExecutor
{
    public bool TryAuthorizeMutation(InoToolRequest request, string actorScope, out InoApprovedTool tool)
    {
        tool = default!;
        return false;
    }

    public Task<InoToolEffectResult> ExecuteAsync(
        InoToolEffectRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new InoToolEffectResult(
            InoToolEffectDisposition.Failed,
            "No trusted typed tool is configured for this action. No external action was performed."));
}
