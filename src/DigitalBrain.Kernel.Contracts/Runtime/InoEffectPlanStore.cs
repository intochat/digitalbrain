using DigitalBrain.Kernel.Contracts.Runtime;
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
    Task<InoToolRequest> PrepareIdempotentAsync(
        string idempotencyKey,
        string actorScope,
        string operationId,
        string toolId,
        byte[] payloadUtf8,
        string safeSummary,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}
