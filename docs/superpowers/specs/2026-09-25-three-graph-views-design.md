# Three graph views for DigitalBrain

## Purpose

Make the brain inspectable at three scales without losing live neuron calls and signals. A user can switch between an icon-rich whiteboard, a compact graph, and a free spatial 3D scene. Selecting a neuron, route, or activity keeps the same selection and details across views. The source of truth for live activity remains the workspace-scoped, bounded observation feed; changing a view does not call neurons or replay signals.

## Current state and problem

`LumenBrainGraph` is a pannable 2D whiteboard with neuron tiles and `NeuronIcon`, but it accepts the older `BrainSnapshot` model. `UiGraph` is a projected canvas with orbit rings; the activity screen uses it with nodes derived from observed events. The activity controller exposes only one `GraphPulse`, chosen from the selected or latest event. Consequently concurrent events are invisible and a published signal only produces a source-local pulse. The UI kit has an `IGraph` neuron with durable nodes/edges and `GraphChanged`, but the activity view does not use that neuron. The historical `three_js` renderer from commit `2ce7b3922` was removed in `bdbf8b98c`.

The icon vocabulary is allowlisted locally. It includes Gmail, Salesforce, Aspire and GitHub assets, but lacks Supabase. An activity event contains identities and types, not a trusted icon path or a complete neuron descriptor. A presentation descriptor must therefore be resolved separately and scoped to the workspace.

## Feature descriptions

### 1. Lumen whiteboard

A 2D, pannable map for reading names, provider icons, status and relationships. Keep its draggable tiles and fit-to-content control. Adapt the current scoped topology/activity projection into its input model; do not revive the legacy `/chats/{name}/brain` polling path. Add visible recent call and publication badges. A signal that has no observed receiver stays on its source tile. Clicking a tile, edge, or activity opens the shared detail panel.

### 2. Compact graph

Keep `UiGraph` for small windows, cards and dense inspection. Remove the decorative orbit hull. Use a stable, viewport-aware cluster layout with pan, zoom, fit and node labels that do not overlap at normal density. Show the same allowlisted `NeuronIcon` inside or beside every node, with a generic fallback. Display several simultaneous activity markers, with distinct call, publication and failure styling. Preserve keyboard and reduced-motion alternatives.

### 3. Spatial 3D graph

Restore the `three_js` scene as a UI library renderer with orbit camera, zoom, fit, picking and stable spatial positions. No fixed circular cage or mandatory spherical distribution: place neurons in clusters in open space, preserving positions as activity arrives. Use icons as camera-facing labels or sprites, backed by the same local icon descriptor as the other views. Calls travel as directed particles from observed source to target. `SignalPublished` flashes at its source; only a separately observed delivery may travel to a receiver. Multiple events animate concurrently. Selection focuses the camera and opens the same activity details; pause/replay uses retained observations only.

## Shared model and neuron boundary

Keep `IGraph` as a reusable UI kit neuron for durable graph definitions: title, nodes, edges, presentation keys, layout hints and version. Extend its serialized state compatibly; retain `GraphChanged` for definition revisions. Add a distinct transient `GraphActivityObserved` signal or equivalent scoped live channel for display events, with event ID, source/target, kind, status and sequence. Do not persist every animation frame or every high-volume activity event in `GraphState`. The observation feed remains authoritative for ordered history, reconnection and gap detection. The graph neuron may bind to an activity feed by scoped reference, but must not publish invented delivery edges.

The Flutter UI library owns `GraphViewMode` and one shared graph view model. The product activity screen maps its scoped snapshot/events into that view model and offers a Whiteboard / Graph / 3D selector. Selection, filters, pause, replay cursor and detail panel live above the renderers. Switching modes preserves them. The kit neuron is usable by other surfaces without granting them unscoped access to the product activity feed.

## Activity view switching

The Activity screen has a visible three-option segmented control above the visualization: **Lumen**, **Graph**, and **3D**. The chosen view occupies the same central area; the activity list and detail panel stay in place. Switching is immediate and does not start a new feed subscription, clear history, or replay old events as new live activity. The selected event/neuron/route, search and status filters, Live/Pause state, and replay cursor are shared. Each view maintains its own camera, pan/zoom, and local node positions while the screen remains open; returning to a view restores that viewpoint. A per-view Fit action resets only its camera or viewport. The selected mode may be remembered per workspace across Activity screen visits.

On narrow windows the selector remains visible above the graph and the existing list/details area remains below it. The selector is keyboard accessible and announces the active mode. If a 3D renderer cannot initialize, show an error in that visualization pane with a one-click switch to Graph; keep the feed, list, details and other modes usable.

Use explicit `iconKey`/module descriptors from the server where available. Resolve keys through a fixed local allowlist; never treat a server string as a file path or URL. Add Supabase to the allowlist with a bundled asset and generic fallback for unknown providers. Labels and icons remain accessible in all three views.

## Live signal rules

- A published signal is visually distinct from a call and is always shown at the publishing neuron, including when no receiver is known.
- A traveling signal requires an observed delivery, not merely a configured subscription or a matching type name.
- Concurrent calls and publications have separate animation instances keyed by event ID. Terminal events resolve the matching operation; they do not replace unrelated activity.
- The activity list records every retained event; animation is a visual summary and may be rate-limited under load. A visible count conveys coalesced events.
- On pause, animations stop at the selected cursor; on reconnect gap, show the gap and do not draw a guessed path. Reduced motion uses static highlights and ordered steps.

## Acceptance

With two calls and a publication arriving close together, all three views show the involved neurons with matching icons and expose all three events in the list. The compact and 3D views show concurrent markers; Lumen shows source activity. Selecting an event highlights its actual route and details in any view. A publication without delivery never draws a receiver path. Switching views retains selection and replay position. Unknown icon keys render a generic icon. The graph remains navigable with keyboard and reduced motion. The existing bounded history, scope isolation and gap behavior continue to hold.
