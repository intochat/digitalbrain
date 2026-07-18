name: awesome-se-team
version: 1.0.0
desc: Awesome SE team
triggers: ReviewProjectRequest
emits: ReviewResult,UiSurface
observed-synapses: 0

on: ReviewProjectRequest
  show card( "Review $path", column( text( "Analyzing..." ), button( "Report", NeuronTelemetry( event: "ProjectReviewed" ) ) ) )
