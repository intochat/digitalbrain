# DigitalBrain / NeuroOS Architecture Cleanup Proposal

Date: 2026-07-03

Scope: the `brain/` repository. The parent repository also contains `Projects/`, `app/`, `marketplace/`, and other historical material, but this proposal treats `brain/` as the product/runtime repo and only calls out parent-level coupling where it affects `brain`.

Implementation note, 2026-07-03: the repo was cleaned up and then consolidated back to one canonical `Brain.slnx`. CI and deploy use that same solution with `-p:SkipFlutterBuild=true`, so the Flutter bridge remains visible for local development without requiring `../app` on GitHub runners.

## Current Implementation Status

Done:

- Removed duplicated `.claude/skills` and trimmed `.agents/skills` to repo-local Aspire guidance.
- Removed skill eval fixtures, phantom ignored-only project directories, local build artifacts, and stale `docs/specs` / `docs/plans`.
- Made `Brain.slnx` the single canonical solution with `src`, `integrations`, `hosts`, `tests`, `deploy`, and `clients` folders.
- Updated CI and deploy to test `Brain.slnx` with `-p:SkipFlutterBuild=true`.
- Moved concrete marketplace seeds from `DigitalBrain.Core` into `DigitalBrain.SeedPacks`.
- Renamed product-level Silo restart/deploy language to Kernel while preserving Orleans technical terms.
- Renamed `DigitalBrain.Tests/UnitTest1.cs` to `DigitalBrain.Tests/Kernel/NeuronTests.cs`.

Still left:

- Split `DigitalBrain.Core` into smaller primitive/runtime/pack/UI/system contract packages.
- Split `DigitalBrain.Kernel` into runtime modules and make the host a composition root.
- Make integration project names match ownership boundaries, especially interface-only projects.
- Move remaining demo/sample UI leakage out of Core and gateway paths.
- Split the central test project into explicit feedback-speed lanes.
- Expand deployment beyond the current one-kernel-image MVP if Telegram transport and MCP need independent images.
- Add architecture guard tests for Core and module dependency direction.
- Clean existing nullable/obsolete API warnings.

## Executive Summary

The core architecture has a strong idea: a .NET Aspire + Orleans runtime where neurons are grains, synapses are immutable messages, and packs extend behavior at runtime. That idea is coherent and worth keeping.

The repo structure around it is not coherent enough. The biggest problems are:

- Tooling and agent skill caches dominate the tracked repository: `.agents` has 378 tracked files and `.claude` has 366 tracked files. That is more tracked files than `DigitalBrain.Kernel` and `DigitalBrain.Tests` combined.
- `Brain.slnx` is now the canonical entry point. It references `../app/Flutter.proj`, and headless automation passes `SkipFlutterBuild=true` so CI does not need the sibling app checkout.
- `DigitalBrain.Kernel` is doing too much. It is the Orleans host, gateway, UI backend, pack embodiment runtime, marketplace, LLM adapter, Google/Windows/Developer grain host, economics engine, self-update workflow, and code-foundry executor.
- `DigitalBrain.Core` is called the stable protocol layer, but it already contains many product, demo, UI, marketplace, LLM, authoring, task, DB, charting, and self-update contracts.
- Several top-level project-looking folders contain only build outputs (`DigitalBrain.Contracts`, `DigitalBrain.Sdk`, `DigitalBrain.SourceGen`). They are ignored, but they confuse humans and tooling.
- Durable docs say `docs/specs` and `docs/plans` are deleted after merge, but those directories currently contain plan/spec files.
- Naming still leaks old concepts (`silo`) into deploy scripts, workflow comments, synapses, tests, and runtime restart paths after the Silo -> Kernel rename.

Recommended first move: do a deletion and boundary pass before adding any more features. The target should be a small product tree, a CI-safe solution, and an explicit runtime/module boundary around Kernel.

## Evidence From The Repository

### Solution Shape

`Brain.slnx` now includes:

- Flutter via `../app/Flutter.proj` under a `clients` solution folder.
- Main product projects (`DigitalBrain.Core`, `DigitalBrain.Kernel`, `DigitalBrain.Aspire`, `DigitalBrain.Mcp`, integrations, tests).
- AppHost and ServiceDefaults.
- Deployment project under `/deploy/`.

CI no longer bypasses the solution. It builds/tests the canonical solution while skipping Flutter work on headless runners:

- `.github/workflows/ci.yml` runs `dotnet test Brain.slnx -c Release -p:SkipFlutterBuild=true --filter "FullyQualifiedName!~E2E"`.
- `.github/workflows/deploy.yml` uses the same test lane before publishing the kernel image.

That means `Brain.slnx` is now the canonical architecture boundary again. The remaining risk is that the Flutter bridge must keep honoring `SkipFlutterBuild=true` in CI.

### Tracked Repository Noise

Tracked file count by top-level path:

| Path | Tracked files |
| --- | ---: |
| `.agents` | 378 |
| `.claude` | 366 |
| `DigitalBrain.Tests` | 136 |
| `DigitalBrain.Kernel` | 101 |
| `DigitalBrain.Core` | 35 |

This is the strongest cleanup signal in the repo. Agent/tool skills may be useful, but tracking two large duplicated tool trees inside product source makes search, file counts, onboarding, and architectural review worse.

### Code Distribution

Approximate non-generated C# line counts excluding `.agents`, `.claude`, `.git`, `.vs`, `bin`, and `obj`:

| Project | C# files | Lines |
| --- | ---: | ---: |
| `DigitalBrain.Tests` | 124 | 10,524 |
| `DigitalBrain.Kernel` | 92 | 5,900 |
| `DigitalBrain.Core` | 34 | 3,028 |
| `DigitalBrain.Mcp` | 4 | 478 |
| `DigitalBrain.Telegram.Transport` | 7 | 400 |
| `deploy` | 1 | 299 |
| `DigitalBrain.Windows` | 5 | 246 |
| `DigitalBrain.Google` | 11 | 233 |
| `DigitalBrain.Aspire` | 2 | 235 |
| `DigitalBrain.Context` | 6 | 214 |

Largest source files:

| File | Lines | Concern |
| --- | ---: | --- |
| `DigitalBrain.Core/UiSurfaces.cs` | 1,463 | UI grammar, samples, live-data builders, action helpers in Core |
| `DigitalBrain.Tests/UnitTest1.cs` | 500 | Legacy scaffold name with real tests |
| `DigitalBrain.Tests/Steps/NeuronSteps.cs` | 435 | Large shared BDD step class |
| `DigitalBrain.Kernel/Gateway/GatewayService.cs` | 423 | gRPC gateway plus UI/demo routing |
| `DigitalBrain.Kernel/GeneratedNeuron.cs` | 379 | pack embodiment, seeded sample behavior, dispatch |
| `DigitalBrain.Core/Synapse.cs` | 362 | many unrelated cross-domain contracts |
| `DigitalBrain.Core/MarketplaceSeeds.cs` | 270 | embedded seed pack code in Core |
| `DigitalBrain.Kernel/Program.cs` | 259 | host composition, DI, Orleans, CORS, data protection, Google, marketplace, gRPC |

The line count is not a bug by itself. The problem is that the largest files sit on architectural boundaries and mix responsibilities.

### Kernel Coupling

`DigitalBrain.Kernel.csproj` references:

- Product/integration projects: `DigitalBrain.Context`, `DigitalBrain.Core`, `DigitalBrain.Developer`, `DigitalBrain.Google`, `DigitalBrain.Mcp`, `DigitalBrain.Telegram.Channel`, `DigitalBrain.UiKit`, `DigitalBrain.Windows`, `NeuroOSPrototype.ServiceDefaults`.
- Runtime packages: Orleans server, journaling, Azure storage, streaming, gRPC, MCP, Stripe, Roslyn, Azure OpenAI, Ollama, Microsoft.Extensions.AI.

This creates a monolithic runtime host. Some dependency direction is correct: Kernel should host concrete grains. But the current project has too many policy decisions and feature areas in one deployable assembly.

### Core Boundary Drift

The README says `DigitalBrain.Core` is "pure protocol" (`README.md:9`). In practice, `DigitalBrain.Core/Synapse.cs` contains:

- Core neuron/synapse abstractions.
- Marketplace contracts.
- User/session contracts.
- LLM contracts.
- software team and Ino contracts.
- task contracts.
- NuGet/developer contracts.
- DB/chart/data-visualization contracts.
- self-update contracts.
- demo contracts.

`DigitalBrain.Core/UiSurfaces.cs` also contains a large UI grammar and many sample/live-data builders. `DigitalBrain.Core/MarketplaceSeeds.cs` embeds product pack source and demo/deleted placeholders. This makes `Core` more like `DigitalBrain.Contracts.All` than a stable primitive layer.

### Generated And Ignored Trash

The following project-looking folders currently contain only ignored build outputs:

- `DigitalBrain.Contracts/`
- `DigitalBrain.Sdk/`
- `DigitalBrain.SourceGen/`

All normal projects also have local `bin/obj` folders, plus `TestResults`. These are ignored and not tracked, but they still degrade local scans and confuse architecture review.

### Docs Drift

`docs/SYSTEM_DESIGN.md` says:

- `docs/specs` and `docs/plans` are deleted once a branch merges (`docs/SYSTEM_DESIGN.md:448-451`).
- Anything else under `docs/` is stale and should be deleted, not added to (`docs/SYSTEM_DESIGN.md:458`).

Current repo still has:

- `docs/specs/2026-07-02-*`
- `docs/plans/2026-07-02-*`

Either those represent active work and should be clearly marked, or they are stale scratch and should be removed.

## Target Architecture

The target should keep the current runtime model, but make boundaries visible and enforceable.

```text
brain/
  src/
    DigitalBrain.Primitives/          # NeuronId, TaskId, Synapse base, causal metadata
    DigitalBrain.Contracts/           # Stable runtime contracts split by domain folders
    DigitalBrain.Packs.Abstractions/  # IPackBehavior, BundleManifest, config fields, trust primitives
    DigitalBrain.Ui.Contracts/        # UiWidgetTree, UiSurface, action schema, no samples
    DigitalBrain.Runtime/             # Orleans grain base, dispatch, journals, stream wiring
    DigitalBrain.Kernel.Host/         # ASP.NET/Orleans host, Program.cs, DI composition
    DigitalBrain.Gateway/             # gRPC gateway services and protobufs
    DigitalBrain.Marketplace/         # marketplace neuron, catalog, install/publish policy
    DigitalBrain.Foundry/             # compile, ALC embodiment, sandbox, capability gate
    DigitalBrain.Llm/                 # chat client factory, scoped config, responder neuron

  integrations/
    DigitalBrain.Integrations.Google/
    DigitalBrain.Integrations.Windows/
    DigitalBrain.Integrations.Developer/
    DigitalBrain.Integrations.Context/
    DigitalBrain.Integrations.Telegram/

  hosts/
    DigitalBrain.AppHost/
    DigitalBrain.McpHost/
    DigitalBrain.Telegram.Transport/
    DigitalBrain.ServiceDefaults/

  tests/
    DigitalBrain.Runtime.Tests/
    DigitalBrain.Kernel.Tests/
    DigitalBrain.Integration.Tests/
    DigitalBrain.E2E.Tests/
    DigitalBrain.TestKit/

  deploy/
  docs/
  eng/
```

This does not require one huge move. Start by creating solution folders and CI solutions, then move physical folders once dependencies are clean.

## Cleanup Opportunities

### 1. Remove duplicated agent/tool trees from product source

Priority: P0

Current state:

- `.agents` and `.claude` together account for 744 tracked files.
- They include skill docs, eval fixtures, sample apphosts, sample `.csproj` files, scripts, and references.
- `.agents` and `.claude` largely overlap.

Recommendation:

1. Keep `skills-lock.json`.
2. Keep only repo-specific overrides that are actually needed, for example `.agents/skills/aspire/SKILL.md` if local Aspire behavior matters.
3. Remove duplicated `.claude/skills` unless there is a hard workflow requirement.
4. Remove skill eval fixtures from the product repo. They are not product code.
5. Add a restore command to `AGENTS.md`, for example `npx skills add ...` / `aspire agent init --non-interactive`, instead of committing entire tool distributions.

Risk:

- Agent workflows may depend on local skill copies. Mitigate by doing this in a branch and running the normal agent/bootstrap flow before deletion lands.

Expected result:

- Hundreds of tracked files removed.
- `rg`, file explorers, metrics, and reviews stop being dominated by tool caches.

### 2. Keep one canonical CI-safe solution

Priority: P0

Current state:

- `Brain.slnx` includes `../app/Flutter.proj`.
- CI and deploy now invoke `Brain.slnx` directly with `-p:SkipFlutterBuild=true`.
- `Brain.CI.slnx` and `Brain.Full.slnx` were intentionally retired after the repository was consolidated back to one solution.

Recommendation:

Keep `Brain.slnx` as the only authoritative solution file:

- Include all product, integration, host, test, deploy, and local Flutter bridge projects in `Brain.slnx`.
- Use `-p:SkipFlutterBuild=true` for headless automation.
- Keep the Flutter bridge project strict about honoring `SkipFlutterBuild` so CI remains independent of `../app`.
- Optional future split: add focused `.slnf` filters or test category lanes, not competing canonical solution files.

Expected result:

- The solution architecture becomes inspectable and enforceable.
- CI validates all included projects instead of relying on transitive coverage through one test project.

### 3. Delete empty project-output directories

Priority: P0

Current state:

- `DigitalBrain.Contracts`, `DigitalBrain.Sdk`, and `DigitalBrain.SourceGen` contain only `bin/obj`.
- They look like real missing projects.

Recommendation:

Delete those directories if their source was intentionally removed. If they are planned projects, recreate them properly with `.csproj` and source, then add them to the right solution.

Expected result:

- No phantom architecture.
- Faster and cleaner repo scans.

### 4. Split `DigitalBrain.Core`

Priority: P1

Current state:

- `DigitalBrain.Core` is positioned as the stable primitive/protocol package.
- It contains primitives, marketplace, UI, trust, economics interfaces, demo messages, task contracts, DB/chart contracts, software-team contracts, self-update contracts, and embedded marketplace seed source.

Recommendation:

Split by stability and dependency direction:

- `DigitalBrain.Primitives`: `Synapse`, `SynapseType`, `NeuronId`, `TaskId`, causal metadata.
- `DigitalBrain.Runtime.Contracts`: `INeuron`, `IHandle<T>`, checkpoint/branch contracts.
- `DigitalBrain.Pack.Contracts`: `IPackBehavior`, `BundleManifest`, `PackManifest`, config field schema, trust primitives.
- `DigitalBrain.Ui.Contracts`: `UiSurface`, `UiWidgetTree`, UI action schema.
- `DigitalBrain.System.Contracts`: runtime management and self-update contracts.
- Domain-specific contract packages only when a domain is genuinely shared outside Kernel.

Rules:

- `Primitives` should not reference Orleans if possible.
- Orleans-specific interfaces should live in runtime contracts.
- Demo/sample contracts should not live in stable Core.
- Embedded seed source should move out of Core into seed-pack projects or generated resources.

Expected result:

- Stable package becomes smaller and safer.
- Marketplace packs reference only the minimal contracts they need.
- Core changes stop forcing downstream rebuilds for unrelated UI/demo/marketplace edits.

### 5. Split `DigitalBrain.Kernel` into runtime modules

Priority: P1

Current state:

`DigitalBrain.Kernel` includes:

- Orleans host startup.
- Grain base and synapse dispatch.
- gateway gRPC services.
- UI stream buses and RFW bridge.
- marketplace neuron.
- generated pack embodiment.
- LLM responder and scoped client factory.
- Google grains.
- Windows/developer grains.
- code foundry, compiler, sandbox, deployment hooks.
- economics and Stripe.
- context/memory grains.

Recommendation:

Do not split by folders only. Split by deploy/runtime responsibility:

- `DigitalBrain.Runtime`: `Neuron`, journals, `SynapseDispatch`, `SynapseStream`, stream subscribers, checkpoint protectors.
- `DigitalBrain.Kernel.Host`: `Program.cs`, Kestrel, Orleans configuration, DI composition, health/CORS/gRPC-web.
- `DigitalBrain.Gateway`: `GatewayService`, `UiGatewayService`, protos, gateway-specific resolver.
- `DigitalBrain.Marketplace.Runtime`: `MarketplaceNeuron`, catalog, install/publish policy, seed provider.
- `DigitalBrain.Foundry.Runtime`: `PackAlcEmbodier`, capability gate, code-run/code-gen/code-deploy neurons.
- `DigitalBrain.Llm.Runtime`: `LlmResponderNeuron`, `DigitalBrainChat`, scoped chat client factory.
- `DigitalBrain.Ui.Runtime`: `FlutterUiNeuron`, `ChatNeuron`, home feed/signal egress buses, RFW bridge.

Short-term rule:

- New feature code should not be added directly to root `DigitalBrain.Kernel/`.
- New features should land in a submodule folder with an explicit service-registration extension.

Expected result:

- Kernel host becomes composition, not feature implementation.
- Tests can target modules without booting the entire Kernel dependency set.

### 6. Make integration projects real ownership boundaries

Priority: P1

Current state:

- Some integration projects contain real logic (`Windows`, `Developer`, `Context`, `Google`).
- Some contain only interfaces (`UiKit`, `Telegram.Channel`).
- Concrete grains still live in Kernel, which is currently reasonable because of Orleans journal/base-class coupling.

Recommendation:

Keep the current dependency direction for now: Kernel hosts grains, integration packages own API clients and testable logic. But make the contract explicit:

- Integration project owns external API clients, options, pure validators, and non-Orleans service logic.
- Runtime module owns Orleans grain wrapper only.
- Interface-only projects should be renamed with `.Contracts` or merged into a clearer contracts package.

Candidates:

- Rename `DigitalBrain.UiKit` -> `DigitalBrain.Ui.Contracts` if it remains only `IFlutterUiNeuron`.
- Rename `DigitalBrain.Telegram.Channel` -> `DigitalBrain.Telegram.Contracts` if it remains only `ITelegramChatNeuron`.

Expected result:

- Less fake modularity.
- Better signal about what can be tested outside Orleans.

### 7. Clean up docs lifecycle

Priority: P1

Current state:

- Durable docs policy says `docs/specs` and `docs/plans` are temporary.
- Current tree still contains several `2026-07-02` spec/plan files.

Recommendation:

Pick one:

- If active: add a small `docs/ACTIVE_WORK.md` index that names the branch/owner/status for each plan/spec.
- If merged/stale: delete `docs/specs` and `docs/plans`, then roll any durable architectural facts into `docs/SYSTEM_DESIGN.md` or `CONTINUITY.md`.

Expected result:

- Contributors know whether a spec is an active decision record or obsolete scratch.

### 8. Finish the Silo -> Kernel rename

Priority: P1

Current state:

Remaining `silo` naming appears in:

- `.github/workflows/deploy.yml`
- `deploy/Program.cs`
- `deploy/DEPLOY-STATUS.md`
- `DigitalBrain.Core/CodeFoundrySynapses.cs`
- `DigitalBrain.Kernel/Foundry/*`
- `DigitalBrain.Kernel/SystemNeurons.cs`
- several test names and step definitions
- `docs/SYSTEM_DESIGN.md` known-gap note says an E2E fixture still waits for `"silo"` (`docs/SYSTEM_DESIGN.md:364-366`)

Recommendation:

- Keep Orleans technical terms where they are accurate (`ISiloBuilder`, `SiloAddress`, Orleans test cluster).
- Rename product/domain terms to Kernel:
  - `SiloRestartRequested` -> `KernelRestartRequested`
  - `RestartResource("silo")` -> `RestartResource("kernel")`
  - Docker repo/comment names from `digitalbrain-silo` to `digitalbrain-kernel` when operationally safe.
- Fix the E2E fixture wait target, then remove the workaround from docs.

Expected result:

- Lower cognitive tax for new contributors.
- Fewer mismatches between AppHost resource names and runtime operations.

### 9. Reduce Core demo/sample leakage

Priority: P2

Current state:

- `UiSurfaceSamples` and demo surface IDs live in `DigitalBrain.Core/UiSurfaces.cs`.
- `DemoMessageSynapse` lives in `DigitalBrain.Core/Synapse.cs`.
- `MarketplaceSeeds` has comments about deleted demo bloat and still embeds concrete seed pack source.
- Gateway code contains demo routes and demo IDs.

Recommendation:

- Move samples into `DigitalBrain.Samples` or test fixtures.
- Keep only reusable UI schema/builders in UI contracts.
- Move seed packs into dedicated pack projects or embedded resources outside Core.
- Gateway should route generic actions, not own demo behavior.

Expected result:

- Core looks like a protocol package again.
- Product demos can evolve without changing stable contracts.

### 10. Split tests by feedback speed

Priority: P2

Current state:

- `DigitalBrain.Tests` is large: 124 C# files, about 10.5k lines.
- It mixes unit, integration, Reqnroll BDD, E2E, gateway, kernel, UI, distribution, foundry, trust, domain, and spike tests.
- Separate integration test projects exist for Google, Windows, Developer, Context, etc., but central tests remain the dominant surface.

Recommendation:

Create explicit test lanes:

- `DigitalBrain.UnitTests`: no Orleans cluster, pure contracts/helpers.
- `DigitalBrain.Runtime.Tests`: Orleans `TestCluster`, no browser/Aspire.
- `DigitalBrain.Gateway.Tests`: gRPC and gateway wire behavior.
- `DigitalBrain.PackAuthoring.Tests`: pack embodiment and BundleHarness.
- `DigitalBrain.E2E.Tests`: Aspire/browser/full distributed tests.
- Keep Reqnroll only for true story-level BDD. Move generated `.feature.cs` into generated output if possible, or keep them clearly separated.

Expected result:

- Faster local loop.
- Cleaner CI filters.
- Easier ownership of failures.

### 11. Decouple deployment from stale naming and one-image assumptions

Priority: P2

Current state:

- Deploy workflow comments and repository names still say "silo".
- Deploy builds and pushes one image.
- Telegram transport is separate in architecture, but deploy workflow comment still frames the simplified flow as one image.

Recommendation:

- Rename deployment resources and comments to Kernel.
- Make image publishing explicit per host:
  - Kernel image.
  - Telegram transport image.
  - MCP host image if needed.
- Keep one-image deployment only if it is an intentional MVP constraint documented as such.

Expected result:

- Deployment architecture matches runtime architecture.

## Proposed Work Plan

### Phase 0: Deletion Pass

Goal: remove noise without changing product behavior.

Tasks:

1. Remove duplicated `.claude/skills` or prove why it must exist.
2. Remove skill eval fixtures from `.agents` if local skills must remain.
3. Delete ignored-only `DigitalBrain.Contracts`, `DigitalBrain.Sdk`, `DigitalBrain.SourceGen`.
4. Delete local `bin/obj/TestResults` with a normal clean command.
5. Resolve `docs/specs` and `docs/plans`: either active index or delete.
6. Rename `DigitalBrain.Tests/UnitTest1.cs` to a meaningful name.

Validation:

- `git status --ignored --short` should no longer be dominated by phantom project folders.
- `rg --files -g "*.csproj"` should show only real project files.
- CI should still pass.

### Phase 1: Solution Boundary Pass

Goal: make build architecture explicit.

Tasks:

1. Keep `Brain.slnx` as the one canonical solution.
2. Include local Flutter through `../app/Flutter.proj`.
3. Include deploy under `/deploy/`.
4. Update CI/deploy to build/test `Brain.slnx` with `-p:SkipFlutterBuild=true`.
5. Add solution folders that reflect target architecture (`src`, `integrations`, `hosts`, `tests`, `deploy`), even before physical moves.

Validation:

- `dotnet build Brain.slnx -p:SkipFlutterBuild=true`
- `dotnet test Brain.slnx -p:SkipFlutterBuild=true --filter "FullyQualifiedName!~E2E"` or equivalent

### Phase 2: Core Boundary Pass

Goal: reduce stable contract blast radius.

Tasks:

1. Move UI schema from `DigitalBrain.Core` into `DigitalBrain.Ui.Contracts`.
2. Move pack/marketplace contracts into `DigitalBrain.Pack.Contracts`.
3. Move demo/test contracts out of Core.
4. Move seed pack code out of Core.
5. Keep compatibility type-forwarding only if packaging requires it.

Validation:

- Pack projects reference the smallest possible contract set.
- `DigitalBrain.Core` line count drops materially.
- No integration package needs Kernel to compile.

### Phase 3: Kernel Modularization

Goal: make Kernel host a composition root.

Tasks:

1. Extract service registration extensions per module.
2. Move gateway services/protos to `DigitalBrain.Gateway`.
3. Move pack embodiment/foundry to `DigitalBrain.Foundry.Runtime`.
4. Move marketplace runtime to `DigitalBrain.Marketplace.Runtime`.
5. Move LLM runtime to `DigitalBrain.Llm.Runtime`.
6. Keep `DigitalBrain.Kernel.Host` as `Program.cs` plus composition.

Validation:

- Kernel host references modules; modules do not reference Kernel host.
- Unit/module tests compile without the full host where possible.
- Aspire AppHost still wires the same `kernel` resource.

### Phase 4: Naming And Deployment Alignment

Goal: align operational names with architecture.

Tasks:

1. Rename product-level `silo` concepts to `kernel`.
2. Fix E2E fixture wait target.
3. Rename container repository and Pulumi variables when safe.
4. Update deploy docs and workflow comments.

Validation:

- `rg -n "silo|Silo"` should only show Orleans technical terms and intentionally retained legacy migration notes.
- E2E fixture no longer waits for missing `"silo"` resource.

## Architecture Guardrails Going Forward

Use these rules to stop the repo from regressing:

1. No new product code under `.agents`, `.claude`, `.superpowers`, `bin`, `obj`, or `TestResults`.
2. No new project-looking folder without a `.csproj` or a README explaining why it exists.
3. `Brain.slnx` must be buildable on a clean CI checkout with `-p:SkipFlutterBuild=true`.
4. `DigitalBrain.Core` cannot take dependencies on feature domains, demos, transport-specific behavior, gateway concerns, or concrete seed packs.
5. `DigitalBrain.Kernel.Host` should compose modules, not implement features.
6. New integration work must define whether it is:
   - pure contract,
   - external client/service logic,
   - Orleans grain wrapper,
   - transport host,
   - marketplace pack.
7. Specs/plans are either active with an owner/status, or deleted after merge.
8. Use `kernel` for product runtime naming; reserve `silo` for Orleans API terminology only.

## Recommended Immediate Pull Request

A good first PR should be deletion-heavy and behavior-preserving:

1. Remove `.claude/skills` if duplicate.
2. Trim `.agents/skills` to only required local overrides, or remove eval fixtures.
3. Delete ignored-only `DigitalBrain.Contracts`, `DigitalBrain.Sdk`, `DigitalBrain.SourceGen`.
4. Delete stale docs/specs/plans or mark them active.
5. Rename `DigitalBrain.Tests/UnitTest1.cs`.
6. Make `Brain.slnx` the single canonical solution and update CI to use it with `-p:SkipFlutterBuild=true`.

This should produce a much cleaner repository without forcing risky runtime changes. Only after that should Core/Kernel splitting begin.
