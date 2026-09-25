# Neuron activity graph

## Intent and scope

Give a developer or operator a live, inspectable view of what DigitalBrain is doing. The first version shows calls to neurons and signals published by neurons. Selecting an activity explains the observed path through the graph. History is bounded in memory and resets on process restart.

This spec concerns observation and visualization. It does not introduce journal persistence, a general Orleans call explorer, graph editing, or automatic replay of grain methods. The graph replays recorded observations only.

## Current checkout

The June 2026 projects survey describes dual journals and a different current repository. This checkout's compiled kernel is smaller: `Neuron` is an Orleans grain with observers, `Signal` is a record, and `Neuron.PublishAsync` publishes to a local hub and observers. Do not treat the survey's journal, synapse, correlation, or causation contracts as existing runtime interfaces here.

Flutter contains `BrainSnapshot`/`BrainActivity` models, a `LumenBrainGraph` whiteboard, and a reusable `UiGraph` with depth projection and node/edge selection. The client still calls `/chats/{chatName}/brain` and `/brain/events`, but current product research records those routes returning 404. The existing Brain UI and models are therefore reusable design material, not proof of an active data pipeline. `GraphOptions.Enabled` exists but is not an activity feed.

## Approach

Three approaches were considered:

1. **Recommended: runtime observation feed plus the existing Flutter projection.** Capture neuron calls and publications once, keep a bounded feed, and render it with a rotatable projected graph. This fits the current stack and keeps the first version small.
2. **OpenTelemetry trace viewer.** It could reuse spans, but trace sampling, duplicated client/server spans, and trace retention do not directly provide the stable topology and activity selection this experience needs.
3. **New full 3D engine and persisted event store.** This supports large spatial scenes and long history but adds rendering and storage work before the observation model is proven.

The first version uses approach 1. The graph must maintain stable positions as events arrive; a call does not rearrange the scene. Depth is represented by the existing projection, rotation, and perspective. A dedicated 3D renderer can replace that view later without changing the activity contract.

## Observation model

One runtime observation module owns a small interface for appending an `ActivityEvent` and reading a bounded snapshot plus subsequent events. Producers do not know about Flutter or graph geometry. Each event has a monotonic sequence within the feed, unique event ID, UTC timestamp, event kind, operation ID, correlation ID when available, source and target neuron IDs when known, method or signal type, and status. Call completion includes duration and a coarse failure category. Payload and result values are absent by default; the UI shows type, identity, timing, and outcome. This avoids making arbitrary application data visible through an inspection surface.

An outgoing call records the observed source-to-target intent. An incoming call records arrival at the target. Matching these under the same operation ID produces one visual call path, rather than two apparent calls. Start and terminal events are separate observations so active calls can be shown. On exception, record failure and rethrow unchanged. Observation never changes the method arguments, result, or call outcome.

Register Orleans outgoing and incoming call filters around grain invocation. Filter to calls involving `INeuron` identities; exclude observer subscription and infrastructure calls from the first version's visual feed. Capture `Neuron.PublishAsync` at its common publication point. A publication is a signal event at the source neuron. A line to a consumer appears only when a corresponding observed delivery exists; publishing alone must not imply delivery.

The feed is scoped to the authenticated workspace or owner used by the product endpoint. Events that cannot be attributed to that scope are excluded from the workspace view. The implementation plan must verify the exact scope propagation path before instrumentation; it must not infer ownership from a display name. The initial deployment target is the current cohosted product runtime. Multi-silo fan-in is a separate extension and must not be claimed as supported by a silo-local buffer.

## Bounded history and transport

Keep at most 2,000 events or 15 minutes of events per scope, whichever limit evicts first. Apply a global memory cap as well so many scopes cannot grow without bound. The module assigns sequences before publication. Reads return the latest bounded snapshot and a cursor; live transport sends events after that cursor. A reconnect supplies the cursor and gets retained events, or an explicit gap marker if that cursor has been evicted. Slow clients cannot block grain calls; when a subscriber overflows, close its stream with a gap indication and require a fresh snapshot.

Expose a scoped snapshot endpoint and one server-sent event stream from the product host. Do not revive the old `/chats/{chatName}/brain` contract unchanged: it models an unavailable legacy backend and does not define the new observation semantics. Endpoint authorization follows the product's existing workspace access rules. The server returns bounded activity and observed nodes/edges, plus `observedAt`, truncation/gap state, and next cursor. No fabricated topology is sent: an edge is an observed call route or known subscription with a distinct kind.

## Interaction

Use a three-area view on desktop: activity list, central graph, and details. Narrow layouts show the graph above a list/details switch. The list has newest-first events, search, type/status filters, and a Live/Pause control. Clicking an event selects its operation or correlation, highlights the relevant neurons and directed path, and opens ordered steps with timestamps, duration, and failure category. Clicking a node or edge filters the list to related observations. A clear selection action restores the full view.

Call start moves a pulse toward the target; completion or failure resolves it there. Signal publication pulses at its source and along only confirmed delivery routes. Persistent edges are visually distinct from transient call paths. Missing sources, unknown targets, evicted steps, and transport gaps receive explicit labels. The UI never draws an inferred successful path through a gap.

Pause freezes the displayed cursor while collection continues. A time scrubber steps through retained observations in their recorded sequence and highlights state at that point. Returning to Live refreshes from the current snapshot. Replay does not call neurons or re-publish signals. Reduced-motion mode replaces moving pulses with static highlights and an ordered step list. The list and detail panel remain usable without manipulating the graph.

## Verification and acceptance

Use a real test neuron that calls another neuron and publishes a signal. Verify that one source action yields a linked outgoing/incoming call, a terminal outcome, and a distinct publication in sequence; selecting it highlights the same route in the list, graph, and details. Test a failure without changing the thrown exception. Test scope isolation, redaction, eviction, reconnect from retained and evicted cursors, slow subscribers, and restart-empty history. Confirm the observer adds no blocking wait or unbounded allocation to the grain call path.

The feature is complete when the product opens a working activity view against its actual endpoints, shows live neuron calls and published signals, supports selection and bounded replay, and explicitly reports gaps. The old dead Brain endpoint calls must be removed or redirected as part of integration so the client no longer polls 404 routes.
