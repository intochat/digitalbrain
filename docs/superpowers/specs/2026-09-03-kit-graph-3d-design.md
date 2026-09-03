# 3D graph kit component with a backing graph entity

**Date:** 2026-09-03
**Status:** design, awaiting approval

## Goal

Ship a production-grade 3D graph component in `digitalbrain_ui_kit`, with
first-class navigation between nodes, backed by a named kit entity the same
way charts, images and spreadsheets already are.

"Ready to go" means: the assistant can call one tool, and a navigable 3D graph
appears as a live card in chat, on a surface, or in the gallery — reading its
state from the silo, not from the message.

## Why the existing pieces are not enough

`kit/lib/src/components/graph/` today is a 2D depth-projected `CustomPainter`
(`kit_graph.dart`, `graph_geometry.dart`, `graph_painter.dart`). It renders,
rotates by drag, and exposes `onNodeTap`/`onEdgeTap` — but nothing calls those
callbacks, there is no camera, no focus, no navigation history, and no backing
entity. `graph_home_screen.dart` builds its nodes inline from chat turns, so a
graph cannot outlive the turn that produced it or be referenced by name.

`D:\Projects\ino\clients\ino.flutter\lib\screens\shell\shell_brain_canvas.dart`
(701 lines, `three_js: ^0.3.0`) is the opposite: a real 3D scene with cluster
placement, orbit controls, raycaster picking returning a typed
`NeuronPick`/`SynapsePick`, `focusOnCluster`, `flareNeuron`, comet synapses,
and — importantly — `projectVec3` / `projectVec3WithDepth`, which project a
world point to a screen `Offset`. That last pair is what lets Flutter overlays
sit on top of the scene, and it already exists.

This design harvests ino's scene work and gives it what it lacks: a real
entity, a navigation model, and tests.

## Scope

**In:** the 3D component, its navigation, the `IGraph` entity, the HTTP read
path, one `show_graph` tool, and the chat-card wiring.

**Out:** replacing the shell's navigation, the flight-deck/execution-trail
concepts, workspaces, and animated synapse comets. Comets are a natural second
increment once the component lands; they are not needed for it to be useful.

The existing 2D `KitGraph` stays exactly as it is. It keeps its tests, keeps
working on platforms without a GL context, and shares its data models with the
new component.

## Architecture

### Data model — one model, two renderers

`graph_models.dart` gains two optional fields on `GraphNode`: `cluster`
(grouping/colour key) and `position` (explicit world coordinates). Both default
to null, so every existing call site compiles untouched. When `position` is
absent, a deterministic layout derives one from the node id — stable across
runs, so a graph does not reshuffle when it reloads.

This keeps `KitGraph` (2D) and `KitGraphView` (3D) reading the same
`GraphNode`/`GraphEdge` types. No parallel type hierarchy.

### The component splits into four units

| Unit | Responsibility | Testable without GL |
|---|---|---|
| `graph_layout.dart` | id → deterministic sphere position; cluster placement | yes, pure Dart |
| `graph_camera.dart` | orbit angles, zoom, focus targets, easing | yes, pure Dart |
| `kit_graph_controller.dart` | scene model, selection, neighbours, **navigation history** | yes, pure Dart |
| `kit_graph_view.dart` | the three_js widget; scene build, picking, render loop | smoke only |

All the logic a reviewer cares about lives in the first three. The widget is a
thin shell over an injectable scene, mirroring how `shell_screen.dart` in ino
injects `canvasKey` so tests can substitute one.

### Navigation is the point

`KitGraphController` exposes the navigation surface:

- `focus(String nodeId)` — fly the camera so the node faces the viewer; pushes
  onto the history stack
- `back()` / `forward()` — history, like a browser. This is what "proper
  navigation between nodes" means: you can walk into the graph and get out
- `selected` — the current node, or null
- `neighbours(String nodeId)` — nodes one edge away, split into incoming and
  outgoing
- `path` — the breadcrumb from the root hub to the selection
- `projectToScreen(String nodeId)` — `Offset?` for Flutter overlays

`KitGraphNavigator` is a pure-Flutter overlay widget rendering that surface:
breadcrumb, neighbour chips, back/forward. It contains no 3D code and is fully
widget-testable — so the navigation, the part that has to feel right, is the
part with the best test coverage.

### The entity follows the established pattern exactly

Charts already do all of this; graphs copy it verbatim rather than inventing:

```
GraphState (record, [Alias("ui.graph-state")])
IGraph : IEntity<GraphState>          Render(GraphState)
KitCardKinds.Graph = "graph"
HttpSurfacePaths.KitGraphPath = "/kit/graphs/{graphName}"
  → MapKitEntities: principal-scoped GetEntity<IGraph>(instance).Read()
KitToolSource.show_graph → grain.Render(state) + KitCardOffer(Graph, name, caption)
```

Flutter mirrors it: `ChatGraphOffer.fromJson`, `UiClient.readGraph(name)`,
`KitGraphRefPart` (kind `graph-ref`), a `FutureBuilder` branch in
`kit_chat_builders.dart`, and a `ReadGraph` typedef threaded through
`chat_contracts.dart`.

The wire shape is pinned by `flutter-wire-contracts.golden.json`; the new
aliases (`ui.graph-state`, `ui.graph-node`, `ui.graph-edge`, `ui.graph`) must be
added there and to the alias assertion in
`core/test/wire_contract_golden_test.dart`.

## Testing strategy

The GL context is the only real constraint: `flutter_test` cannot render
three_js. The design routes around it rather than lowering coverage.

- **Pure Dart unit tests** — layout determinism, camera focus angles and
  easing, navigation history (back/forward/truncate-on-branch), neighbour
  resolution, breadcrumb derivation, `KitGraphPart` metadata round-trip,
  `ChatGraphOffer.fromJson`.
- **Widget tests** — `KitGraphNavigator` in isolation; the chat builder's
  `graph-ref` branch in its offline, loading, missing and loaded states.
- **Smoke test** — `KitGraphView` with an injected fake scene, asserting it
  builds and forwards picks to the controller. Never asserts pixels.
- **C#** — entity round-trip, the HTTP endpoint under `KitSurfaceTests.cs`, and
  `show_graph` landing a card on the turn stream under `ChatTurnTests.cs`.

## Risks

**`three_js` on web and Windows.** The kit currently has no GL dependency and
targets both. ino runs `three_js: ^0.3.0` on Flutter SDK `^3.11.0`; this repo is
on `^3.12.0`. Task 4 gates on a real build for both targets before any further
work lands — if web fails, the 2D `KitGraph` is already the fallback and the
component degrades rather than blocking.

**Coverage shifts shape.** `kit_graph_test.dart` covers the 2D painter's
geometry today. The 3D view cannot be covered the same way. Net coverage of
*behaviour* goes up (navigation and layout are properly tested for the first
time); coverage of *rendering* goes down. That is the right trade, but it is a
trade.

**Layout quality.** A deterministic sphere layout is predictable, not pretty,
for dense graphs. Force-directed layout is deliberately out of scope for the
first increment; the `position` field on `GraphNode` is the seam that lets the
server supply better coordinates later without touching the component.

## Out of scope, deliberately

Synapse comets, execution trails, time scrubbing, workspace integration,
force-directed layout, and any change to the shell's tab structure.
