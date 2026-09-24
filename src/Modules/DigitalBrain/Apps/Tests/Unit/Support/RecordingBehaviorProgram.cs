using System.Collections.Concurrent;
using DigitalBrain.Behavior;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

// Replaces the supervised worker: records what an installed app asks the Behavior module to run.
[GrainType("behavior.program")]
public sealed class RecordingBehaviorProgram : Neuron, IBehaviorProgram
{
    public static ConcurrentDictionary<string, BehaviorSnapshot> Programs { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, bool> Deleted { get; } = new(StringComparer.Ordinal);

    private string Key => this.GetPrimaryKeyString();

    private BehaviorSnapshot Current => Programs.GetOrAdd(Key, _ => new BehaviorSnapshot(0, BehaviorDesiredState.Stopped, BehaviorExecutionState.Stopped, null, null, null, false, null, []));

    public Task<BehaviorSnapshot> Read(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task<BehaviorSnapshot> Deploy(DeployBehavior request, CancellationToken cancellationToken = default)
    {
        var current = Current;
        if (Deleted.ContainsKey(Key)) { throw new InvalidOperationException("The program was deleted."); }
        if (current.Revision != request.ExpectedRevision) { throw new InvalidOperationException("Stale program revision."); }
        var deployment = new BehaviorDeployment(current.Deployments.Count + 1, request.Artifact, request.ConfigurationJson, DateTimeOffset.UtcNow, null);
        return Task.FromResult(Programs[Key] = current with
        {
            Revision = current.Revision + 1,
            DesiredState = BehaviorDesiredState.Running,
            State = BehaviorExecutionState.Running,
            DesiredDeploymentRevision = deployment.Revision,
            Deployments = [.. current.Deployments, deployment],
        });
    }

    public Task<BehaviorSnapshot> Delete(DeleteBehavior request, CancellationToken cancellationToken = default)
    {
        Deleted[Key] = true;
        return Task.FromResult(Programs[Key] = Current with { Revision = Current.Revision + 1, DesiredState = BehaviorDesiredState.Stopped, State = BehaviorExecutionState.Stopped });
    }

    public Task<BehaviorSnapshot> Stop(ChangeBehaviorState request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<BehaviorSnapshot> Start(ChangeBehaviorState request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<BehaviorSnapshot> Rollback(RollbackBehavior request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<BehaviorLogPage> ReadLogs(long afterSequence, int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
