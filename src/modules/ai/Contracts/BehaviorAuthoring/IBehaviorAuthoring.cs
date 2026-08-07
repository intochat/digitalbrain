using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

// Approve is deliberately absent from the accepted-synapse surface: a handled synapse enters the
// active capability catalog and can be materialized as a model tool, and the scenario approval is
// the human gate that turns model-written source into a proposed revision. It stays a
// ClientEntryPoint method, reachable by an owner client and by nothing the model can call.
[ClientEntryPoint]
[Alias("ai.behavior-authoring")]
[Description(
    "Behavior authoring neuron holding scenario-first change proposals for the owner's behaviors "
    + "between drafting and the owner's approval")]
public partial interface IBehaviorAuthoring : INeuron
{
    [Alias(nameof(Propose))]
    Task<BehaviorChangeProposed> Propose(ProposeBehaviorChangeRequest request);

    [Alias(nameof(Approve))]
    Task<BehaviorChangeDecision> Approve(ApproveBehaviorChange command);
}
