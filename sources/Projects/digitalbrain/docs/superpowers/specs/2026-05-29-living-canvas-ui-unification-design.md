# DigitalBrain — The Living Canvas (UI unification + simplification)

> Design spec. Written 2026-05-29. Builds on v5 "The Cut"
> (`docs/v5plan/VISION.md`) and v6 Phase F (`docs/v6plan/PHASE_F.md`).
> Aligns with invariants **V5-2** (one message type), **V5-3** (no global
> catalog), **V5-4** (UI is data). Where this spec is silent, those win.

## 1. The idea in one paragraph

The home screen *is* the brain: a calm neuron graph on pure-black liquid
glass. Neurons render their **own** mini-app surfaces (RFW cards) — so the
Flutter app holds **no neuron-specific Dart**, only a generic renderer. One
floating dock drives everything, with a single **Assist ⟷ Operate** toggle.
In **Assist** you talk to the brain (personal AI assistant). In **Operate**
the same canvas becomes a **visual constructor**: a `+` button is the one way
to add anything — a **new neuron**, a **new synapse**, or a **reference from
the SDK**. You wire neurons by dragging output→input ports, bind **lifecycle
reactions** (`when X.Activated → …`) and **handlers** (`incoming synapse →
action`) in an inspector, drill into **composite** neurons that encapsulate
whole sub-apps, find anything by **vector search**, and debug against **real
synapse payloads** in a pull-up console with replay and "fire a test synapse."
No `.ino` is required to operate — neurons come from the `NeuronBuilder`
factory. `.ino` / Ino-forge remains the *authoring* path, not a prerequisite.

## 2. Goals / non-goals

**Goals**
- One home surface, two modes (Assist / Operate); no tab bar, minimal chrome.
- Neurons own their UI as RFW data. Flutter = graph + dock + generic RFW host.
- A `+`-driven visual constructor: new neuron / new synapse / reference SDK.
- Wire routes, bind handlers + lifecycle reactions, all in UI.
- Composite neurons (nodes that contain sub-graphs) with drill-in navigation.
- Vector search across neurons / domains / scenarios by meaning.
- Debug drawer over **real** synapse payloads: inspect, replay, test-fire.
- Factory-first: operate DDD-shaped neurons with zero hand-written `.ino`.

**Non-goals (this spec)**
- No NeMo / NIM / Ino fine-tuning (that's Phase F F5–F10; untouched here).
- No 3D constellation rework; `/constellation` stays as-is or is parked.
- No new wire protocol — progress and payloads ride the existing journal.
- No mass-rename (D-A holds: DigitalBrain substrate vs product domain).

## 3. The five primitives (the whole mental model)

Everything in the UI maps to one of five things that already exist in the
runtime. This is the unification — nothing new is invented:

| UI concept | Runtime backing | Source today |
|---|---|---|
| **Neuron** (node / mini-app) | `Neuron : DurableGrain`, `IHandle<T>`, `INeuronMetadata` | `kernel/DigitalBrain.Runtime/Neurons/` |
| **Synapse** (wire / data type) | `abstract record Synapse` (the record *is* the schema) | `kernel/DigitalBrain.Runtime/Neurons/Synapse.cs` |
| **Handler** (`incoming → action`) | `IHandle<TSynapse>` registration | `NeuronBuilder.OnReceive<T>` |
| **Lifecycle reaction** (`when X.Activated → …`) | broadcast synapse on the journal (V5-2: a signal *is* a synapse) | `Neuron` outgoing `IDurableList<Synapse>` |
| **Surface** (the card) | `rfw:` block rendered by the host | `UI/.../rfw_host/`, V5-4 |

The `+` flow only ever creates the first two; handlers and reactions are
bound *on* a neuron; surfaces are authored per neuron. Five concepts, one
canvas.

## 4. Screens & components

### 4.1 Home / Living Canvas (Assist)
- **Full-bleed graph.** Bright orb = talk-to-able neuron; dim = idle/available;
  **amber** = actively working now. Edges = synapse routes; amber dashed = live.
- **Neuron mini-app cards.** Each is a neuron's RFW surface rendered by the
  generic host as a floating frosted card. No per-neuron Dart.
- **Top chrome (minimal):** brain identity + status dot (left); `⌘K` command
  + settings (right).
- **Floating dock (bottom-center):** attach · prompt field · voice mic · amber
  send. Above it, the **Assist ⟷ Operate** segmented toggle.

### 4.2 Operate (the visual constructor)
- Same canvas, editable. Nodes gain **ports** (input left, amber output right).
- **`+` FAB** → popover with three doors:
  - **New neuron** — name + DDD shape (connector / domain / role) → factory node.
  - **New synapse** — fields form → a `record`; appears in the palette.
  - **Reference from SDK** — searchable catalog of `DigitalBrain.SDK`
    connectors (LLM, Google, Telegram, Stripe, SQLite, Windows…), each a real
    C# neuron packing a whole integration.
- **Left palette** = factory neurons + synapse types (drag onto canvas).
- **Right inspector** for the selected neuron:
  - **Handlers** — rows of `incoming synapse → action` (`AnalyzeText → emit
    Summary`); `+ handle a synapse…`.
  - **Reacts to lifecycle** — rows of `when X.Activated → …`, `when any.
    UnresolvedReference → pause & ask me`; `+ react to an event…`.
  - **Surface (RFW)** — edit the neuron's card.
- **Wiring** = drag output port → input port to create a synapse route. Commit
  with ⌘S.

### 4.3 Inspect & Debug (drawer, any mode)
- **Composite drill-in.** Double-click a node → its inner wiring fills the
  canvas; breadcrumb (`Brain / Triage`) navigates back. References to other
  neurons show as dashed nodes with a `references · X` badge. Nesting is
  unbounded (a node can itself be a composite).
- **Debug console** (pull-up bottom drawer, present in Assist *and* Operate):
  - **Live synapses** tab — real stream; each row is a real synapse; click →
    its **actual JSON payload** on the right.
  - **Scenarios / Logs** tabs.
  - **Replay scrubber** over the synapse ring buffer (already in-tree:
    `features/live/timeline/synapse_ring_buffer.dart`, `timeline_strip.dart`).
  - **`✎ edit & fire test synapse`** — hand-craft a payload, inject into a
    neuron, watch the result (TDD-by-poking, no code).

### 4.4 Vector search (⌘K + per-door)
- Semantic search over neuron / domain / scenario names + descriptions via the
  existing `IEmbeddingGenerator`. Powers the `⌘K` palette and the search box
  inside each `+` door (e.g. "summarize my inbox" → Triage, LLM.Summarize).

## 5. The Flutter cut (first slice — highest priority)

Target end state for `UI/flutter/lib`: **graph + dock + generic RFW host +
the constructor surfaces**, nothing neuron-specific.

- **Keep / promote:** `rfw_host/*` (the generic renderer — the load-bearing
  V5-4 piece), `digital_brain_ui/*` (the liquid-glass kit: `GlassMaterial`,
  `LiquidGlassSurface`, theme), `features/brain/widgets/floating_prompt_dock`,
  `features/brain/voice_input`, `features/live/timeline/*` (replay buffer),
  `grpc/*` stubs, `features/neuron_constructor/*` (the visual constructor
  models/state — extend, don't rebuild).
- **Audit for deletion (verify zero refs first):** screen-specific Dart that
  hard-codes neuron UI rather than rendering RFW; redundant
  constellation/brain-scene scaffolding the single canvas replaces; any card
  widget that should instead be an RFW surface.
- **Routing collapses** toward a single canvas route with mode state, not
  three page routes — `/constellation` parked behind a flag, `/brain/:id`
  folded into the canvas.
- **Rule:** the cut is governed by `tools/claude-lsp` find-references, not by
  eye. A widget survives only if (a) it's the generic RFW host, (b) it's in the
  `digital_brain_ui` kit, or (c) it's one of the four constructor/dock/graph/
  debug surfaces. Everything else is suspect.

> This slice ships **before** any new backend work. Success = the app renders
> the home canvas with RFW cards and the dock, with materially fewer Dart files
> than today and no neuron-specific widgets.

## 6. Backend touch-points (additive, minimal)

- **Lifecycle reactions** need neurons to emit `Neuron.Activated` /
  `Neuron.Deactivated` / `Neuron.UnresolvedReference` broadcast synapses (V5-2,
  V5-3). `UnresolvedReference` already specified by V5-3.
- **Constructor → factory.** New neuron / new synapse / wiring map to
  `NeuronBuilder` (`WithName / WithInputSynapse / WithOutputSynapse /
  OnReceive`) producing a `ProgrammaticNeuron`, registered via the existing
  scanner/registry. Composite = a neuron whose handler asks/emits to its inner
  neurons by FQN.
- **SDK reference catalog.** Enumerate `DigitalBrain.SDK` connector neurons +
  metadata for the catalog door (reuse `INeuronMetadata`).
- **Vector index.** Embed neuron/domain/scenario name+description via
  `IEmbeddingGenerator`; query on search. No central catalog persisted (V5-3);
  index is rebuildable at activation.
- **Debug stream.** Surface the journal's incoming/outgoing
  `IDurableList<Synapse>` as a gRPC stream of envelopes incl. payload; the
  replay buffer already exists client-side. Test-fire = a gateway call that
  emits a user-crafted synapse into a target neuron.

## 7. Data flow

```
voice/text ─▶ dock ─▶ gateway (gRPC) ─▶ Ino/target neuron
neurons ─▶ journal synapses ─▶ BrainWatch stream ─▶ canvas (orbs, edges, cards)
                                              └▶ debug console (payloads, replay)
+ New neuron/synapse ─▶ gateway ─▶ NeuronBuilder ─▶ registry ─▶ canvas node
search query ─▶ embed ─▶ vector match ─▶ ⌘K / door results
```

The Flutter client only ever speaks the gateway proto
(`DigitalBrain.Kernel.Contracts/Protos`); it never references domain projects.

## 8. Error handling
- Unresolved wiring → neuron emits `Neuron.UnresolvedReference`, parks in modal
  lock; the inspector shows it as a `when any.UnresolvedReference → pause & ask
  me` reaction (V5-3). No throw across the cortex — emit a failure synapse.
- Bad RFW document → the host degrades to an error string per key (already the
  behavior in `RfwRuntimeHost`), never takes down the canvas.
- Test-fire with an invalid payload → validated at the gateway; returns a
  failure synapse rendered inline in the console.

## 9. Testing
- `dotnet test` only (no `flutter test`, per CLAUDE.md). UI assertions are on
  RFW payload contracts over gRPC in `DigitalBrain.E2E.Tests`.
- New backend seams (lifecycle synapses, constructor→factory, vector index,
  debug stream, test-fire) each get scenario coverage gated red→green.
- Verify the running app via the Aspire Flutter resources + Dart-MCP
  (`flutter-windows`) per the `flutter` skill; batch UI changes, verify once.

## 10. Slices (each shippable; deletion-first per Musk's algorithm)

1. **S1 — The Flutter cut.** §5. Single canvas route + dock + generic RFW host;
   delete neuron-specific Dart; green build. *(Do first.)*
2. **S2 — Assist canvas live.** Graph orbs + edges + neuron RFW cards driven by
   the BrainWatch journal stream; the dock drives Ino.
3. **S3 — Lifecycle synapses.** Emit `Neuron.Activated/Deactivated/
   UnresolvedReference`; canvas reflects state (amber = working).
4. **S4 — Operate mode + `+` flow.** Ports, wiring, the three-door `+`
   (new neuron / new synapse / reference SDK) → `NeuronBuilder`; inspector
   handlers + lifecycle reaction binding.
5. **S5 — Debug drawer.** Real synapse stream + payload inspector + replay over
   the ring buffer + edit-&-fire test synapse.
6. **S6 — Composite drill-in.** Breadcrumb navigation into a neuron's inner
   wiring; reference badges.
7. **S7 — Vector search.** Embed + query neuron/domain/scenario; `⌘K` + per-door
   search.

## 11. Open questions (defaults chosen, flag to change)
- Debug = drawer in any mode (chosen) vs. third mode toggle.
- Composite drill = breadcrumb (chosen) vs. inline expansion.
- `/constellation` (multi-brain) — parked behind a flag this phase; revisit
  with E-MULTIBRAIN.
