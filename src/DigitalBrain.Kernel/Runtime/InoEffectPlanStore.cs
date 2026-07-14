using System.Security.Cryptography;
using DigitalBrain.Core.Runtime;
using Orleans;

namespace DigitalBrain.Kernel.Runtime;

public sealed class InoEffectPlanStore(
    IGrainFactory grainFactory,
    InoEffectPlanAuthority authority) : IInoEffectPlanStore
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
        return await PrepareAsync(
            planId,
            actorScope,
            operationId,
            toolId,
            payloadUtf8,
            safeSummary,
            expiresAt,
            cancellationToken);
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
        var planId = Convert.ToHexStringLower(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("feature-effect:" + idempotencyKey)));
        return PrepareAsync(
            planId,
            actorScope,
            operationId,
            toolId,
            payloadUtf8,
            safeSummary,
            expiresAt,
            cancellationToken);
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
        var plan = new InoEffectPlan(
            planId,
            actorScope,
            operationId,
            toolId,
            payloadUtf8.ToArray(),
            safeSummary,
            expiresAt);
        InoEffectPlanTransitions.ValidatePlan(plan, requirePayload: true);
        await grainFactory.GetGrain<IInoEffectPlanNeuron>(planId).PutAsync(plan).WaitAsync(cancellationToken);
        return new InoToolRequest(
            toolId,
            InoToolAccess.Mutation,
            authority.Issue(planId, actorScope, toolId, safeSummary),
            safeSummary);
    }
}
