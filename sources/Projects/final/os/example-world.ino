name: example-world
version: 1.0.0
desc: Second world declared via world: from in brain.ino
observed-synapses: 0
system: true

seed: os/marketplace.ino
seed: os/packager.ino
seed: os/creator.ino

on: WorldConnected
  show card( "World $name", column( text( "Connected world frame (UI via shell + surfaces)" ), text( "Marketplace connected by default (search/install via public contract)" ), button( "Search marketplace", ListPublished() ), button( "Install demo from marketplace", InstallFromMarketplace( ExperienceId: "demo" ) ) ) )
