using DigitalBrain.Coding;
using DigitalBrain.Core;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Metadata;
using Orleans.Runtime;

namespace DigitalBrain.Behavior;

[Alias("behavior.events"), DefaultGrainType("behavior.program")]
internal interface IBehaviorProgramEvents : IGrainWithStringKey { Task Changed(); Task LogsChanged(Guid generation, long sequence); }

[GrainType("behavior.program")]
internal sealed class BehaviorProgramNeuron(BehaviorSupervisor supervisor, ICodeArtifactStore artifacts, ILogger<BehaviorProgramNeuron> logger)
    : Neuron, IBehaviorProgram, IBehaviorProgramEvents
{
    public Task<BehaviorSnapshot> Read(CancellationToken cancellationToken = default) => supervisor.Store.ReadAsync(this.GetPrimaryKeyString(), cancellationToken);
    public async Task<BehaviorSnapshot> Deploy(DeployBehavior request, CancellationToken cancellationToken = default)
    {
        if (await supervisor.Store.ReplayAsync(this.GetPrimaryKeyString(), "deploy", request.OperationId, request, cancellationToken) is { } prior) { return prior; }
        await artifacts.OpenVerifiedAsync(request.Artifact, cancellationToken);
        var result = await supervisor.Store.DeployAsync(this.GetPrimaryKeyString(), request, cancellationToken);
        await DeploymentChanged(result);
        supervisor.Wake();
        return result;
    }
    public async Task<BehaviorSnapshot> Start(ChangeBehaviorState request, CancellationToken cancellationToken = default)
    {
        if (await supervisor.Store.ReplayAsync(this.GetPrimaryKeyString(), "start", request.OperationId, request, cancellationToken) is { } prior) { return prior; }
        var current = await Read(cancellationToken);
        var deployment = current.Deployments.SingleOrDefault(d => d.Revision == current.DesiredDeploymentRevision) ?? throw new InvalidOperationException("Deploy an artifact first.");
        await artifacts.OpenVerifiedAsync(deployment.Artifact, cancellationToken);
        var result = await supervisor.Store.ChangeAsync(this.GetPrimaryKeyString(), request, true, cancellationToken);
        supervisor.Wake();
        return result;
    }
    public async Task<BehaviorSnapshot> Stop(ChangeBehaviorState request, CancellationToken cancellationToken = default)
    {
        var result = await supervisor.Store.ChangeAsync(this.GetPrimaryKeyString(), request, false, cancellationToken);
        supervisor.Wake();
        return result;
    }
    public async Task<BehaviorSnapshot> Rollback(RollbackBehavior request, CancellationToken cancellationToken = default)
    {
        if (await supervisor.Store.ReplayAsync(this.GetPrimaryKeyString(), "rollback", request.OperationId, request, cancellationToken) is { } prior) { return prior; }
        var current = await Read(cancellationToken);
        var deployment = current.Deployments.SingleOrDefault(d => d.Revision == request.DeploymentRevision) ?? throw new KeyNotFoundException("Retained deployment not found.");
        await artifacts.OpenVerifiedAsync(deployment.Artifact, cancellationToken);
        var result = await supervisor.Store.RollbackAsync(this.GetPrimaryKeyString(), request, cancellationToken);
        await DeploymentChanged(result);
        supervisor.Wake();
        return result;
    }
    public async Task<BehaviorSnapshot> Delete(DeleteBehavior request, CancellationToken cancellationToken = default)
    {
        if (await supervisor.Store.ReplayAsync(this.GetPrimaryKeyString(), "delete", request.OperationId, request, cancellationToken) is { } prior) { return prior; }
        var result = await supervisor.Store.RemoveAsync(this.GetPrimaryKeyString(), request, cancellationToken);
        await supervisor.StopProgramAsync(this.GetPrimaryKeyString(), cancellationToken);
        supervisor.Wake();
        return result;
    }
    public Task<BehaviorLogPage> ReadLogs(long afterSequence, int limit = 100, CancellationToken cancellationToken = default)
        => supervisor.Logs.ReadAsync(this.GetPrimaryKeyString(), afterSequence, limit, cancellationToken);
    public async Task Changed()
    {
        var state = await Read();
        await PublishAsync(new BehaviorExecutionChanged(this.GetPrimaryKeyString(), state.GenerationId ?? Guid.Empty, state.State, state.Ready, state.Error));
    }
    public Task LogsChanged(Guid generation, long sequence)
        => PublishAsync(new BehaviorLogAvailable(this.GetPrimaryKeyString(), generation, sequence));
    private async Task DeploymentChanged(BehaviorSnapshot snapshot)
    {
        try { await PublishAsync(new BehaviorDeploymentChanged(this.GetPrimaryKeyString(), snapshot.Revision, snapshot.Deployments.Last().Artifact.Id)); }
        catch (Exception error) { logger.LogWarning(error, "Deployment persisted but notification failed."); }
    }
}