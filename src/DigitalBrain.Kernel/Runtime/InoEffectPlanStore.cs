using System.Security.Cryptography;
using DigitalBrain.Core.Runtime;
using Orleans;

namespace DigitalBrain.Kernel.Runtime;

public interface IInoEffectPlanStore
{
    Task<InoToolRequest> PrepareAsync(
        string actorScope,
        string operationId,
        string toolId,
        byte[] payloadUtf8,
        string safeSummary,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}

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
