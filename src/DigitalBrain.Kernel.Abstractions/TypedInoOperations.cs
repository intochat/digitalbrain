using System.Security.Cryptography;
using DigitalBrain.Core.Runtime;

namespace DigitalBrain.Kernel.Runtime;

public enum InoOperationResultStatus
{
    Unsupported,
    Ready,
    NeedsAuthorization,
    Prepared,
    Applied,
    AlreadyApplied,
    Rejected,
    OutcomeUnknown
}

public sealed record InoReadOperation(
    string ActorScope,
    string OperationId,
    SemanticIntentProposal Intent,
    InoAuthorizationResume? Authorization,
    WorkflowReference Workflow);

public sealed record InoReadOperationResult(
    InoOperationResultStatus Status,
    string SafeResult,
    string? ToolId = null,
    InoAuthorizationRequest? Authorization = null,
    string? Continuation = null);

public sealed record InoMutationPreview(
    string ActorScope,
    string OperationId,
    SemanticMutationProposal Proposal,
    WorkflowReference Workflow);

public sealed record InoMutationPreviewResult(
    InoOperationResultStatus Status,
    string SafeResult,
    string? ToolId = null,
    string? SafePreview = null,
    byte[]? Payload = null,
    InoAuthorizationRequest? Authorization = null);

public sealed record InoEffectExecution(
    string ActorScope,
    string OperationId,
    string ToolId,
    string SafePreview,
    string PayloadHash,
    string EffectId,
    string IdempotencyKey,
    string ExecutionProof,
    byte[] Payload)
{
    public bool HasValidPayloadHash() =>
        Payload is { Length: > 0 } &&
        string.Equals(
            PayloadHash,
            Convert.ToHexStringLower(SHA256.HashData(Payload)),
            StringComparison.Ordinal);
}

public sealed record InoEffectApplyResult(
    InoOperationResultStatus Status,
    string SafeResult);

public sealed record InoEffectVerificationResult(
    bool Verified,
    string SafeResult);

public interface IInoOperationCapability
{
    bool Supports(string toolId);
    Task<InoReadOperationResult> ReadAsync(
        InoReadOperation request,
        CancellationToken cancellationToken = default);
    Task<InoMutationPreviewResult> PreviewAsync(
        InoMutationPreview request,
        CancellationToken cancellationToken = default);
    Task<InoEffectApplyResult> ApplyAsync(
        InoEffectExecution request,
        CancellationToken cancellationToken = default);
    Task<InoEffectVerificationResult> VerifyAsync(
        InoEffectExecution request,
        CancellationToken cancellationToken = default);
}

public sealed class NoOpInoOperationCapability : IInoOperationCapability
{
    public bool Supports(string toolId) => false;

    public Task<InoReadOperationResult> ReadAsync(InoReadOperation request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new InoReadOperationResult(InoOperationResultStatus.Unsupported, "Operation not supported."));

    public Task<InoMutationPreviewResult> PreviewAsync(InoMutationPreview request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new InoMutationPreviewResult(InoOperationResultStatus.Unsupported, "Operation not supported."));

    public Task<InoEffectApplyResult> ApplyAsync(InoEffectExecution request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new InoEffectApplyResult(InoOperationResultStatus.Unsupported, "Operation not supported."));

    public Task<InoEffectVerificationResult> VerifyAsync(InoEffectExecution request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new InoEffectVerificationResult(false, "Operation not supported."));
}
