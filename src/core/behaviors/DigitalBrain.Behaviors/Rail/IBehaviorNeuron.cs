namespace DigitalBrain.Behaviors;

using System.ComponentModel;
using DigitalBrain.Abstractions;

[ClientEntryPoint]
[Alias("behaviors.behavior")]
[Description("Owner behavior rail neuron")]
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

    [Alias(nameof(ActivateBound))]
    Task<BoundBehaviorActivationResult> ActivateBound(ActivateBoundBehavior command);

    [Alias(nameof(StopRun))]
    Task<BehaviorSnapshot> StopRun(StopBehavior command);

    [Alias(nameof(StartRun))]
    Task<BehaviorSnapshot> StartRun(StartBehavior command);

    [Alias(nameof(Rollback))]
    Task<BehaviorSnapshot> Rollback(RollbackBehaviorRevision command);

    [Alias(nameof(Execute))]
    Task<BehaviorExecutionResult> Execute(ExecuteBehaviorRevision command);
}
