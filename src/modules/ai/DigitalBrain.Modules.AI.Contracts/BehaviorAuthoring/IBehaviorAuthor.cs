namespace DigitalBrain.AI;

public interface IBehaviorAuthor
{
    BehaviorScenarioProposal ProposeScenarios(BehaviorChangeRequest request);

    BehaviorChangeResult ApplyApprovedScenarios(
        BehaviorChangeRequest request,
        BehaviorScenarioProposal approved);
}
