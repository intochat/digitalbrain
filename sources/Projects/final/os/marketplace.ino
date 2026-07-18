name: marketplace
version: 1.0.0
desc: Marketplace (system)
triggers: InstallFromMarketplace,PublishToMarketplace,ListPublished
emits: ExperiencePacked,ExperienceListed,UiSurface
system: true
region: main
observed-synapses: 0

on: ExperienceListed
  show card( "🛒 Marketplace", column( text( "— Core Bundles (tap install, live update shell) —" ), text( "kernel-tasks: active tasks + alarms" ), button( "Install kernel-tasks", InstallFromMarketplace( ExperienceId: "kernel-tasks" ) ), text( "creator: proposals + ino gen" ), button( "Install creator", InstallFromMarketplace( ExperienceId: "creator" ) ), text( "weather-watcher: live conditions" ), button( "Install weather", InstallFromMarketplace( ExperienceId: "weather-watcher" ) ), text( "gmail-last-senders: recent contacts (needs auth)" ), button( "Install gmail", InstallFromMarketplace( ExperienceId: "gmail-last-senders" ) ), text( "google-auth: sign-in grants" ), button( "Install google-auth", InstallFromMarketplace( ExperienceId: "google-auth" ) ), text( "— Global / Community —" ), button( "Search global", ListPublished() ), text( "— Actions —" ), button( "Uninstall last", UninstallBundle( "demo" ) ), button( "▶ Distribution sim (N+1 + live UI)", RunDistributionSimulation() ) ) )
