using System.Security.Cryptography;
using DigitalBrain.Kernel.Contracts.Runtime;
using Orleans;
namespace DigitalBrain.Kernel.Runtime;

internal sealed class InoEffectPlanStore(IGrainFactory grainFactory, InoEffectPlanAuthority authority) : IInoEffectPlanStore
{
    public async Task<InoToolRequest> PrepareAsync(
        string actorScope,
        string operationId,
        string toolId,
        byte[] payloadUtf8,
        string safeSummary,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var planId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        return await PrepareAsync(planId, actorScope, operationId, toolId, payloadUtf8, safeSummary, expiresAt, cancellationToken);
    }
    public Task<InoToolRequest> PrepareIdempotentAsync(
        string idempotencyKey,
        string actorScope,
        string operationId,
        string toolId,
        byte[] payloadUtf8,
        string safeSummary,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (idempotencyKey.Length > 512 || idempotencyKey.Any(char.IsControl))
            throw new ArgumentException("A bounded effect idempotency key is required.", nameof(idempotencyKey));
        var planId = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("feature-effect:" + idempotencyKey)));
        return PrepareAsync(planId, actorScope, operationId, toolId, payloadUtf8, safeSummary, expiresAt, cancellationToken);
    }
    public Task<InoToolEffectResult> DeclineAsync(
        InoToolRequest request,
        string actorScope,
        string decisionId,
        CancellationToken cancellationToken = default)
    {
        var plan = ResolvePlan(request, actorScope);
        return grainFactory.GetGrain<IInoEffectPlanNeuron>(plan).DeclineAsync(actorScope, decisionId, cancellationToken);
    }
    public Task<InoEffectDecision?> ReadDecisionAsync(
        InoToolRequest request,
        string actorScope,
        CancellationToken cancellationToken = default)
    {
        var plan = ResolvePlan(request, actorScope);
        return grainFactory.GetGrain<IInoEffectPlanNeuron>(plan).ReadDecisionAsync(actorScope, cancellationToken);
    }
    private async Task<InoToolRequest> PrepareAsync(
        string planId,
        string actorScope,
        string operationId,
        string toolId,
        byte[] payloadUtf8,
        string safeSummary,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var plan = new InoEffectPlan(planId, actorScope, operationId, toolId, payloadUtf8.ToArray(), safeSummary, expiresAt);
        InoEffectPlanTransitions.ValidatePlan(plan, requirePayload: true);
        await grainFactory.GetGrain<IInoEffectPlanNeuron>(planId).PutAsync(plan).WaitAsync(cancellationToken);
        return new InoToolRequest(toolId, InoToolAccess.Mutation, authority.Issue(planId, actorScope, toolId, safeSummary), safeSummary);
    }
    private string ResolvePlan(InoToolRequest request, string actorScope)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorScope);
        if (request.Access != InoToolAccess.Mutation ||
            !authority.TryValidate(request.Scope, actorScope, request.ToolId, request.SafeSummary, out var planId))
            throw new UnauthorizedAccessException("Signed effect plan evidence is required.");
        return planId;
    }
}
