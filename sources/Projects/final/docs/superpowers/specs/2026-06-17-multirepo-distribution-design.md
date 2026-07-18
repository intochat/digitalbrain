# Multi-Repo Distribution Design

**Date:** 2026-06-17
**Status:** Approved (design) — ready for implementation planning
**Source:** Decompose `final/` into open-source repos whose only coupling is the neuron/synapse fabric, proving distribution out of the box, local-first.

## Goal & driver

Split the single `final/` solution into independent open-source repos. The **primary driver is distribution architecture**: the split must demonstrate that synapses cross repo *and* process boundaries out of the box. Everything stays runnable locally from day 0 via one common solution. **NuGet publishing is deferred** — local sources only for now. Apps are not NuGet packages; they arrive via a **private marketplace with public contracts** that the kernel downloads as trusted apps.

The Core Law is unchanged: everything is a neuron or a synapse. The repo split must not introduce any concept that isn't expressible as neuron↔synapse.

## The four repos

```
digitalbrain-protocol   ← the seam (public contracts), depends on nothing
inolang                 ← the language, extracted from Core, depends on protocol
digitalbrain-os         ← kernel + runtime + host + marketplace, depends on protocol + inolang
digitalbrain-apps       ← SDK + domains + .ino/.yaml programs (bundles), depends on protocol (+inolang for compiled apps)
```

### What moves where

`DigitalBrain.Core` is **dissolved** — that is what makes the seam honest. There is no more `Core`.

| Repo | Contains (from today's tree) | Depends on |
|---|---|---|
| **protocol** | `Domain/Events/Synapse.cs` + `SynapseMetadata`, `RoutingMode`, `BrainScope`; `Application/INeuron`, `IHandle<>`, `IEmit<>`; marketplace **public contracts** — `ExperiencePackage`/bundle-manifest type + install synapses (`InstallFromMarketplace`, `PublishToMarketplace`, `ListPublished`, `ExperiencePacked`, `ExperienceListed`, `UiSurface`); the **`IAspire` interface** (restart capability contract) | nothing |
| **inolang** | `Core/Domain/Ino/*` (`InoParser`, `InoAst`, `InoValidator`, `RuleInterpreter`), `Core/Domain/Yaml/*` (`YamlParser`) | protocol |

> **Decision (2026-06-17, Plan 2):** `DigitalBrain.SourceGen` does NOT go to inolang. It is the Orleans dispatch-manifest generator (scans `IHandle<>`) with no relation to or dependency on the Ino language; it is a runtime/dispatch concern and moves with the Orleans runtime into **os** (Plan 3). This supersedes the earlier table entry that placed SourceGen under inolang.
| **os** | `Infrastructure/Orleans/*` (`Neuron` base, `SynapseDispatch`), `Kernel`, `AppHost`, `Aspire.Hosting`, `State`, `UI`, marketplace **installer** neuron, local marketplace registry; **implements `IAspire`** | protocol, inolang |
| **apps** | `DigitalBrain.Sdk` (renamed from `Connectors` — Gmail/Telegram/FileSystem/GoogleAuth + author helpers + IAspire wrapper), `Awesome`, `Ino/Experiences`, `os/*.ino`, `os-on-yaml/*.yaml`, `Clients.Console`, `Clients.Flutter` | protocol (+ inolang for compiled apps) |

### SDK naming resolution

`Connectors` is renamed **`DigitalBrain.Sdk`** — the app-author surface. The old `DigitalBrain.Sdk` (IAspire restart-only surface) folds in. To avoid a dependency cycle:

- The **`IAspire` interface** (a public capability contract) lives in **protocol**, so `os` can implement it without depending on `apps`.
- The **SDK** (apps repo) bundles connector neurons, author helpers, and a friendly wrapper over the IAspire contract.

Resulting dependency direction (no cycle): `apps → SDK → protocol`; `os → protocol` (implements IAspire); `os → inolang`.

## Distribution & local dev — two composable seams

### Compile-time: one common solution

A thin **`digitalbrain-workspace`** meta-repo holds the four repos as **git submodules** plus a root `DigitalBrain.slnx` that `ProjectReference`s across them. A `Directory.Build.props` switch (`UseLocalSources`) flips `ProjectReference` ↔ (future) `PackageReference`.

- **Day 0:** `UseLocalSources=true`, ProjectReference only, NuGet deferred.
- **Later:** flip to PackageReference once packages are published.

This is the "common solution" — clone the workspace, `dotnet build`, everything compiles together.

### Run-time: distribution out of the box

The **os `AppHost`** composes everything as Aspire resources:

- **OS silo** — kernel + marketplace installer neuron.
- **Local marketplace registry** — a folder/endpoint serving bundle manifests built from the `apps` repo output. This is the "private marketplace, public contracts": the registry speaks only protocol types; the kernel downloads trusted apps from it.
- **Apps**, two delivery modes (both valid simultaneously):
  - **Separate Aspire-orchestrated silo** — app runs in its own process, joins the shared synapse stream. Proves cross-*process* distribution locally.
  - **Hot-installed bundle** — installed into the OS silo at runtime, no restart.

A single broadcast that reaches both an in-silo handler and a separate-silo handler is the N+1 distribution proof across a real boundary — locally, from day 0.

## Bundle format — manifest-declared, both kinds

A marketplace bundle's manifest declares its **kind**; the kernel installer handles both:

- **Interpreted** — declarative `.ino`/`.yaml` source the inolang runtime interprets at install. Hot-loadable, no compilation. Matches `os/` and `os-on-yaml/` today.
- **Compiled** — a built `.NET` neuron assembly the kernel loads into the silo (built against protocol, optionally inolang). Needs assembly-load handling.

The manifest carries: id, version, kind, declared `triggers`/`emits` (the synapse surface), `system` flag, region. The marketplace serves either payload; the installer neuron resolves kind and wires handlers so the N+1 contract holds for both.

## Testing — the contract is the proof

`DistributionDynamicHandlers.feature` is the executable spec for the marketplace contract and moves to the **os** repo (it exercises kernel + installer + Orleans TestCluster). It must continue to prove: after install, a broadcast reaches **N+1** handlers and the new handler reacts to system events without silo restart.

Extended for the multi-repo world to additionally cover:
- A bundle installed from the **local marketplace registry** (not just an in-test bundle) reaches N+1.
- A handler in a **separate Aspire-orchestrated silo** receives the same broadcast (cross-process proof).
- **Both** bundle kinds (interpreted + compiled) satisfy the N+1 contract.

Each repo additionally keeps its own focused tests: protocol (serialization/round-trip of synapses), inolang (parser/validator/interpreter), apps (per-connector/domain behavior).

## Migration order (high level — details in the plan)

1. **Extract protocol** from Core (synapse vocabulary, INeuron/IHandle/IEmit, marketplace contracts, IAspire interface).
2. **Extract inolang** from Core (`Domain/Ino`, `Domain/Yaml`, SourceGen); depends on protocol.
3. **Re-home os** (Orleans runtime, kernel, host, aspire, marketplace installer); Core disappears.
4. **Form apps** (Connectors→Sdk, Awesome, Experiences, `.ino`/`.yaml`, clients).
5. **Stand up workspace meta-repo** (submodules + root slnx + UseLocalSources switch).
6. **Local marketplace registry** resource in AppHost + manifest format.
7. **Migrate & extend** the distribution feature to prove cross-repo/cross-process N+1.

## Open questions deferred (not blocking)

- NuGet package ids/versioning scheme (deferred — local sources only for now).
- Licensing per repo (separate open-source governance is a future concern, not this design's driver).
- Assembly-load isolation strategy for compiled bundles (resolve during os implementation).
