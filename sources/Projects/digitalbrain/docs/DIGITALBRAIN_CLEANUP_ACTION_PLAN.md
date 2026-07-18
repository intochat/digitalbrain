# DigitalBrain Cleanup Action Plan

Status: architecture cleanup plan, written 2026-06-02.

This plan turns the current repository into a coherent DigitalBrain operating
system: one always-running Aspire composition, neurons as reactive actors,
synapses as the only message/data contract, `.ino` files as executable
experiences, private-silo marketplace bundles with public contracts, and one
simulation-first BDD test harness.

## Decisions

1. `DigitalBrain.slnx` is the canonical solution name.
2. DigitalBrain is the product and operating system. Use supporting terms for
   layers: Runtime, Kernel, SDK, Contracts, AppHost, Brain, Silo, Bundle,
   Simulation. Stop using the old "DigitalBrain as both substrate and product"
   wording.
3. A neuron is the unit of behavior. A synapse is both the data schema and the
   message. Broadcasts are synapses with broadcast routing, not a second
   concept called Signal.
4. An experience is one or more `.ino` files. `.ino` is like `.feature` plus
   runtime behavior: it contains behavior, contracts, UI, scenarios, and
   install metadata.
5. The system runs continuously through Aspire. Source changes should apply by
   rebuilding or restarting the affected Aspire resource. AppHost/topology
   changes require a full AppHost restart or an isolated simulation AppHost.
6. Private marketplace silos are first-class. A bundle may keep implementation
   private while publishing only public contracts: neuron interfaces and
   synapse schemas.
7. Tests become simulations. Reqnroll/Gherkin is used for simulation narratives
   through a generic simulation harness, not as per-neuron `.feature` +
   `.Steps.cs` triplets.
8. Documentation is cut to the current truth. Old v3/v4/v5/v6 research pages
   should be deleted or archived after their useful decisions are folded into
   these three docs and the root `README.md` / `CLAUDE.md`.

## Current Repo Audit

### What already matches the target

- `DigitalBrain.slnx` already exists and includes the active projects:
  `DigitalBrain.AppHost`, `DigitalBrain.Runtime`, `DigitalBrain.Kernel`,
  `DigitalBrain.InoLang`, `DigitalBrain.InoLang.Tests`, `DigitalBrain.SDK`,
  and the Flutter project.
- `digitalbrain.cs` already launches the Aspire composition through
  `builder.AddDigitalBrain()`.
- `kernel/DigitalBrain.AppHost` already declares Redis, Orleans, the kernel
  resource, the local Ollama models, Flutter resources, topology parsing from
  `digitalbrain.ino`, and a dashboard custom command.
- `kernel/DigitalBrain.Runtime/Neurons/Synapse.cs` already has one `Synapse`
  base record with `RoutingMode.PointToPoint` and `RoutingMode.Broadcast`.
- `InoCompiler`, `Interpreter`, and `ScenarioRunner` already exist in
  `inolang/DigitalBrain.InoLang`.
- `InoCreatorNeuron` and `InoAuthoringLoop` already implement the core loop:
  prompt -> `.ino` draft -> compile -> simulate scenarios -> persist ->
  hot-register.
- `DurableTaskCompletionSourceGrain` already exists and is used by the Creator
  path to let authoring work survive restarts better than a plain in-memory
  task.
- Marketplace code is real, not only a memo: `MarketplaceNeuron`,
  `LicenseNeuron`, `LocalBundleInstaller`, `BundleInfo`, signature checks,
  download, install, and marketplace tests exist.
- `InterpretedNeuronRegistry` already supports live interpreted-neuron
  registration and duplicate protection.

### What conflicts with the target

- `CLAUDE.md` still references missing `docs/v3` and `docs/v4` folders and
  still says every neuron is a `.cs` + `.feature` + `.Steps.cs` triplet.
- `README.md` describes deleted or missing projects such as
  `DigitalBrain.Core`, `DigitalBrain.Core.Hosting`,
  `DigitalBrain.ServiceDefaults`, `DigitalBrain.Boot`,
  `DigitalBrain.NeuronTesting`, `DigitalBrain.SDK.Contracts`, and
  `DigitalBrain.SDK.Mcp`.
- `DigitalBrain.Kernel.csproj` still references `Gherkin`, embeds
  `**/*.feature`, and explicitly excludes several `*.Steps.cs` files from
  compilation.
- `kernel/DigitalBrain.Runtime/Runtime/SignalAttribute.cs`, `[Signal]`
  attributes, signal comments, `HandledSignalSubscriptions`, and
  `SynapseBroadcaster` terminology keep the old signal concept alive.
- `sdk/DigitalBrain.SDK/DigitalBrain/INO/InoToCSharpTranspiler.cs` still emits
  Reqnroll `.feature` and `.Steps.cs` artifacts instead of typed C# records and
  neuron classes.
- The tree still contains generated or exploratory material that should not be
  canonical: `scratch/`, `examples/orleans-setup/`, `kernel/DigitalBrain.Domains.Dynamic/Generated/`,
  many `.agents/` report folders, `bin/`, `obj/`, UI build outputs, and bulky
  docs assets.
- The old docs disagree with each other. `docs/v5plan/DOMAINS.md` says domains
  are public GitHub repos and no trust layer; `docs/v6plan/MARKETPLACE.md`
  adds paid signed bundles; the desired direction is private silo marketplace
  with public contracts. Promote the new direction and delete the older split.

## Architecture Review Passes

### Review 1: Naming and repository shape

Finding: the solution name is already correct, but the language around it is
not. The repo should not keep old substrate/product duplication, `BrainOS`
language, missing v3/v4 references, or stale project names.

Decision:

- Keep `DigitalBrain.slnx`.
- Keep `digitalbrain.cs` as the product launch entry.
- Keep `kernel/DigitalBrain.AppHost` because Aspire operations and resource
  restart/rebuild are central to the desired workflow.
- Rename stale docs and comments before renaming code. Names in docs control
  future implementation.
- Do not resurrect old projects just because docs mention them.

Implementation effect:

- Root docs become short and current.
- Namespaces and folders converge around:
  `DigitalBrain.Runtime`, `DigitalBrain.Kernel`, `DigitalBrain.SDK`,
  `DigitalBrain.Contracts`, `DigitalBrain.InoLang`, `DigitalBrain.Simulations`.
- `DigitalBrain.Domains.*`, `DigitalBrain.Core.*`, `ServiceDefaults`,
  `Boot`, `NeuronTesting`, and `SDK.Contracts` references are either removed
  or mapped to the new target.

### Review 2: Reactive neuron/synapse runtime

Finding: the runtime already has `Synapse` with `RoutingMode`, but legacy signal
names remain across attributes, docs, manifests, comments, and tests.

Decision:

- The public model is one message type:
  `SynapseEnvelope { type, payload, metadata, routing }`.
- Broadcast lifecycle/system messages are just synapses with broadcast routing.
- Old `SignalAttribute` and `ContractKind.Signal` language should disappear.
- Activation-time resolution stays. Missing references emit an activation
  failure/unresolved-reference synapse and park the neuron.

Implementation effect:

- Rename `HandledSignalSubscriptions` to `BroadcastSubscriptions` or
  `HandledBroadcastSynapses`.
- Rename `SynapseBroadcaster` only if the resulting code is clearer; otherwise
  keep the class but remove the signal vocabulary from public APIs.
- Update `.ino` grammar and generated docs so `signal(...)` is accepted only as
  a temporary compatibility alias for `synapse(...)`, then remove the alias
  after migration.

### Review 3: `.ino` experiences and Creator flow

Finding: `.ino` already compiles and simulates, and Creator already authors and
hot-registers `.ino`. The missing piece is a single product vocabulary and a
runtime console/log view for the active authoring run.

Decision:

- `digitalbrain.Fire(new InoRequest(userPrompt))` is the conceptual entry.
- `InoRequest` carries prompt, active brain, active context, installed bundle
  contract catalog, current `.ino` files, SDK capability version, and desired
  execution mode.
- Creator emits progress synapses for each step:
  `Prompting`, `Compiling`, `Simulating`, `Activating`, `Failed`.
- The UI/console follows those progress synapses by correlation id, not by
  scraping logs.
- Generated `.ino` files live per brain under local runtime data, not as random
  committed files.

Implementation effect:

- Keep `InoAuthoringLoop` and make it the official forge loop.
- Add or promote `InoRequest` / `InoResponse` contracts.
- Make the "new console starts on my PC" behavior a first-class runtime
  surface:
  either a local console resource in Aspire, or a Flutter/RFW log surface bound
  to `InoAuthoringProgress` by correlation id.
- Record generated `.ino` provenance: prompt, model key, contract catalog
  hash, source hash, simulation report, activation status.

### Review 4: Marketplace and private silo contracts

Finding: the existing marketplace installs `.bdom` bundles and verifies
signature/license, but it is still framed around public domain repositories and
private implementation is not separated cleanly from public contracts.

Decision:

- Marketplace bundle:
  implementation package with private `.ino`, optional C# sidecars, manifest,
  signature, license, and simulation report.
- Public contract package:
  synapse records, neuron interfaces, FQNs, version, capabilities, and required
  SDK/runtime version.
- Private silo:
  the implementation runs in a private brain/silo/cluster, but other brains can
  depend on its public contracts and call it through signed synapse envelopes.
- Contract publication is enough to prove private clusters: a consuming brain
  can compile and simulate against public contracts without seeing
  implementation.

Implementation effect:

- Add one public contract surface. Prefer a small
  `contracts/DigitalBrain.Contracts` project if cross-silo C# references are
  required. Otherwise generate contract JSON/C# from `.ino` manifests and keep
  the solution smaller.
- Current recommendation: add `contracts/DigitalBrain.Contracts` because the
  requested private-cluster proof needs stable public synapse/interface types.
- Move marketplace contracts toward generated contract manifests instead of
  hand-scattered `*SynapseTypes`.
- Keep `LocalBundleInstaller`, `MarketplaceNeuron`, and `LicenseNeuron`, but
  rename docs and APIs from "domain install" to "bundle install".

### Review 5: Aspire lifecycle and dynamic resources

Finding: Aspire gives strong local orchestration and custom resource commands.
Official docs support declaring commands with `WithCommand`, executing commands
through `ResourceCommandService`, and invoking commands from the CLI. They do
not provide a normal path for arbitrary mutation of the Aspire app model after
`builder.Build().RunAsync()`.

Decision:

- Do not depend on dynamic Aspire resource addition at runtime.
- Runtime-dynamic behavior should be `.ino` neuron activation, not AppHost
  mutation.
- If a new external process/container is needed while the app is running, use a
  declared supervisor resource or resource command that launches it, reports
  state by synapses, and persists desired topology for the next AppHost restart.
- If the AppHost graph itself changes, restart the AppHost or start an isolated
  simulation AppHost.

Implementation effect:

- Keep the app constantly running through `aspire start`.
- Use resource rebuild/restart commands for Kernel, SDK, Flutter, and local
  model resources.
- Add explicit resource commands:
  `simulate-bundle`, `install-bundle`, `reload-contracts`, `rebuild-kernel`,
  `restart-silo`, `open-ino-console`.
- For topology changes from `.ino`, write a desired-topology file first, then
  require AppHost restart or run an isolated AppHost simulation.

### Review 6: Simulation-first testing

Finding: the repo has three test styles: xUnit parser/runtime tests, old
Reqnroll example tests, and kernel `.feature` files that are now excluded from
compilation. That makes "green" unclear.

Decision:

- One simulation harness owns all tests.
- `.ino` scenarios are activation gates.
- Reqnroll/Gherkin `.feature` files describe user/runtime simulations using
  generic steps. They do not live next to each neuron and do not generate custom
  `Foo.Steps.cs` files.
- Every existing test is migrated into a simulation feature or deleted because
  it duplicates a more useful simulation.

Implementation effect:

- Add `simulations/DigitalBrain.Simulations` as the only BDD test project.
- Keep a generic step library:
  `GivenBrainSteps`, `BundleSteps`, `SynapseSteps`, `InoSteps`,
  `AspireResourceSteps`, `AssertionSteps`.
- Migrate parser microtests into grammar simulation features first, then
  marketplace tests, then Creator tests, then UI/gateway tests.
- Use Reqnroll's documented structure:
  `Feature`, `Rule`, `Scenario`/`Example`, `Background`,
  `Scenario Outline`, `Examples`, tags, doc strings, and data tables.

## Target System Model

### Boot

1. `aspire start` or `dotnet run digitalbrain.cs` starts the AppHost.
2. AppHost starts Redis, Orleans, Kernel, SDK/MCP resource, Flutter resources,
   local model resources, and optional private marketplace silo resources.
3. Kernel loads:
   active brain,
   local generated `.ino` files,
   installed bundles,
   public contract packages,
   SDK built-ins,
   current simulations metadata.
4. `InterpretedNeuronRegistry` registers interpreted neurons and publishes
   their public contract entries.
5. UI opens to the Living Canvas/Constellation and subscribes to synapse
   streams.

### Ino request flow

1. User prompt enters through Flutter, MCP, CLI, or conceptual
   `digitalbrain.Fire(new InoRequest(userPrompt))`.
2. Kernel attaches context:
   active brain id, installed public contracts, installed private bundles,
   current `.ino` sources, recent synapse history, runtime capability version.
3. Ino/Creator drafts a new `.ino` file.
4. The draft compiles through `InoCompiler`.
5. Scenarios run through `ScenarioRunner` or the central simulation harness.
6. If red, Creator feeds diagnostics back to the LLM.
7. If green, the `.ino` is persisted and registered live through
   `InterpretedNeuronRegistry`.
8. A console/RFW surface follows `InoAuthoringProgress` and shows source,
   diagnostics, scenario state, and activation.

### Bundle and private-silo flow

1. Author creates `.ino` experiences.
2. Pack command emits:
   private implementation bundle,
   public contract manifest/package,
   simulation report,
   signature.
3. Marketplace publishes public contract data and stores private implementation
   in the private silo.
4. Consumer installs public contracts first.
5. Consumer can simulate against contracts before buying/private access.
6. Paid/private install verifies marketplace signature and entitlement.
7. Private remote calls use signed synapse envelopes and public contract types.

### Simulation flow

1. A `.feature` simulation declares system state in `Background`.
2. `Rule` groups invariants: activation, routing, marketplace, recovery,
   private-silo, UI projection, Aspire resource lifecycle.
3. Steps build a `SimulationWorld`.
4. Steps install contracts/bundles, start in-memory or isolated Aspire
   resources, fire synapses, and collect emitted synapses/logs/UI cards.
5. Assertions inspect synapse streams, activation status, contract catalogs,
   generated `.ino`, and resource states.

## Phased Plan

### Phase 0: Freeze the truth

Goal: establish the canonical direction without changing behavior.

Actions:

- Add these three docs.
- Update `README.md` to point to these docs and remove references to missing
  v3/v4 docs.
- Update `CLAUDE.md` to remove triplet rules and stale project layout.
- Add a short `docs/README.md` that says only the current docs are canonical.
- Make a deletion list for stale docs and generated assets.

Gate:

- `git diff` contains docs only.
- No source code changes.
- Root docs no longer point to missing canonical docs.

### Phase 1: Repository and solution hygiene

Goal: make `DigitalBrain.slnx` the only solution entry and remove stale
project/path references.

Actions:

- Confirm `DigitalBrain.slnx` loads and builds.
- Remove stale solution entries only if discovered outside the slnx.
- Remove or quarantine generated folders from source control:
  `scratch/`, `kernel/DigitalBrain.Domains.Dynamic/Generated/`,
  old example `bin/obj`, committed build outputs.
- Update `.gitignore` for runtime generated `.ino`, generated manifests,
  kernel port files, Aspire DCP artifacts, Flutter build output, and old
  `.feature.cs` generated files.
- Move old examples that still teach useful ideas into simulations; delete the
  rest.

Gate:

- `dotnet build DigitalBrain.slnx --no-restore` or normal build is green.
- `rg "docs/v3|docs/v4|DigitalBrain.Core|DigitalBrain.ServiceDefaults|DigitalBrain.Boot|DigitalBrain.NeuronTesting|DigitalBrain.SDK.Contracts|DigitalBrain.SDK.Mcp"` has only intentional compatibility notes.

### Phase 2: Public contracts surface

Goal: allow private implementation and public cluster contracts.

Actions:

- Add `contracts/DigitalBrain.Contracts` or generate equivalent contract
  artifacts from `.ino` manifests.
- Define the public ABI:
  `SynapseContract`, `NeuronContract`, `BundleContract`,
  `ContractManifest`, `CapabilityDeclaration`, `SimulationManifest`.
- Decide whether public contracts are C# records, JSON schemas, `.ino`
  contract-only files, or all three generated from the same source.
- Add a rule: implementation bundles may be private, but public contracts must
  be enough to compile simulations and route synapses.
- Add contract versioning:
  bundle id, contract version, runtime version, SDK version, hash.
- Update marketplace publish to store and expose contracts separately from
  implementation zip bytes.

Gate:

- A consuming test can compile a simulation using only public contracts for a
  private bundle.
- A consuming test cannot inspect the private implementation.

### Phase 3: Kill legacy signal vocabulary

Goal: one message model.

Actions:

- Rename public APIs from signal to broadcast synapse:
  `SignalAttribute`, `HandledSignalSubscriptions`, `AuthoredSignalFqn`,
  old docs/comments.
- Keep compatibility aliases temporarily if required by existing `.ino`.
- Update `SynapseEnvelope` metadata to include routing and future federation
  fields:
  origin brain, destination brain, signature, contract version.
- Update parser tests and `.ino` examples from `signal(...)` to `synapse(...)`.
- Update `InoToCSharpTranspiler` so it no longer writes "Then signal".

Gate:

- `rg "\bSignal\b|signal\("` returns only compatibility shims and migration
  tests.
- Broadcast tests pass through the same `Synapse` path as point-to-point asks.

### Phase 4: Ino experience schema

Goal: `.ino` becomes the single source for behavior, UI, contracts, scenarios,
and marketplace metadata.

Actions:

- Promote a stable `.ino` structure:
  metadata, imports, `neuron`, optional inheritance/base type, synapse
  declarations, handlers, UI, scenarios.
- Add contract-only `.ino` support:
  define public synapses/interfaces without implementation.
- Add sidecar support:
  `.ino` says `delegate`, C# sidecar implements handler body.
- Add inheritance vocabulary:
  `neuron OpenAiChat : DigitalBrain.SDK.Core.Llm` or equivalent.
- Resolve the base namespace:
  either `DigitalBrain.SDK.Core.Neuron` as public facade, or
  `DigitalBrain.Runtime.Neurons.Neuron` for runtime-only internals.
- Make `LLM : Neuron` a real SDK pattern:
  new provider neurons inherit from a common LLM base contract and use
  `Microsoft.Extensions.AI` internally.

Gate:

- A new LLM provider can be added by one `.ino` contract plus one C# sidecar,
  with no AppHost/project edits unless a new external resource is required.
- Creator can author a new `.ino` and expose its progress through the console
  surface.

### Phase 5: Private silo marketplace

Goal: marketplace supports local, private, and public bundles without changing
the runtime path.

Actions:

- Rename "domain" install language to "bundle" and "experience".
- Split bundle publish into:
  `PublishContracts`, `PublishImplementation`, `PublishSimulationReport`.
- Keep signatures and license checks from existing marketplace code.
- Add private-silo implementation location:
  local private silo, remote private silo, or marketplace-hosted storage.
- Add `InstallPublicContractBundle`.
- Add `InstallPrivateImplementationBundle`.
- Add `CallPrivateNeuron` simulation using only public contracts.
- Add entitlement and trust checks:
  signed bundle, signed license, signed cross-silo synapse envelope.

Gate:

- Free local bundle installs from disk.
- Paid private bundle requires signature and license.
- Public contracts are installable without implementation.
- Private implementation can be called through its public contract.

### Phase 6: Central simulation harness

Goal: all tests become simulations.

Actions:

- Add `simulations/DigitalBrain.Simulations`.
- Add Reqnroll packages only to the simulation project.
- Add generic step library:
  brain setup, contract install, bundle install, `.ino` authoring,
  synapse fire/ask, resource state, UI card, log/console, assertion.
- Convert existing tests in this order:
  1. Ino parser and scenario runner tests.
  2. Marketplace trust/download/install/checkout tests.
  3. Creator/authoring tests.
  4. Dynamic neuron Orleans tests.
  5. Kernel feature files.
  6. UI/gateway tests.
- Delete per-neuron `.feature` and `.Steps.cs`.
- Update `Directory.Packages.props` so Reqnroll lives only where the simulation
  project needs it.

Gate:

- `dotnet test DigitalBrain.slnx` runs simulations.
- Every green test has a `.feature` simulation or `.ino` scenario.
- No custom per-neuron step files remain.

### Phase 7: Aspire operational loop

Goal: the app stays running; resource changes are explicit and observable.

Actions:

- Document the runtime loop:
  `aspire start`, inspect resources, rebuild/restart resource, read logs,
  run simulation, repeat.
- Add resource commands on Kernel or supervisor:
  `rebuild-kernel`, `reload-contracts`, `install-bundle`,
  `simulate-bundle`, `restart-silo`, `open-ino-console`.
- Separate source-code rebuild from runtime `.ino` hot activation.
- Add a topology-change policy:
  if AppHost graph changes, write desired topology and restart AppHost or run
  isolated simulation.
- Add a simulation AppHost for resource/topology tests.

Gate:

- A normal `.ino` install/activation does not restart Aspire.
- A source change can rebuild/restart only the relevant resource.
- AppHost topology simulations run in isolation.

### Phase 8: Federation and private cluster proof

Goal: prove public contracts enable private clusters.

Actions:

- Extend `SynapseEnvelope` with origin/destination brain and signature.
- Add brain keypair and pinned public-key table.
- Add public contract discovery for remote/private bundles.
- Add ACL:
  private neuron unreachable unless exposed by contract and entitlement.
- Add one local two-silo simulation:
  consumer brain installs only public contract; provider brain runs private
  implementation; consumer fires synapse; provider returns response.

Gate:

- Private implementation is not present in consumer source/runtime data.
- Consumer compiles and simulates against public contracts.
- Cross-silo call rejects if signature, entitlement, or ACL fails.

### Phase 9: Documentation deletion

Goal: remove 99 percent of stale docs.

Actions:

- Keep:
  root `README.md`,
  root `CLAUDE.md`,
  `docs/DIGITALBRAIN_CLEANUP_ACTION_PLAN.md`,
  `docs/DIGITALBRAIN_CONTINUATION_PROMPT.md`,
  `docs/DIGITALBRAIN_TARGET_TREE.md`.
- Delete or archive:
  `docs/v5plan`, `docs/v6plan`, old research docs, bulky visual assets,
  old superpower plans, stale screenshots, old implementation plans.
- If a doc contains a unique current decision, copy it into this plan before
  deletion.

Gate:

- `docs/` has only canonical current docs.
- `README.md` links to those docs and no missing folders.
- `CLAUDE.md` is short, current, and executable.

## Verification Commands

Use these as gates while implementing. Adjust only when the codebase changes
the command shape intentionally.

```pwsh
git status --short
dotnet build DigitalBrain.slnx --no-restore
dotnet test DigitalBrain.slnx --no-build --max-parallel-test-modules 1
aspire start
aspire ps --format Json --non-interactive --nologo
```

For simulation work:

```pwsh
dotnet test simulations/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj
```

For docs cleanup:

```pwsh
rg "docs/v3|docs/v4|DigitalBrain.Core|ServiceDefaults|NeuronTesting|Reqnroll|\.feature|Signal" README.md CLAUDE.md docs
```

## Risks

- Adding a `DigitalBrain.Contracts` project partially reverses the old v5
  "no contracts projects" rule. It is justified only for public/private silo
  boundaries. Keep it tiny.
- Dynamic Aspire resource addition is not a reliable assumption. Keep dynamic
  runtime behavior inside `.ino`/Orleans; restart AppHost for topology changes.
- Rewriting all tests to simulations is a large migration. Keep the old tests
  temporarily, but every slice must retire at least one old test style.
- The working tree is currently dirty. Do not revert or overwrite existing SDK
  moves unless the user explicitly asks.

## External Sources

- Reqnroll Gherkin reference:
  https://docs.reqnroll.net/latest/gherkin/gherkin-reference.html
- Aspire custom resource commands:
  https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/custom-resource-commands

