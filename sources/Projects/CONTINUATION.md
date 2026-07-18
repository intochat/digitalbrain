# NeuroOS — Build Continuation (fresh, typed-C#, feature-by-feature)

> For the engineer/colleague starting this build. Read `docs/projects-survey-comparison.md` first — it maps
> every feature to the **best-of-breed reference tree** to harvest it from. This repo is a consolidation of
> several prototypes (`ino`, `final`, `v4`, `v3`, `digitalbrain`, `self-improving`, `IAW`) kept as **reference
> only**. We are building a clean implementation, one layer at a time, into a fresh `src/`.

## Mission
Build NeuroOS — an LLM-embodied "operating system" where **everything is a Neuron** (an Orleans grain) that
communicates by firing **Synapse** records — as a clean, **strongly-typed C#** codebase, layer by layer, with
**marketplace distribution + self-update (including the kernel itself)** as a first-class concern from day one.

## Non-negotiable invariants
1. **Typed C# only. NO INO lang / no DSL for behavior.** Packs are C# (Roslyn-compiled). The prototypes that
   leaned on a `.ino`/INO language (`digitalbrain`) are reference for *ideas*, not for the authoring model.
2. **Neuron/Synapse core:** `INeuron` (Orleans grain), `Synapse` records carrying **`correlationId` + `causationId`**,
   dual **incoming/outgoing journals** as the source of truth (replayable).
3. **Distribution-first:** every layer ships as an **installable, signed, updatable pack** — *including the kernel*.
   Updating the kernel must work via the marketplace (sign → publish → install → Collectible-ALC hot-load or Aspire
   resource restart), proven by a test.
4. **TDD on a live substrate:** real Orleans `TestCluster`, no mocks for the core loop (see `v3`'s `Simulation`
   harness and `final`'s Reqnroll distribution feature). Run tests at high severity; a layer is not "done" until
   its tests are green AND it is packaged + installable + updatable with a test proving it.

## The keystone gap (build this EARLY — nobody had it end-to-end)
A single **typed-C#** chain, **tested as one flow**:
> sign a typed-C# pack → install into the **already-running** cluster → **Roslyn compile** → **Collectible ALC**
> load → register as an Orleans grain → dispatch a synapse → assert the response in the timeline → unload on update.

Harvest: `v4` `CollectibleAssemblyLoadContext` (compile/load/unload, tested) + `v3` `GateNeuron`
(parse→compile→ALC→run embedded scenario→`NeuronActivated`) + `final`'s `DistributionDynamicHandlers.feature`
(16 scenarios, N+1 handler-growth proof) as the test template. Make Core/Kernel themselves ship through this chain.

## Build order (feature by feature — each gets: brainstorm → spec in `docs/specs/` → plan → TDD → review → ships as a pack)

1. **Core** — `INeuron`, `Synapse` (`correlationId`/`causationId`), `Neuron` base (dual journals, checkpoint hooks),
   `NeuronId`. Harvest `final`/`self-improving` `DigitalBrain.Protocol` + `IAW` static-virtual typed metadata.
   *DoD:* live-substrate Ping/Greeter tests (broadcast + Ask/Reply + negative), correlation/causation traced.
2. **Distribution engine (the keystone above)** — typed-C# pack → Roslyn → Collectible ALC → grain → dispatch →
   update/unload, signed, tested end-to-end. *DoD:* `final`-style Reqnroll proves N+1 growth from a dynamically
   *compiled* pack (not a pre-known type).
3. **Kernel** — `IKernelTask : Neuron { progress }`, Reqnroll interpreter/testing, **Checkpoint** (state snapshot),
   **Branching** (replay into a branch grain), **Self-update** (Aspire restart). *And the kernel ships as a pack*
   updatable via #2. Harvest `current digitalbraintech` (`CreateCheckpointAsync`/`BranchAsync`), `final` (`ForkBrain`),
   `digitalbrain` (encrypted checkpoint).
4. **SDK — typed integration neurons** — `IFileSystemNeuron`, `IAspireNeuron`, `IShellNeuron`, `IRoslynNeuron`,
   `IGitNeuron`, `IDotNetNeuron`, `INuGetNeuron`, `IWingetNeuron`. Harvest **`IAW`** (the model for typed,
   compiler-verified, zero-reflection integration neurons).
5. **Marketplace** — publish/install, **signing + trust** (Ed25519 from `final` or ECDSA from `digitalbrain`),
   license + economics (**Stripe** + Google auth from `digitalbrain`).
6. **MCP** — server exposing neuron tools over **HTTP transport** (remote-reachable). Harvest **`IAW`** (production
   HTTP MCP). Co-host in the silo where possible (avoid cross-process Orleans-client networking).
7. **Ino** — personal-assistant neuron + **Context neuron** backed by a **graph and/or vector store** (Qdrant + RAG).
   Harvest **`IAW`**.
8. **Awesome** — `WingetNeuron`, "engineering team" / "flutter engineering team" experiences (harvest `final`'s typed
   `SoftwareEngineeringTeam`), and **`IWasm`/WASM** sandboxed embodiment (**net-new — nobody has it**; only build if
   sandboxing is required).
9. **UI Kit** — typed widgets (Button/Text/onClick), a **`Chat : Neuron, IHandle<VisualizeStructuredData>`**, and
   **server-driven UI** (RFW + bidirectional gRPC `UiGateway`). Harvest **`digitalbrain`** (RFW+gRPC) + the
   `digitalbrain-app` Flutter client.

## Definition of done (every layer)
- Strongly-typed C#, no INO-lang.
- Live-substrate tests green (high severity).
- **Packaged as an installable + updatable pack** with a test proving install→embody→update through the
  distribution engine (#2) — the kernel included.

## Reference map (harvest from)
| Need | Reference tree |
|---|---|
| Purest typed-C# model | `IAW`, `final`, `v3`, `self-improving` |
| Core + N+1 dispatch | `final` / `self-improving` |
| Embodiment (Roslyn + Collectible ALC) | `v4` + `v3` |
| Most-tested live distribution | `final` |
| Marketplace trust + economics | `digitalbrain`, `final` |
| Typed integration SDK neurons | `IAW` |
| MCP over HTTP | `IAW` |
| Context graph/vector | `IAW` |
| Server-driven UI | `digitalbrain` (+ `digitalbrain-app`) |
| Checkpoint / Branching | `current digitalbraintech`, `final` |

See `docs/projects-survey-comparison.md` for the full matrix and exact file paths, and
`docs/distribution-algorithm-pass.md` for the Elon Steps-1/2 reasoning behind "install → embody" being the keystone.
