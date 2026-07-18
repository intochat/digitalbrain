# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this workspace is

`E:\Projects` is **not one project** — it is several from-scratch iterations of the *same* vision: an agent operating system whose only primitives are **neurons** (actors/grains) and **synapses** (immutable typed messages), plus a marketplace that installs new behavior as bundles. Each top-level folder is a separate reboot that mined the previous ones for the best ideas. See `README.md` for the full cross-version comparison.

**`final/` is the canonical, current codebase. Start there for all new work.** The other folders are reference/archaeology — read them to recover a pattern, but do not extend them.

| Folder | Role |
|---|---|
| `final/` | **Canonical clean reboot.** Aspire 13 + .NET 11 + clean neuron/synapse core + Reqnroll BDD. All new work goes here. |
| `ino/` (+ `ino/IAW/`) | Strongest E2E (NeuronE2ETest + Playwright over generated UI), multi-domain silos, BDD. Has its own detailed `CLAUDE.md`. |
| `IAW/` | Rich Aspire hosting — LLM tiers, voice, Ollama, Qdrant, Orleans dev. Has its own `CLAUDE.md`. |
| `v1/` | Brain-controls-Aspire (IDigitalBrain restarts live resources), heavy InoLang/`.ino`, kernel runtime. |
| `v2/` | Clean-room manifesto: minimal core, `Simulation = neuron = test`, rich marketplace bundles (interpreted `.ino`, hot reload). |
| `v3/` | Capsule layout (Contracts + Simulations + impl co-located), Ino transpiler sims. |
| `v4/` | Fresh Aspire template scaffold (AppHost + web + api + redis), thin abstractions. |
| `mcps/` | Reusable MCP server configs (context7, aspire, playwright, dart, …) copied into each project. |

Each iteration carries its own `CLAUDE.md`, `Directory.Packages.props`, `global.json`, and `.slnx`. **When working inside a folder, that folder is the repo root** — use its solution, its package versions, and its CLAUDE.md if present. `ino/CLAUDE.md` in particular is deep and authoritative for ino.

## The Core Law (applies to every version)

**Everything is a Neuron or a Synapse. No exceptions.** A neuron is an actor (Orleans grain). A synapse is an immutable typed message, broadcast on a shared timeline or sent point-to-point. `IDigitalBrain` is a neuron; a test `Simulation` is a neuron; the marketplace installer is a neuron that adds neurons. If a concept cannot be expressed as neuron↔synapse, it does not belong in core. The marketplace proof is dynamic: after installing a bundle, the *same* broadcast must reach **N+1** handlers and the new handler must react to system events — without any silo restart.

## Working in `final/` (the canonical project)

From `final/`. aspire.config.json points CLI at AppHost.

Fast loop: `dotnet run start.cs` (REPL client) + targeted `dotnet test ... --filter`.

Full: `aspire run` when touching hosting/resources. `dotnet test` for Reqnroll gates. `aspire ps/logs` for inspection.

Resource restart: aspire CLI or MCP cmds when present.

### final's architecture (the big picture spanning files)

DDD layering inside `src/DigitalBrain.Core`:

- **`Domain/Events/`** — the synapse vocabulary. `Synapse.cs` is the abstract base record: every synapse carries `SynapseMetadata` (ids, correlation/causation, caller, receiver, `RoutingMode`, `BrainScope`) and a `Stamp(firing, incoming)` method that threads correlation/causation lineage. All synapse types are `[GenerateSerializer]` records deriving from `Synapse`.
- **`Application/`** — the neuron contracts. `INeuron : IGrainWithStringKey`. Wiring is **declared on interfaces**: a neuron implements `IHandle<TSynapse>` for each synapse it consumes and `IEmit<TSynapse>` for each it produces — these are scannable so the system can build a dispatch manifest and prove handler counts.
- **`Infrastructure/Orleans/`** — the runtime. `Neuron.cs` is the abstract base grain. It subscribes to the broadcast **timeline stream** on activation, filters incoming synapses to only the types it `IHandle<>`s, and exposes the fire verbs: `Emit` (broadcast), `Ask` (p2p), `Reply` (back to caller). It also re-broadcasts `Activated` / `SynapseIncoming` / `SynapseOutgoing` wrapper events so all traffic is observable on the timeline, and enforces a `MaxDepth` recursion guard via `RequestContext`. `SynapseDispatch.cs` resolves `IHandle<>` methods, preferring a source-generated `DigitalBrain.SourceGen.DispatchManifest` (frozen dictionaries, pre-resolved invokers) and falling back to reflection over interfaces.
- **`State/`**, **`UI/`** — durable neuron state and widget/card producers.

The **dispatch manifest** is the performance + provability seam: `DigitalBrain.SourceGen` (incremental source generator) emits the neuron→synapse handler map at build time; `SynapseDispatch` consumes it if present, else scans interfaces. Editing handler wiring means re-checking both the `IHandle<>` interface and that the generator picks it up.

Project graph: `DigitalBrain.AppHost` is a **deliberately thin** Aspire host — it reflection-loads `DigitalBrain.Kernel.KernelHost.RunAsync` (which hosts the Orleans silo + `IDigitalBrainGrain` + `AspireGrain`) and is driven by `DIGITALBRAIN_*` env vars for per-world clustering. The *brain neuron* — not the AppHost — owns real orchestration decisions (launching worlds, restarting resources). `DigitalBrain.Sdk` holds `IAspire` (restart-only surface). `DigitalBrain.Clients.Console` + `start.cs` are thin `INeuron` clients.

### Tests are executable specs

`src/DigitalBrain.Core.Tests` uses Reqnroll over real Orleans TestCluster. The DistributionDynamicHandlers.feature proves the marketplace contract: after install, broadcast reaches N+1 handlers and the new one reacts on system events.

Use Imposter for fast mocks; real Simulation substrate for integration proofs. Extend the feature for core distribution changes.

## Per-project conventions (from final/README.md and history)

- Core Law: neuron or synapse only.
- Latest packages via central Directory.Packages.props.
- Self-explanatory names; no boilerplate summaries.
- Relevant tests for the change. Fast path: start.cs + targeted dotnet test. Full aspire only for hosting work.
- Relative paths in final/.

## Reference docs

`final/docs/01-10*.md`: lineage + design. Read 01-03 before deep core changes.
