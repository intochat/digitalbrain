using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Behavior;

[Alias("behavior.program"), DefaultGrainType("behavior.program")]
public interface IBehaviorProgram : INeuron
{
    Task<BehaviorSnapshot> Read(CancellationToken cancellationToken = default);
    Task<BehaviorSnapshot> Deploy(DeployBehavior request, CancellationToken cancellationToken = default);
    Task<BehaviorSnapshot> Stop(ChangeBehaviorState request, CancellationToken cancellationToken = default);
    Task<BehaviorSnapshot> Start(ChangeBehaviorState request, CancellationToken cancellationToken = default);
    Task<BehaviorSnapshot> Rollback(RollbackBehavior request, CancellationToken cancellationToken = default);
    Task<BehaviorSnapshot> Delete(DeleteBehavior request, CancellationToken cancellationToken = default);
    Task<BehaviorLogPage> ReadLogs(long afterSequence, int limit = 100, CancellationToken cancellationToken = default);
}