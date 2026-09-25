# Three Graph Views Implementation Plan

> **For agentic workers:** Implement this plan task by task. Keep commits focused and run the listed tests before each commit.

**Goal:** Offer Lumen, compact and spatial 3D views of the same scoped neuron activity, with shared provider icons and visible concurrent signals.

**Architecture:** Retain the bounded activity feed as the event source. Extend the UI kit graph neuron for durable definitions and add a transient activity notification; map both into one Flutter view model consumed by three renderers. Keep selection and replay state in the activity screen, outside renderers.

**Tech Stack:** Orleans/C# contracts and signals; Flutter/Dart UI library; historical `three_js` renderer from commit `2ce7b3922` as a porting reference.

**Spec:** `docs/superpowers/specs/2026-09-25-three-graph-views-design.md`

## Global constraints

- Work on the current `codex/neuron-activity-graph` branch unless the user redirects.
- Keep history bounded and workspace scoped. Never persist animation frames or infer signal delivery.
- Use only allowlisted local icons; unknown keys get a generic icon.
- Respect pause/replay, gaps, reduced motion and keyboard access.
- Port the historical 3D code selectively; do not restore dead routes or duplicate graph contracts.

## Review focus

- A publication with no known receiver must appear at the source and have no traveling route.
- Several events in one animation interval must remain individually inspectable.
- A feed gap or mode switch must not turn an old event into a new live pulse.
- Unknown or malformed icon keys must never load arbitrary assets or URLs.
- A large or sparse graph must remain navigable without fixed orbital bounds or unreadable labels.

## File map

| Area | Responsibility |
| --- | --- |
| `src/Modules/Google/Flutter/Contracts/Graph/` and `Flutter/Graph/` | Versioned durable graph definition and neuron change signal |
| `src/Modules/Google/Flutter/app/ui/lib/src/components/graph/` | Shared graph model, compact renderer, spatial renderer, animation state |
| `src/Modules/Google/Flutter/app/ui/lib/src/lumen/` | Whiteboard adapter and shared icon vocabulary |
| `src/Modules/Google/Flutter/app/shell/lib/workspace/activity/` | Feed-to-view mapping, mode selector, shared selection and replay |
| Existing Graph C# and Flutter test directories | Contract, renderer and interaction verification |

## Tasks

### 1. Pin the event and presentation contract

- [ ] Inspect the actual call/publication event schema and source of module/icon descriptors; record which fields are authoritative and how a workspace-scoped lookup works.
- [ ] Add tests for concurrent call and publication events, source-only publication, terminal pairing, and reconnect gap.
- [ ] Define a shared Flutter view model with stable node/edge IDs, allowlisted icon key, call/signal activity instances, sequence and selection IDs. Map bounded activity snapshots into it without creating unobserved edges.
- [ ] Run the focused activity controller tests; commit the model and mapping.

### 2. Make the UI kit graph neuron a complete durable definition

- [ ] Extend `IGraph`/`GraphState` nodes with optional icon key, module and layout hints while preserving old serialized fields and `Render` callers.
- [ ] Add a transient graph activity notification contract only if a graph surface requires neuron-originated updates; include scoped identity, event ID and sequence, and avoid storing every event in grain state. Specify how consumers subscribe and reconnect through the authoritative feed.
- [ ] Keep `GraphChanged` for definition changes; validate node/edge references and icon keys at the boundary.
- [ ] Extend `GraphFacts` and `GraphHttpFacts` for compatibility, state revision and signals; run focused .NET tests; commit.

### 3. Share icons across all views

- [ ] Add a local Supabase SVG and `supabase` to `NeuronIconKind`; test allowlisted lookup and unknown-key fallback.
- [ ] Move icon-key resolution into one shared helper usable by Lumen, compact and 3D graphs. Prefer server descriptors; use exact built-in fallbacks only where legacy snapshots lack keys.
- [ ] Add component tests proving Gmail, Supabase and generic neurons show the same identities in each graph model; commit.

### 4. Improve the compact graph

- [ ] Replace fixed sphere layout and orbit hull in `graph_geometry.dart`/`graph_painter.dart` with stable open cluster positions, fit/pan/zoom and readable labels.
- [ ] Render the shared `NeuronIcon` as a widget overlay at projected node positions; keep selection hit testing aligned after transforms.
- [ ] Replace singular `GraphPulse` rendering with a bounded set of activity instances keyed by event ID; distinguish call, publication, completion and failure. Test two simultaneous events and reduced motion.
- [ ] Run focused Flutter graph tests and commit.

### 5. Restore the spatial renderer

- [ ] Port camera, mesh, edge and raycast concepts from `2ce7b3922` into a new UI library scene behind a small `GraphScene` interface. Check current Flutter and package compatibility before choosing a `three_js` version.
- [ ] Replace the historical spherical shell with stable unconstrained clusters and camera-facing icon/label overlays. Implement orbit, zoom, fit, picking and focus.
- [ ] Animate multiple observed call/signal instances; source-local publication must be visible even without a receiver. Dispose meshes, textures, timers and renderer on mode switch.
- [ ] Add scene adapter tests for layout, picking, pulse paths and disposal; run the Windows Flutter build and relevant UI tests; commit.

### 6. Adapt Lumen to the live feed

- [ ] Map the current workspace-scoped graph view model to Lumen tiles and relationships without depending on the obsolete Brain endpoint.
- [ ] Show call and source-publication activity on tiles/edges; preserve drag positions, pan/zoom and current icon tiles.
- [ ] Test signal-only and call activity, selection, sparse and many-node layouts; commit.

### 7. Integrate the three views

- [ ] Add a visible Lumen / Graph / 3D segmented control to `ActivityView`, in the same visualization pane on wide and narrow layouts. Keep one feed subscription and preserve controller selection, filters, Live/Pause state and replay cursor across switches.
- [ ] Retain each view's local camera/pan/zoom while Activity remains open; provide a Fit action for the selected view. Decide and test per-workspace persistence of the chosen mode.
- [ ] If the 3D renderer fails initialization, show a pane-level error and a Graph fallback action while keeping activity list/details live.
- [ ] Keep activity list and detail panel shared. Add static activity markers and ordered steps when reduced motion is enabled, and a count for coalesced activity under load.
- [ ] Test keyboard mode switching and narrow layout. Verify with a real test neuron that publishes a signal and calls another neuron: all three modes agree on identities, icons, selected route and bounded events. Verify a publication without observed delivery stays at its source.
- [ ] Run focused .NET and Flutter suites, Flutter analyze and Windows build; inspect the three modes in the running app at compact and wide sizes; commit.
