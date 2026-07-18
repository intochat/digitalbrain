# ino — architecture spec

Deep design notes for the three primitives and the self-improvement loop. Extracted from CLAUDE.md on 2026-04-12 to keep the working-instructions file lean. This document is the canonical record of decisions verified via Context7.

## Three primitives — recap

1. **Neurons** — Orleans grains, LLM-optional. Pure-code grains participate equally with LLM-powered ones.
2. **Synapses** — typed durable messages that play three roles: signal (delivery), memory (decay), thinking (executable C#).
3. **Self-improving loop** — Aspire + Orleans machinery that creates/rewires neurons without human deploy.

## Synapses — decay model

Every synapse carries `decay ∈ [0, 100]`. Memories are not a separate store — the durable messages themselves ARE the memory.

| Decay | State | Meaning |
|---|---|---|
| 100 | Hot | Just created or recently accessed; actively in context; returned first in recall |
| ~50 | Warm | Searchable, not surfaced in default recall |
| 30 | Cold | Default floor for older synapses; still searchable |
| 1 | Soft-deleted | Invisible to normal search; only retrievable via explicit "include deleted" API |
| 0 | Hard-deleted | Removed from storage |

**Rules (intentionally minimal to start):**

- **Nightly sleep cycle** ("consolidation pass") decrements untouched synapses toward 30, then toward 1 after extended inactivity.
- **Access boost** — any retrieval or reference bumps a synapse back up (minimum ~50). "Recently touched" = "mattered again."
- **Importance signals** — user pins, cross-references from newer synapses, outcome-success feedback lift to 100 or pin permanently.
- **Purge to 0** happens only by explicit action. Nothing is lost without intent.

Default search returns `decay ≥ 30`. Explicit long-search dives into `1-30`. `decay == 0` is gone.

## Synapses — thinking, not orchestration

When a neuron needs branching/loops/arbitrary logic, it fires a synapse that **carries executable C# code**. The code can call other neurons, loop over results, branch on conditions, compose computations. The code IS the neuron's thought, made executable.

This is **not** orchestration in the common sense:

| Agent orchestration (removed) | Synapse-as-thought (kept & reframed) |
|---|---|
| A "boss" agent decides who does what | Each neuron fires its own code-carrying synapses |
| Static workflow graphs | Arbitrary C# — full Turing power |
| Coupling between planner and workers | Thinking is local to one neuron's reasoning |
| Prescriptive | Expressive |

The old `CodeOrchestratorAgent` is the current implementation of thinking synapses. It gets reframed and renamed:

- `CodeOrchestratorAgent` → `SynapseAgent`
- `ICodeOrchestrator` → `ISynapse`
- `OrchestrationResult` → `SynapseResult`
- `iaw/Agents/Orchestration/` → `iaw/Agents/Synapse/`
- Purge "orchestration" from code/comments/prose

The `IAW.slnx` → `ino.slnx` file rename landed 2026-04-10 ahead of schedule. The C# identifier rename is staged migration, not a rewrite.

## Self-improvement loop — three layers (L1/L2/L3)

**Verified 2026-04-09** against `/microsoft/aspire.dev` and `/dotnet/orleans` via Context7. Two constraints are load-bearing:

1. **Aspire's AppHost topology is frozen after `builder.Build()`.** Aspire's own AGENTS.md: *"Changes to the `apphost.cs` file will require a restart of the application to take effect."* There is no public API to add/remove resources dynamically. What IS supported: `app.ResourceCommands.ExecuteCommandAsync(name, KnownResourceCommands.StopCommand/StartCommand, ct)` for individual resource restarts; `WithCommand(...)` for composite stop→mutate→start flows; debugger auto-reattach on project restart.

2. **Orleans grain type manifests are built at silo startup and exchanged between peers at cluster-join.** Loading new grain types into one silo via `AssemblyLoadContext` breaks cluster-wide manifest consistency: silo A's new type is invisible to silo B, so grain references that end up on B can't resolve. "Hot-reload a silo without dropping state" is not a free primitive — either state persists through restart, or new types live on all silos from day one.

The self-improvement loop therefore operates across three distinct layers:

### L1 — new neuron (common case, ~95% of loop operations)

Neurons are persistently-created specialized agents: system prompt + react-loop tool selection + a C# script encoding the neuron's logic. Creating one = writing a row to the cluster-wide `AgentRegistry` grain (`iaw/Core/Registry/AgentRegistryGrain.cs`) with an extended `AgentRecord`. The `NeuronGrain` host type exists in the silo codebase from day one; every silo activates neurons by ID immediately after registration.

**No assembly loading, no silo restart, no Aspire restart.**

Per-change cost: one grain write (~10 ms) + Roslyn script compile on first activation per silo (~100-500 ms, cached after). Cluster-wide visibility via shared storage.

### L2 — reasoning-time C# (thinking case)

A neuron's LLM emits C# code mid-thought to compose a branch, loop, or multi-call sequence that doesn't belong in the neuron's persisted script. This is the current `CodeOrchestratorAgent` / future `SynapseAgent` case (`iaw/Agents/Orchestration/CodeOrchestratorAgent.cs`). Compiled via Roslyn in-grain, ephemeral, never persisted as code — only its effects (fired synapses) persist.

Per-change cost: ~50-200 ms compile. **Not cluster-visible, correctly** — a thought belongs to the one neuron reasoning about it.

### L3 — new compiled capability (rare, human-gated)

A genuinely new compiled tool, grain primitive, or Orleans infrastructure that can't be expressed as a Roslyn script. Requires rebuilding the silo project and rolling-restarting silos via Aspire's `ResourceCommandService`. Orleans supports graceful drain + grain migration during rolling upgrade.

Per-change cost: 1-5 minutes. **Gated on human approval** until the loop's judgment is trustworthy.

**Design implication:** the loop prefers L1 for the overwhelming majority of self-improvement. When ino decides it needs a new specialized neuron, the LLM produces a prompt + tool-selection list + Roslyn script. L2 is the in-reasoning Turing escape hatch. L3 is the rare "ino invented a new primitive" path.

## Synapse persistence — three Orleans primitives

**Verified 2026-04-09** against `/dotnet/orleans`. Plain grain calls (the primitive behind `iaw/Core/Communication/IReceiver.cs` today) are request/response with timeouts — **not at-least-once, not durable, not persisted**. A call lost to a silo crash is lost.

Durability in Orleans comes from three distinct primitives:

- **`Grain<TState>` / `IPersistentState<T>` + `WriteStateAsync()`** — per-grain durable state via Azure Table / Redis / ADO.NET providers.
- **Orleans Streams with a persistent stream provider** (e.g. `AddAzureQueueStreams`) — managed pub/sub with consumer-side checkpoint cursors, batch delivery, multiplexing across queues.
- **`Orleans.DurableJobs`** — explicit at-least-once with retries; idempotency is the handler's responsibility (pattern: `if (await _state.IsProcessed(jobId)) return;`).

The synapse abstraction stitches these together behind one primitive: **`Synapse` (a typed record carrying verb + payload + decay + sender/receiver/timestamp) fired via `INeuron.Fire(Synapse)` to a `NeuronGrain`, persisted as durable state on a `SynapseStoreGrain` partitioned per receiver, with decay updates on every access.** The communication event IS the memory — there is no separate memory store.

`IReceiver<TMessage>` stays as the typed delivery surface; the synapse layer sits beside it and picks the right primitive per synapse kind:
- Orleans Streams for signal delivery
- DurableJobs for exactly-once thinking effects
- `Grain<TState>` for the decay-carrying memory store

## Neuron discovery — split by origin (option ε)

- **Compile-time neurons** (existing `IShell`, `IDotNet`, `IRoslyn`, `IFileSystem`, `IGit`, `IAgentRegistry`, etc. — anything shipped in the silo assembly) keep their typed API: `iaw.Get<IShell>(taskId).ExecuteAsync(...)`. **Zero migration cost for existing code.** Today's `CodeOrchestratorAgent.cs:78-129` "AGENT API — USE TYPED METHODS" prompt stays valid.

- **Runtime-created neurons** (anything added via L1 self-improvement) are accessed via the universal `INeuron` interface: `iaw.Get<INeuron>("neuron-id").Fire(new Synapse { Verb = "open_url", Args = { ["url"] = url } })`. One grain type, unlimited identities.

- **Upgrade path:** a runtime neuron that proves itself can be promoted to a compile-time typed interface via L3 (rebuild + rolling restart). Its synapse shape becomes the method signature. Nothing is stuck in untyped land forever — only until it matures.

- **Discovery:** `IAgentRegistry.HybridSearchAsync(query, embedding)` already exists (`iaw/Core/Registry/AgentRegistryGrain.cs:73-106`) with vector+keyword search over `AgentRecord` entries. Extend `AgentRecord` with a `SynapseSchema` field for dynamic neurons; teach `ToPromptStringAsync` to render both typed-method signatures (compile-time) and synapse-verb lists (runtime) in the LLM catalog.

- **Registration persistence:** `RegisterAsync` today updates only the in-memory dict (`AgentRegistryGrain.cs:20-24`). Extend it to persist via `IPersistentState<RegistryState>` so new neurons survive silo restarts and propagate cluster-wide via shared storage.

- **Dispatch convention:** method-name PascalCase ↔ verb snake_case. `OpenUrl(string url)` ↔ `Synapse { Verb = "open_url", Args = { ["url"] = url } }`. The `INeuron` base handles translation once per activation via Roslyn metadata parsing.

## Synapse schema format — C# interface source as canonical

Evaluated JSON Schema, OpenAI function-calling schema, and C# interface source. **C# interface source wins:**

- **Zero impedance mismatch** — we already execute C# scripts via Roslyn. The schema the LLM reads, the facade parser parses, and the runtime type system are the same language. Nothing translates.
- **Roslyn is already in the codebase** (`iaw/Agents.CSharp/Roslyn/`, exposed as `IRoslyn` via `CodeOrchestratorAgent.cs:99`). Extracting method signatures from an interface source string is ~20 lines of `CSharpSyntaxTree.ParseText` + a `SyntaxWalker`.
- **LLM reads what it writes.** System-prompt catalog shows actual C# interface source with XML doc comments. No mental translation from JSON Schema to C#.
- **Versioning is regular C# versioning** — add method = new verb, rename = breaking, `[Obsolete]` = deprecate.
- **Export is cheap** — JSON Schema for external consumers (e.g. exposing a neuron to OpenAI function-calling) is a derived view generated from the canonical C# interface via Roslyn.

**Storage:** `AgentRecord.SynapseSchema` is a `string` containing C# interface source. A new `ISynapseSchemaParser` uses Roslyn to extract verbs + payload shapes for validation and catalog rendering. JSON Schema / protobuf / OpenAI function-calling schema are all non-canonical export views, not the stored format.

## In-process Roslyn Scripts — canonical neuron template

Today's `CodeOrchestratorAgent` (`iaw/Agents/Orchestration/CodeOrchestratorAgent.cs:202-302`) generates a **standalone C# console project per task** — writes `orchestration.csproj` + `orchestration.cs`, runs `dotnet build` + `dotnet run` as a child process that reconnects to the cluster via `IAWCluster.Connect(args)` (`iaw/Aspire.Client/IAWCluster.cs:23-31`). Per-task cost: seconds minimum, tens of seconds cold. Also csproj-shaped fragility (target framework drift, reference allowlist in `CodeValidator.Sanitize`, removed-usings handling).

Migrate to **in-process `Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript.RunAsync`** compiled and cached inside the firing grain. The script's globals object injects `ClusterClient`, `TaskId`, `ILogger`, and a `NeuronContext` with `Get<T>()` forwarders — no `IAWCluster.Connect(args)`, no child process, no IPC round-trip. Per-task cost drops to ~50-200 ms for a warm Roslyn process. The compiled script delegate is cached per-neuron per-silo; invalidated only when the neuron's script source changes.

**Canonical neuron script template** (shared by L1 persisted neurons AND L2 reasoning-time scripts — one surface to learn):

```csharp
// globals injected by the host:
//   NeuronContext iaw     — cluster client wrapper with Get<T>() forwarders
//   string        taskId  — correlation id for this invocation
//   ILogger       log     — scoped logger
//   Synapse       synapse — the incoming synapse firing this script
//
// return: Synapse (the reply, persisted and decay-tagged by the host)

public async Task<Synapse> Handle() {
    switch (synapse.Verb) {
        case "example_verb":
            var shell = iaw.Get<IShell>(taskId);
            var result = await shell.ExecuteAsync(synapse.Args.GetString("cmd"), ...);
            return Synapse.Reply("ok", new { output = result.Output });
        default:
            return Synapse.Error($"Unknown verb: {synapse.Verb}");
    }
}
```

**Deferral:** migrate after known-problems #1 (Synapse rename), #2 (decay), #3 (consolidation). The current standalone-console path keeps working through the rename pass; the cut-over is mechanical once Roslyn Scripts have a test harness equivalent to the current `CodeValidator` allowlist.

**Load-bearing prerequisite to verify before implementation:** that `CSharpScript` compiled delegates are correctly scoped to the silo process lifetime (not leaked across activations) and that globals-object references don't pin dead grain activations in memory.

## Visualization prototype directions (exploration, not commitment)

Ten directions brainstormed as candidate renderers on top of the `SceneGraph`:

1. Agent Sessions — rooms you enter, not windows
2. Self-drawing futuristic OS — Flutter/CanvasKit, the wow demo
3. DevUI graph-in-TUI — honest baseline, ported from existing SignalR pipeline
4. Text narration — `NarratorAgent` rephrases events in English
5. Icons + primitives — status-bar density with Nerd Fonts
6. Neural map — 2D brain, signal-flow view, pulses along synapses
7. Stream of consciousness — vertical timeline of neuron thoughts
8. Holographic layers — translucent z-ordered stack planes
9. Cortical atlas — 3D brain, anatomy view, fMRI-style region glow (three.js)
10. Hex1b declarative shell — pure TUI baseline (https://hex1b.dev)

**Build order:** #10 (baseline defines `SceneGraph` + composer + renderer pipeline) → #4 (near-free once pipeline exists) → #2 (wow demo on Flutter/CanvasKit) → #6 + #9 as a sibling pair sharing the layout engine.
