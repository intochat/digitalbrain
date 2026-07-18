name: packager
version: 1.0.0
desc: Packager (system)
triggers: PackExperience
emits: ExperiencePacked,UiSurface
system: true
observed-synapses: 0

on: PackExperience
  show card( "Pack $experienceId v$version", column( text( "Materialized capsule" ), button( "Publish", PublishToMarketplace( ExperienceId: "$experienceId" ) ) ) )
