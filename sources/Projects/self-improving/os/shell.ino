name: shell
version: 1.0.0
desc: Workspace and placement (substrate, system)
triggers: PinSurface,UnpinSurface,MoveSurface,BundleInstalled,BundleUninstalled,UiSurface
emits: WorkspaceChanged
system: true
observed-synapses: 0

on: UiSurface
  show card "Liquid Glass Desktop", column(container(12|glass, row(icon("widgets"), text("DigitalBrain OS"), divider(), button("🔍 Search", ListPublished()), button("⎋ Sign out", BeginGoogleAuth()))), row(container(8|glass, column(text("— VIEWS —"), button("🏠 Home", PinSurface(SurfaceId: "marketplace", Region: "main", Order: 0)), button("✅ Tasks", PinSurface(SurfaceId: "kerneltasks", Region: "main", Order: 1)), button("🛒 Marketplace", PinSurface(SurfaceId: "marketplace", Region: "main", Order: 2)), button("✨ Creator", PinSurface(SurfaceId: "ui-def-creator", Region: "main", Order: 3)), button("📧 Mail", OpenWindow(SurfaceId: "gmail-senders-chart", Title: "📧 Mail", X: 80, Y: 80, Width: 540, Height: 380)), button("🌤️ Weather", PinSurface(SurfaceId: "weather", Region: "widgets", Order: 5)), button("🔐 Auth", PinSurface(SurfaceId: "ui-def-google-auth", Region: "main", Order: 6)), button("🎨 Design System", PinSurface(SurfaceId: "ui-kit", Region: "main", Order: 7)), button("Open Tasks as Win", OpenWindow(SurfaceId: "kerneltasks", Title: "Active Tasks", X: 120, Y: 120, Width: 420, Height: 380)), button("Tile Layout", AutoLayoutWindows()))), container(0|glass, column(text("— MAIN WORKSPACE —"), container(6|glass, column(text("Declarative UI from .ino • pin from nav for live surfaces"), text("Glass tokens + getwidget kit render all"))), text("[[region:main]]"))), container(8|glass, column(text("— WIDGETS DOCK —"), text("[[region:widgets]]")))), text("Liquid Glass OS • full declarative chrome from shell.ino • nav+regions+windows reactive"))

on: NeuronTelemetry when Event = "UninstallRefused"
  show card( "System bundle", column( text( "Cannot uninstall (system: true, see telemetry data)" ) ) )

on: NeuronTelemetry when Event = "QuarantineGreen"
  show card( "Quarantine Green", column( text( "sig+tests passed, promoted" ), button( "Use", InstallFromMarketplace( ExperienceId: "demo" ) ) ) )

on: NeuronTelemetry when Event = "BundleUpdateResult"
  show card( "Bundle Updated", column( text( "Update result from telemetry" ) ) )
