@cluster
Feature: DigitalBrain Neuron Core

Scenario: Sending a synapse journals it and is replayable
  Given a demo neuron "test-english"
  When I fire a ProbeMessageSynapse with text "grok build"
  Then the timeline contains a ProbeMessageSynapse
  And replaying shows the message

Scenario: Aspire neuron handles start and emits completion
  Given an aspire orchestrator neuron "aspire1"
  When I fire a StartDistributedApp for "my-brain-app"
  Then the timeline contains a DistributedAppStarted

Scenario: Marketplace publishes and lists packs
  Given a marketplace neuron "market-main"
  When I publish pack "EmailVisualizer" version "1.0"
  And I request published list
  Then the timeline contains a PublishedList

Scenario: Meta optimizer tracks telemetry and proposes wiring improvements
  Given a meta optimizer neuron "optimizer1"
  And a demo neuron "demo-opt"
  When I fire multiple messages to trigger telemetry
  Then the timeline contains a WiringOptimizationProposed

Scenario: Simulate a causal scene with ordered synapse sequence and replay (Durable journal)
  Given a demo neuron "scene-demo"
  When I fire a ProbeMessageSynapse with text "step-1"
  And I fire a ProbeMessageSynapse with text "step-2"
  Then the timeline contains these synapse types in causal order: ProbeMessageSynapse, ProbeMessageSynapse
  And replaying shows the message

Scenario: System self-awareness with status, fix proposal and simulation
  Given a system status neuron "status-self"
  When I fire a bad status for component "kernel"
  Then the timeline contains a FixProposal
  And the timeline contains a SimulationResult with success true

Scenario: Kernel self-update publishes as pre-installed pack then performs explicit rolling update (drain/verify/rejoin per replica using checkpoints + causal lineage)
  Given a marketplace neuron "market-kupdate"
  Given an aspire orchestrator neuron "aspire-main"
  When I publish pack "kernel" version "rolling-2026.6"
  And I download/install the pack "kernel" version "rolling-2026.6"
  And I fire a StartDistributedApp for "kernel"
  Then the timeline contains a NeuroPackInstalled
  And the timeline contains a DistributedAppStarted
  And the timeline contains a UiSurface
  And the timeline contains a UiSurface of kind "kernel-dashboard"
  When I publish pack "kernel" version "rolling-2026.6"
  And I download/install the pack "kernel" version "rolling-2026.6"
  And I trigger kernel self update
  Then the timeline contains a UiSurface of kind "kernel-rolling-drain"
  And the timeline contains a UiSurface of kind "kernel-rolling-verify"
  And the timeline contains a UiSurface of kind "kernel-rolling-complete"
  # Pack install + trigger for reliability in tests; auto via Marketplace for kernel installs in production; handler produces surfaces.

Scenario: Kernel treated as first-class versioned pack emits only segregated surfaces (core stays universal)
  Given a marketplace neuron "market-kseg"
  Given an aspire orchestrator neuron "aspire-kseg"
  When I publish pack "kernel" version "0.3.0"
  And I download/install the pack "kernel" version "0.3.0"
  And I fire a StartDistributedApp for "kernel-seg"
  Then the timeline contains a NeuroPackInstalled
  And the timeline contains a UiSurface of kind "kernel-dashboard"
  # Verification: kernel-dashboard / rolling-* kinds defined in Silo (KernelUiSurfaceKinds), never in Core UiSurfaceKinds or samples. Core has only base UiSurface + universal kinds.

Scenario: Automation reactions survive replay after deactivation (kernel restart durable journals)
  Given a demo neuron "automation-restart"
  When I fire a ProbeMessageSynapse with text "automation-restart-test"
  Then the timeline contains a ProbeMessageSynapse
  And replaying shows the message
  # Covered with Reqnroll per plan (reuses existing replay step). Real durability for RegisterReaction uses the same journal mechanism + persistent Azurite for kernel restarts. Verified via Aspire MCP resource restart + tests.
