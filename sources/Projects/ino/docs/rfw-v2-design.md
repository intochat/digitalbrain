# RFW v2 — generative UI for ino

**Status:** design draft, post-v0.1.
**Phase 3 today:** RFW v1 ships one widget-tree blob per assistant turn. The server emits `parseLibraryFile`-compatible text + a JSON data map; Flutter parses it once, the tree is static for the lifetime of the message.
**The gap:** ino reasons in synapses that fire over time — routing decisions, tool calls, partial results, confirmations. RFW v1 forces all of that into a single end-of-turn snapshot. The user can't *watch* ino think; they get a screenshot.

This doc proposes RFW v2: a small grammar of high-level widgets that the LLM emits as **UiPatch synapses**, streamed into a per-message dynamic tree the Flutter client mutates incrementally.

## Principles

1. **UI mutations are synapses.** Every patch is a typed durable message routed via `IFirePort`. Replay, decay, and Inspector visibility come for free — same primitive as everything else in ino.
2. **Domain-shaped widgets, not visual primitives.** The palette is keyed to the *shapes ino emits* (routing decisions, tool traces, suggestions), not to layout (`Row`, `Container`). RFW v1 keeps owning layout as the escape hatch.
3. **One-way patch stream, idempotent ids.** Each patch has a stable widget id; re-applying a patch is a no-op. Flutter never has to ask the server "what's the current state?" — it derives it from the patch log.
4. **No template DSL on the wire.** v1's `parseLibraryFile` text format is great for humans designing UIs but expensive for LLMs to emit token-by-token. v2 patches are JSON envelopes referencing pre-registered widget types.

## Wire format

A single new gRPC chunk type extends today's `ChatChunk`:

```protobuf
message ChatChunk {
  oneof payload {
    string text_delta = 1;       // existing — token stream
    RfwBlob rfw = 2;             // existing — v1 one-shot
    UiPatch ui_patch = 3;        // NEW — v2 incremental
    // ...telemetry, result_cards, etc.
  }
}

message UiPatch {
  string message_id = 1;         // groups patches into one assistant turn
  string node_id = 2;             // stable id within the message tree
  PatchOp op = 3;                 // append | replace | update | remove
  string widget_type = 4;         // e.g. "RoutingDecision", "ToolCallTrace"
  google.protobuf.Struct props = 5; // widget-specific data
  string parent_node_id = 6;      // empty = root of this message
  int32 index = 7;                // position within parent.children
}

enum PatchOp {
  APPEND = 0;
  REPLACE = 1;
  UPDATE = 2;   // shallow-merge props onto existing node
  REMOVE = 3;
}
```

Server side, an `LlmNeuron` calls a Dart-tool-like API:

```csharp
await ui.AppendAsync("routing", new RoutingDecision {
    Domain = "travel",
    Confidence = 0.87,
    Reason = "matched 'plan a trip'",
});
// later, when a tool call resolves:
await ui.UpdateAsync("routing", new { Status = "complete" });
```

Each call fires a `UiPatchSynapse` that the gateway forwards as a `ui_patch` chunk.

## Widget palette (v2.0 — locked)

Sweet spot per the prior conversation: 6–12 semantic widgets keyed to neuron output shapes, plus two generic fallbacks. Anything outside the palette falls back to RFW v1.

| Widget | Props | Use |
|---|---|---|
| `RoutingDecision` | `domain, confidence, reason, status` | Cortex picked a domain |
| `SynapseTimeline` | `entries: [{kind, neuron, time, summary}]` | live-fire log of synapses for this turn |
| `ToolCallTrace` | `tool, args, result, status, duration_ms` | one row per tool invocation; `status: pending → running → complete/error` |
| `Suggestion` | `text, action: {kind, payload}` | a tappable next step the user can accept |
| `Confirmation` | `prompt, options: [{label, value}]` | user must choose before ino proceeds |
| `MetricStrip` | `entries: [{label, value, unit, trend}]` | small horizontal numeric strip |
| `ProgressNote` | `text, percent?` | "thinking…" / "fetching flights…" placeholder |
| `Markdown` | `body` | streamed prose (token-deltas update body in place) |
| `CodeBlock` | `language, body, copyable` | tool-call arg dumps, exception traces |
| `KeyValueList` | `entries: [{key, value}]` | structured fact dump (place details, ride quote) |
| `Embed` | `url, kind: image\|map\|chart` | external content; client decides how to render |
| `Group` | `title, collapsible, children` | only structural primitive; everything else is leaf |

Outside this set the model is told to use RFW v1 (`Embed{kind:"rfw", url:"inline://..."}`).

## Streaming UX

What "build the tree dynamically" actually looks like for the user:

```
T+0ms    text_delta     "Looking at your request..."
T+200ms  ui_patch       APPEND  RoutingDecision  {domain: travel, status: pending}
T+450ms  ui_patch       UPDATE  RoutingDecision  {confidence: 0.87, status: complete}
T+500ms  ui_patch       APPEND  SynapseTimeline  {entries: []}
T+520ms  ui_patch       APPEND  ToolCallTrace    {tool: tripradar.search, status: running}
T+1200ms ui_patch       UPDATE  ToolCallTrace    {status: complete, duration_ms: 680}
T+1300ms ui_patch       APPEND  KeyValueList     {entries: [{...flights}]}
T+1400ms ui_patch       APPEND  Suggestion       {text: "Book the 09:15 flight"}
T+1500ms text_delta     "Best option is the 09:15 — landing at..."
```

Each patch arrives during the turn; the message bubble *grows* live. The user can interrupt at any patch (Confirmation, Suggestion) to redirect.

## Flutter side

A `DynamicMessage` model holds:
- `Map<String, _Node> nodesById`
- `_Node root` with ordered children

`_Node` is `(widgetType, props, children)`. The `UiPatchHandler` mutates `nodesById` and triggers a `setState` on the message widget. Each `widgetType` resolves to a Dart builder in a registry — same shape as `LocalWidgetLibrary` from v1, just keyed off JSON props instead of RFW DataSource.

```dart
final palette = <String, V2WidgetBuilder>{
  'RoutingDecision': (ctx, props, _) => RoutingDecisionCard(...),
  'ToolCallTrace':   (ctx, props, _) => ToolCallTraceRow(...),
  'Markdown':        (ctx, props, _) => MarkdownBody(props['body'] as String),
  'Group':           (ctx, props, kids) => Column(children: kids),
  // ...
};
```

## Tradeoffs

**Why not just stream RFW v1 deltas?** RFW's `parseLibraryFile` is designed for whole-file replacement. Partial trees would require either parsing a shadow DSL or re-emitting + re-parsing the whole text every patch — both worse than JSON patches.

**Why not Dart-side LLM tool dispatch (skip the synapse)?** Faster (no gateway round-trip), but breaks "everything is a synapse" — loses replay, Inspector, decay, and offline reconstruction. Phase 4 just shipped Genesis on the synapse premise; v2 should reinforce it.

**Why a fixed palette instead of letting the LLM declare new widget types per turn?** Two reasons: (1) prompt-token cost — every new type description bloats the system prompt, and (2) versioning — a fixed v2.0 schema can be tested, screenshot-reviewed, and migrated. The escape hatch is `Embed{kind:"rfw"}`, which preserves RFW v1's full power for one-off shapes.

**Risk: palette drift.** Once domains start asking for `FlightCard`, `HotelCard`, `RideQuote`, the palette balloons. Rule: domain-specific cards stay in `KeyValueList` + `Embed{rfw}`; only neuron-output shapes (routing, tools, suggestions) earn first-class palette slots.

## Migration from v1

- **v1 stays.** `RfwBlob` chunk type and `_RfwContent` widget remain; nothing breaks.
- **Phase A** — palette + handler shipped behind a `kV2Enabled` const, prototype tab on `/rfw-v2-demo` exercises every widget with simulated patches (this PR).
- **Phase B** — server-side `IUiSink` interface + a single `LlmNeuron` (probably Cortex) emits patches alongside its v1 text response. Flutter renders both; v2 above v1.
- **Phase C** — domain neurons (Travel, Taxi) emit `Suggestion` + `Confirmation` patches. Add user → server back-channel for accept/decline.
- **Phase D** — once v2 covers ≥80% of turns, deprecate v1. Travel/Taxi-specific cards (`FlightCard`, `HotelCard`) move to `KeyValueList` or stay as `Embed{rfw}`.

## Open questions

1. **Patch ordering.** gRPC streams are ordered per-call but UiPatch may interleave with text_delta. Patches reference parent_node_id explicitly so out-of-order parent-after-child should be handled (buffer until parent appears).
2. **Tear-down on retry.** If the user retries an assistant turn, do we replay the patch log from scratch, or the server emits `REMOVE` on every node first? Pick the former — patches are per-message-id keyed, retry = new message_id.
3. **Back-channel from widgets.** Suggestion/Confirmation need to send the user's choice back. Reuse the existing `SendMessage` gRPC with a structured payload (`{kind: "ui_response", node_id, value}`)? Or a dedicated `RespondToUi` RPC? Vote: structured `SendMessage` to keep one envelope.
4. **Decay.** Synapses decay; do UI patches inherit that? Probably yes — old assistant messages with their full patch logs become recall-able evidence the same way text turns do.
