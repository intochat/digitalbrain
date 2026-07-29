namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

[ClientEntryPoint]
public partial interface IBehaviorNeuron : INeuron
{
    [Alias(nameof(Read))]
    Task<BehaviorSnapshot> Read();

    [Alias(nameof(Propose))]
    Task<BehaviorSnapshot> Propose(ProposeBehaviorRevision command);

    [Alias(nameof(RunTests))]
    Task<BehaviorSnapshot> RunTests(RunBehaviorTests command);

    [Alias(nameof(Approve))]
    Task<BehaviorSnapshot> Approve(BehaviorRevisionApproval approval);

    [Alias(nameof(Activate))]
    Task<BehaviorSnapshot> Activate(ActivateBehaviorRevision command);

    [Alias(nameof(Rollback))]
    Task<BehaviorSnapshot> Rollback(RollbackBehaviorRevision command);

    [Alias(nameof(Execute))]
    Task<BehaviorExecutionResult> Execute(ExecuteBehaviorRevision command);
}
