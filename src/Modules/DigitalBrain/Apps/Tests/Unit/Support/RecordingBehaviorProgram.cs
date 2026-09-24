using System.Collections.Concurrent;
using System.Text.Json;
using DigitalBrain.Behavior;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

// Replaces the supervised worker: records what an installed app asks the Behavior module to run, with
// the real store's rules for replayed operation ids, revision conflicts and deleted programs.
[GrainType("behavior.program")]
public sealed class RecordingBehaviorProgram : Neuron, IBehaviorProgram
{
    public static ConcurrentDictionary<string, BehaviorSnapshot> Programs { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, bool> Deleted { get; } = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<(string Program, Guid Operation), (string Hash, BehaviorSnapshot Result)> Receipts = new();

    private string Key => this.GetPrimaryKeyString();

    private BehaviorSnapshot Current => Programs.GetOrAdd(Key, _ => new BehaviorSnapshot(0, BehaviorDesiredState.Stopped, BehaviorExecutionState.Stopped, null, null, null, false, null, []));

    public Task<BehaviorSnapshot> Read(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task<BehaviorSnapshot> Deploy(DeployBehavior request, CancellationToken cancellationToken = default)
        => Command(request.OperationId, "deploy", request, request.ExpectedRevision, current =>
        {
            if (Deleted.ContainsKey(Key)) { throw new InvalidOperationException("The program was deleted."); }
            var deployment = new BehaviorDeployment(current.Deployments.Count + 1, request.Artifact, request.ConfigurationJson, DateTimeOffset.UtcNow, null);
            return current with
            {
                Revision = current.Revision + 1,
                DesiredState = BehaviorDesiredState.Running,
                State = BehaviorExecutionState.Running,
                DesiredDeploymentRevision = deployment.Revision,
                Deployments = [.. current.Deployments, deployment],
            };
        });

    public Task<BehaviorSnapshot> Delete(DeleteBehavior request, CancellationToken cancellationToken = default)
        => Command(request.OperationId, "delete", request, request.ExpectedRevision, current =>
        {
            Deleted[Key] = true;
            return current with { Revision = current.Revision + 1, DesiredState = BehaviorDesiredState.Stopped, State = BehaviorExecutionState.Stopped };
        });

    public Task<BehaviorSnapshot> Stop(ChangeBehaviorState request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<BehaviorSnapshot> Start(ChangeBehaviorState request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<BehaviorSnapshot> Rollback(RollbackBehavior request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<BehaviorLogPage> ReadLogs(long afterSequence, int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    private Task<BehaviorSnapshot> Command<T>(Guid operation, string kind, T request, long expectedRevision, Func<BehaviorSnapshot, BehaviorSnapshot> apply)
    {
        var hash = kind + JsonSerializer.Serialize(request);
        if (Receipts.TryGetValue((Key, operation), out var prior))
        {
            return prior.Hash == hash ? Task.FromResult(prior.Result) : throw new InvalidOperationException("Operation ID was used with different input.");
        }
        var current = Current;
        if (current.Revision != expectedRevision) { throw new InvalidOperationException("Behavior revision conflict; read the program again."); }
        var result = Programs[Key] = apply(current);
        Receipts[(Key, operation)] = (hash, result);
        return Task.FromResult(result);
    }
}
