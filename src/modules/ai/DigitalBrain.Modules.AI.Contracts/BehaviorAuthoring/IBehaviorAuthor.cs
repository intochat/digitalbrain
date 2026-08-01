namespace DigitalBrain.AI;

public interface IBehaviorAuthor
{
    BehaviorScenarioProposal ProposeScenarios(BehaviorChangeRequest request);

    Task<BehaviorChangeResult> ApplyApprovedScenarios(
        BehaviorChangeRequest request,
        BehaviorScenarioProposal approved,
        CancellationToken cancellationToken = default);
}
