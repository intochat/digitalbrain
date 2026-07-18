name: creator
version: 1.0.0
desc: Creator
triggers: CreateIno,PackExperience
emits: UiSurface
region: main
observed-synapses: 0

on: ImprovementProposal
  show card( "Proposal: $description", column( text( "$description" ), button( "Approve", ApproveAction( action: "execute" ) ), button( "Dismiss", NeuronTelemetry( event: "ProposalDismissed" ) ) ) )
