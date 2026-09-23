using DigitalBrain.Core;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Metadata;
using Orleans.Runtime;

namespace DigitalBrain.Coding;

[Alias("coding.draft-events"), DefaultGrainType("coding.draft")]
internal interface ICodeDraftEvents : IGrainWithStringKey
{
    Task Changed(Guid operationId);
}

[GrainType("coding.draft")]
internal sealed class CodeDraftNeuron(CodeCheckCoordinator checks, ILogger<CodeDraftNeuron> logger) : Neuron, ICodeDraft, ICodeDraftEvents
{
    public Task<CodeDraftSnapshot> Read(CancellationToken cancellationToken = default)
        => checks.Store.ReadAsync(this.GetPrimaryKeyString(), cancellationToken);

    public async Task<CodeDraftSnapshot> Save(SaveCodeDraft request, CancellationToken cancellationToken = default)
    {
        var state = await checks.Store.SaveAsync(this.GetPrimaryKeyString(), request, cancellationToken);
        try { await PublishAsync(new CodeDraftSaved(this.GetPrimaryKeyString(), state.Revision)); }
        catch (Exception error) { logger.LogWarning(error, "Draft saved but live notification failed."); }
        return state;
    }

    public async Task<CodeCheckSnapshot> Check(CheckCodeDraft request, CancellationToken cancellationToken = default)
    {
        var check = await checks.Store.QueueAsync(this.GetPrimaryKeyString(), request, cancellationToken);
        if (check.Status == CodeCheckStatus.Queued) { await checks.EnqueueAsync(this.GetPrimaryKeyString(), request.OperationId, CancellationToken.None); }
        return await checks.Store.ReadCheckAsync(this.GetPrimaryKeyString(), request.OperationId, CancellationToken.None);
    }

    public Task<CodeCheckSnapshot> ReadCheck(Guid operationId, CancellationToken cancellationToken = default)
        => checks.Store.ReadCheckAsync(this.GetPrimaryKeyString(), operationId, cancellationToken);

    public async Task CancelCheck(Guid operationId, CancellationToken cancellationToken = default)
    {
        await checks.CancelAsync(this.GetPrimaryKeyString(), operationId, cancellationToken);
        try { await Changed(operationId); }
        catch (Exception error) { logger.LogWarning(error, "Check cancelled but live notification failed."); }
    }

    public async Task Changed(Guid operationId)
    {
        var check = await ReadCheck(operationId);
        await PublishAsync(new CodeCheckChanged(this.GetPrimaryKeyString(), operationId, check.Revision, check.Status));
    }
}