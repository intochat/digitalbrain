# DigitalBrain Target Tree

Status: target repository structure after the cleanup/refactor.

This is the intended shape, not the current shape. The current repo still has
stale docs, old examples, generated folders, legacy signal vocabulary, and
mixed test styles.

## Top Level

```text
E:/digitalbrain/
|-- DigitalBrain.slnx
|-- Directory.Build.props
|-- Directory.Build.targets
|-- Directory.Packages.props
|-- README.md
|-- CLAUDE.md
|-- digitalbrain.cs
|-- digitalbrain.ino
|-- docs/
|-- contracts/
|-- kernel/
|-- inolang/
|-- sdk/
|-- simulations/
|-- UI/
|-- tools/
`-- examples/
```

Keep the top level boring. Runtime/generated data should not live here except
for deliberate samples.

## Canonical Docs

```text
docs/
|-- DIGITALBRAIN_CLEANUP_ACTION_PLAN.md
|-- DIGITALBRAIN_CONTINUATION_PROMPT.md
`-- DIGITALBRAIN_TARGET_TREE.md
```

Optional if the project needs a docs index:

```text
docs/
`-- README.md
```

Delete or archive after migration:

```text
docs/v5plan/
docs/v6plan/
docs/superpowers/
docs/DIGITALBRAIN_RESEARCH.md
docs/implementation_plan.md
docs/architectural_blueprint.md
docs/apple_minimalist_redesign.md
docs/*.lottie
docs/*.png
```

If any old doc contains a still-current rule, copy the rule into the cleanup
plan, root `README.md`, or root `CLAUDE.md` before deleting it.

## Solution Projects

Target `DigitalBrain.slnx`:

```xml
<Solution>
  <Project Path="contracts/DigitalBrain.Contracts/DigitalBrain.Contracts.csproj" />
  <Project Path="kernel/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj" />
  <Project Path="kernel/DigitalBrain.Runtime/DigitalBrain.Runtime.csproj" />
  <Project Path="kernel/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj" />
  <Project Path="inolang/DigitalBrain.InoLang/DigitalBrain.InoLang.csproj" />
  <Project Path="simulations/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj" />
  <Project Path="sdk/DigitalBrain.SDK/DigitalBrain.SDK.csproj" />
  <Project Path="UI/flutter/Flutter.proj" Type="9a19103f-16f7-4668-be54-9a1e7a4f7556" />
</Solution>
```

Notes:

- `contracts/DigitalBrain.Contracts` is the only proposed new C# project.
  It exists only if public/private silo boundaries need stable C# contracts.
- If generated JSON contracts are enough, this project can be skipped.
- `inolang/DigitalBrain.InoLang.Tests` should either be folded into
  `simulations/DigitalBrain.Simulations` or kept temporarily only for parser
  migration. The long-term target is one simulation test project.
- Keep `DigitalBrain.AppHost`; the constantly-running Aspire workflow needs a
  real composition root even if `digitalbrain.cs` remains the product launch
  entry.

## Contracts

```text
contracts/
`-- DigitalBrain.Contracts/
    |-- DigitalBrain.Contracts.csproj
    |-- Core/
    |   |-- SynapseContract.cs
    |   |-- NeuronContract.cs
    |   |-- ContractManifest.cs
    |   |-- BundleContract.cs
    |   `-- CapabilityDeclaration.cs
    |-- Marketplace/
    |   |-- PublicBundleContract.cs
    |   |-- PrivateImplementationContract.cs
    |   `-- EntitlementContract.cs
    |-- Federation/
    |   |-- BrainHandleContract.cs
    |   |-- FederatedSynapseContract.cs
    |   `-- PublicKeyContract.cs
    `-- Generated/
        `-- README.md
```

Rules:

- Public contracts contain synapse schemas and neuron interfaces, not private
  implementation.
- Generated contracts come from `.ino` manifests where possible.
- This project must not grow into runtime logic.
- Private bundles may depend on contracts; contracts must not depend on private
  bundles.

## Kernel

```text
kernel/
|-- DigitalBrain.AppHost/
|   |-- DigitalBrain.AppHost.csproj
|   |-- Program.cs
|   |-- DigitalBrain/
|   |   |-- AddDigitalBrainExtensions.cs
|   |   |-- DigitalBrainResource.cs
|   |   |-- DigitalBrainBuilder.cs
|   |   |-- FlutterCompositionBuilder.cs
|   |   `-- Records.cs
|   |-- Resources/
|   |   |-- ResourceCommands.cs
|   |   |-- TopologySimulationCommands.cs
|   |   `-- InoConsoleCommands.cs
|   `-- Tray/
|
|-- DigitalBrain.Runtime/
|   |-- DigitalBrain.Runtime.csproj
|   |-- Neurons/
|   |   |-- Neuron.cs
|   |   |-- Synapse.cs
|   |   |-- SynapseFactory.cs
|   |   |-- NeuronAttributes.cs
|   |   `-- State/
|   |-- Runtime/
|   |   |-- SynapseEnvelope.cs
|   |   |-- IInterpretedNeuronRegistry.cs
|   |   |-- IInterpretedNeuronSource.cs
|   |   |-- InterpretedNeuronRegistration.cs
|   |   `-- NeuronDescriptor.cs
|   |-- Dynamic/
|   |   |-- AuthorInoNeuronRequest.cs
|   |   |-- InoAuthoringProgress.cs
|   |   `-- DynamicSynapseTypes.cs
|   |-- Tasks/
|   |   |-- IDurableTaskCompletionSourceGrain.cs
|   |   |-- DurableTaskCompletionSourceGrain.cs
|   |   `-- DurableTaskCompletionSourceState.cs
|   |-- Marketplace/
|   |   `-- MarketplaceContracts.cs
|   |-- Protos/
|   |-- Security/
|   |-- Ui/
|   `-- Visualization/
|
`-- DigitalBrain.Kernel/
    |-- DigitalBrain.Kernel.csproj
    |-- Cortex/
    |-- Creator/
    |   `-- InoAuthoring/
    |       |-- CreatorInoSystemPrompt.cs
    |       |-- InoAuthoringLoop.cs
    |       |-- InoCreatorNeuron.cs
    |       |-- InoNeuronStore.cs
    |       `-- DynamicGeneratedInoSource.cs
    |-- Gateway/
    |-- Ino/
    |-- Marketplace/
    |   |-- MarketplaceNeuron.cs
    |   |-- LicenseNeuron.cs
    |   |-- LocalBundleInstaller.cs
    |   |-- BundleSignatureVerifier.cs
    |   `-- ContractPublicationNeuron.cs
    |-- Runtime/
    |   |-- InterpretedNeuronRegistry.cs
    |   |-- InterpretedNeuronDispatcher.cs
    |   |-- InoMetadataScanner.cs
    |   |-- InoFilesystemWatcher.cs
    |   |-- ScheduledReminderGrain.cs
    |   `-- SynapseBroadcastRouter.cs
    |-- Simulations/
    |   `-- KernelSimulationHooks.cs
    |-- User/
    `-- Visualization/
```

Rules:

- Runtime owns primitives and ABI-like internals.
- Kernel owns application behavior: Creator, Ino, Gateway, Marketplace,
  Introspector, Brain registry, operational neurons.
- AppHost owns composition only.
- No stale `DigitalBrain.Domains.Dynamic` source project.
- Runtime-generated `.ino` goes under local app data, not committed source.

## InoLang

```text
inolang/
`-- DigitalBrain.InoLang/
    |-- DigitalBrain.InoLang.csproj
    |-- Ast/
    |-- Diagnostics/
    |-- Lexing/
    |-- Linking/
    |-- Parsing/
    |-- Planning/
    |-- Runtime/
    |-- Testing/
    |   |-- ScenarioRunner.cs
    |   |-- SimulationProjection.cs
    |   `-- InoScenarioReport.cs
    `-- Text/
```

Long-term:

- Move parser unit tests into `simulations/DigitalBrain.Simulations`.
- Keep `ScenarioRunner` as the activation gate for `.ino` scenarios.
- Reqnroll should not be referenced from `DigitalBrain.InoLang`.

## SDK

```text
sdk/
|-- DigitalBrain.SDK/
|   |-- DigitalBrain.SDK.csproj
|   |-- Core/
|   |   |-- NeuronBuilder.cs
|   |   |-- ProgrammaticNeuron.cs
|   |   |-- DigitalBrainSdkCore.cs
|   |   `-- ContractsFacade.cs
|   |-- DigitalBrain/
|   |   |-- Ai/
|   |   |-- Identity/
|   |   |-- Marketplace/
|   |   |-- SoftwareEngineering/
|   |   `-- Ui/
|   |-- Microsoft/
|   |   |-- Aspire/
|   |   |-- CSharp/
|   |   `-- Windows/
|   |-- Google/
|   |-- Canvas/
|   |-- CodeGraph/
|   |-- Postgres/
|   |-- Sqlite/
|   |-- Stripe/
|   |-- Telegram/
|   |-- XAI/
|   `-- Testing/
|
`-- digital_brain_sdk_flutter/
    |-- pubspec.yaml
    `-- lib/
```

Rules:

- `DigitalBrain.SDK` is one C# assembly for platform access and sidecars.
- Provider-specific folders are okay; provider-specific projects are not.
- New LLM support should be a new provider/model sidecar inheriting the common
  LLM neuron/contract pattern, not a new runtime abstraction.
- `InoToCSharpTranspiler` should stop emitting `.feature` and `.Steps.cs`.

## Simulations

```text
simulations/
`-- DigitalBrain.Simulations/
    |-- DigitalBrain.Simulations.csproj
    |-- Features/
    |   |-- boot/
    |   |   |-- digitalbrain_boot.feature
    |   |   `-- aspire_lifecycle.feature
    |   |-- ino/
    |   |   |-- creator_authoring.feature
    |   |   |-- ino_activation_gate.feature
    |   |   `-- ino_console.feature
    |   |-- marketplace/
    |   |   |-- bundle_publish.feature
    |   |   |-- private_silo_contracts.feature
    |   |   |-- license_trust_chain.feature
    |   |   `-- install_bundle.feature
    |   |-- runtime/
    |   |   |-- synapse_routing.feature
    |   |   |-- broadcast_synapses.feature
    |   |   |-- neuron_activation.feature
    |   |   `-- durable_tasks.feature
    |   |-- federation/
    |   |   `-- private_cluster_contract_call.feature
    |   `-- ui/
    |       |-- living_canvas.feature
    |       `-- rfw_projection.feature
    |-- Steps/
    |   |-- BrainSteps.cs
    |   |-- BundleSteps.cs
    |   |-- ContractSteps.cs
    |   |-- InoSteps.cs
    |   |-- SynapseSteps.cs
    |   |-- AspireResourceSteps.cs
    |   |-- UiSteps.cs
    |   `-- AssertionSteps.cs
    |-- Support/
    |   |-- SimulationWorld.cs
    |   |-- SimulationBrain.cs
    |   |-- SimulationBundleStore.cs
    |   |-- SimulationSynapseBus.cs
    |   |-- InMemorySimulationHost.cs
    |   `-- IsolatedAspireSimulationHost.cs
    `-- Reports/
```

Rules:

- Every `.feature` uses generic steps.
- No per-neuron `Foo.Steps.cs`.
- Use Gherkin `Feature`, `Rule`, `Scenario`, `Background`,
  `Scenario Outline`, `Examples`, doc strings, data tables, and tags.
- Use tags for layers:
  `@simulation`, `@runtime`, `@marketplace`, `@private-silo`, `@aspire`,
  `@ui`, `@slow`.
- `.ino` scenario blocks remain the activation gate; Reqnroll simulations are
  system and behavior acceptance tests.

Example simulation style:

```gherkin
Feature: Private silo contract call

  Background:
    Given a brain named "consumer"
    And a private provider brain named "provider"
    And the public contracts are installed:
      | bundle                 | version |
      | acme/insurance-triage  | 1.0.0   |

  Rule: Consumers can compile against public contracts only

    Scenario: Consumer calls a private neuron through its public synapse contract
      Given provider has private implementation bundle "acme/insurance-triage"
      When consumer fires synapse "Acme.Insurance.TriageRequest"
        """
        { "claimText": "rear-ended at traffic light" }
        """
      Then provider should receive synapse "Acme.Insurance.TriageRequest"
      And consumer should receive synapse "Acme.Insurance.TriageResponse"
      And consumer should not have implementation source for "Acme.Insurance.Triage"
```

## UI

```text
UI/
`-- flutter/
    |-- pubspec.yaml
    |-- lib/
    |   |-- app.dart
    |   |-- main.dart
    |   |-- router.dart
    |   |-- digital_brain_ui/
    |   |-- features/
    |   |   |-- living_canvas/
    |   |   |-- ino_console/
    |   |   |-- marketplace/
    |   |   `-- simulations/
    |   |-- grpc/
    |   |-- rfw_host/
    |   |-- shell/
    |   |-- telemetry/
    |   `-- theme/
    |-- assets/
    |   |-- rfw/
    |   `-- shaders/
    |-- web/
    |-- windows/
    |-- android/
    |-- ios/
    |-- linux/
    `-- macos/
```

Rules:

- Flutter is a generic shell and RFW renderer.
- No per-neuron Flutter widgets unless they are generic RFW kit components.
- Ino console displays authoring progress by correlation id.
- Marketplace UI consumes bundle/contract synapses, not private implementation.

## Bundles and Runtime Data

Do not commit normal runtime data. Desired local shape:

```text
%LocalAppData%/DigitalBrain/
`-- brains/
    `-- {brainId}/
        |-- brain.json
        |-- bundles/
        |   |-- installed/
        |   `-- private/
        |-- contracts/
        |-- generated/
        |   `-- *.ino
        |-- simulations/
        |   `-- reports/
        |-- state/
        |-- auth/
        `-- logs/
```

Committed examples should be minimal:

```text
examples/
|-- README.md
|-- hello-world.ino
`-- private-silo-contract-call/
    |-- provider/
    |   |-- manifest.json
    |   `-- Triage.ino
    |-- public-contracts/
    |   `-- Acme.Insurance.contracts.ino
    `-- simulation.feature
```

## Delete From Canonical Source

The final repository should not keep these as current source:

```text
scratch/
kernel/DigitalBrain.Domains.Dynamic/Generated/
examples/orleans-setup/
**/bin/
**/obj/
UI/flutter/build/
UI/flutter/.dart_tool/
*.feature.cs
kernel/DigitalBrain.Kernel/**/*.feature
kernel/DigitalBrain.Kernel/**/*.Steps.cs
```

If any item is still needed, move its behavior into:

- `.ino` scenario block for activation gates.
- `simulations/DigitalBrain.Simulations/Features` for BDD simulations.
- `examples/` only if it is a small, current, intentional sample.

## Naming Rules

- Solution: `DigitalBrain.slnx`.
- Product: DigitalBrain.
- Launch: `digitalbrain.cs`.
- Runtime namespace: `DigitalBrain.Runtime`.
- Kernel namespace: `DigitalBrain.Kernel`.
- Public contracts namespace: `DigitalBrain.Contracts`.
- SDK namespace: `DigitalBrain.SDK`.
- Neuron names: `<Capability>Neuron` for C# runtime classes,
  `<Domain>.<Capability>` for `.ino` FQNs.
- Synapse names: request/response/event names that describe the payload.
- No public `Signal` term. Use broadcast synapse.
- No public `Grain` suffix except for Orleans implementation classes.
- No `Manager`, `Helper`, `Util` for new public types.

## Aspire Operating Shape

```text
AppHost
|-- orleans-redis
|-- kernel
|-- digitalbrain-mcp
|-- flutter-web
|-- flutter-windows
|-- ino-llm
|-- private-marketplace-silo
`-- simulation-runner
```

Rules:

- AppHost resources are declared before `Build().RunAsync()`.
- Runtime-generated neurons are hot-registered through Orleans.
- New AppHost resources require AppHost restart or isolated simulation.
- Use resource commands for rebuild/restart/simulate/install/reload.
- Do not use AppHost mutation as the core runtime extension mechanism.

