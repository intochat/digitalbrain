# ino brain — `AskIno()` + Creator + concurrent ino-instances + redesigned `/brain`

Date: 2026-05-04
Status: design approved (verbal) — pending user review of this written spec.

## 1. Vision

ino is an OS. Public surface is one MCP method, `AskIno()`. Under the hood there
is a per-user `InoNeuron` grain — an Orleans grain that *merges Cortex + Creator
as capabilities, not siblings*. The user has many `InoNeuron` activations
running in parallel (process-class, not singleton), and a "background mind" of
autonomic neurons (monitors, reminders, decay) ticks alongside user-driven
intents. The brain visualisation at `/brain` shows ALL of this — every fire is
a particle, every Creator-spawned neuron is a sphere born live, every ino-
process trace is a coloured trail walking the brain. Travel re-uses
tripradar's existing Kafka pattern for monitoring, plumbed through ino's
synapses; the same pattern generalises to anything Creator spawns (e.g.
"watch dotnet 11 preview 3 release, ping me on Telegram").

Load-bearing companion docs:
- `docs/product-vision-final.md` — locked v0.1 decisions.
- `docs/experience-anatomy.md` — what makes an experience good, BDD seam.
- `docs/claude-project.md` — pinned Claude project README.
- `domains/travel/tripradar/CLAUDE.md` — tripradar's bot/Kafka pattern this
  spec mirrors.

## 2. Architectural decisions

### 2.1 Cortex + Creator merge into `InoNeuron`

`InoNeuron : LlmNeuron<InoEvent>, IInoNeuron` becomes the per-user grain.
What was `CortexNeuron` (`src/Ino.Kernel/CortexNeuron.cs`) and what was
`CreatorNeuron` (`iaw/src/Agents.CSharp/CreatorNeuron.cs` plus the L1
`MissedIntentTracker → NeuronOptimizer → CreatorNeuron → RoslynPlan` chain)
become **capabilities** injected into `InoNeuron`:

```csharp
public interface ICortexCapability {
    Task<RoutingMatch> RouteAsync(string prompt, ExperienceContext ctx, CancellationToken ct);
    Task<NeuronSpec> DraftSpecAsync(string prompt, ExperienceContext ctx, CancellationToken ct);
}

public interface ICreatorCapability {
    Task<NeuronCreationResult> CreateAsync(NeuronSpec spec, CancellationToken ct);
}
```

`InoNeuron.AskAsync` is the only entry point reachable from `IInoMcp.AskIno()`
(the gateway). It does:

1. Resolve the routing match via `_cortex.RouteAsync(prompt, ctx)`.
2. If `match.CanHandle == true`, dispatch via existing `IExperiencePlan` path.
3. Otherwise, draft a `NeuronSpec` via `_cortex.DraftSpecAsync(prompt)` and
   call `_creator.CreateAsync(spec)` — gated by approval risk (see §2.3).
4. Either way, the response goes back through `Caller` to the gateway.

**Many ino-instances per user** means many `InoNeuron` grain activations,
keyed by `(userId, sessionId)` not just `userId`. Sessions are explicit on
`AskIno()` so callers can address a specific running instance or open a new
one. The "background mind" instances use a synthetic `sessionId = "autonomic"`.

### 2.2 `IInoMcp.AskIno()` — single public method

```csharp
public sealed record AskInoRequest(
    string Prompt,
    string UserId,
    string SessionId = "default",
    Dictionary<string, string>? Metadata = null);

public sealed record InoResponse(
    string Text,
    string CorrelationId,
    RfwPayload? Rfw = null,
    InoCreatedNeuron? CreatedNeuron = null);

public interface IInoMcp {
    Task<InoResponse> AskInoAsync(AskInoRequest request, CancellationToken ct);
}
```

Implementation lives in `Ino.Gateway.Grpc` (or a new `Ino.Mcp` host adjacent
to `iaw/src/MCP`) and resolves the per-user `InoNeuron` grain via
`grainFactory.GetGrain<IInoNeuron>(GrainId.Create("ino", $"{userId}/{sessionId}"))`.

The existing `Chat()` gRPC RPC stays for the Flutter client, but its handler
internally now also routes through `InoNeuron.AskAsync` — no parallel
codepath.

### 2.3 Creator: what `CreateAsync` actually does

Creator is *not* a separate grain — it's `ICreatorCapability` injected into
`InoNeuron`. The implementation lives in `Ino.Core.Genesis` (new namespace)
backed by:

- `IRoslynCompiler` — already exists in `iaw/src/Agents.CSharp/Roslyn`,
  reuse it.
- `IDiscovery.RegisterDynamicExperienceAsync` — already exists at
  `src/Ino.Kernel/Discovery.cs:101`.
- `IProposalLog` — already exists from Phase 4 Inspector E.3.
- `IFirePort` — already exists in `Ino.Core.Hosting`.
- `IGrainReminderRegistry` — Orleans Reminders client.

`CreateAsync` flow:

1. **Risk gate.** `IRiskGate.RequiresApprovalAsync(spec)` — high-risk specs
   (anything that costs money, sends external messages, or runs unbounded
   loops) get a `Proposal` written to `ProposalLog` with status `Pending` and
   the call returns `NeuronCreationResult.PendingApproval`. Low-risk specs
   auto-approve.
2. **Compile.** `IRoslynCompiler.CompileAsync(spec.Body)` produces an
   in-memory `Assembly` with the grain class.
3. **Register.** Discovery gets the new experience via
   `Discovery.RegisterDynamicExperienceAsync`. The next call to
   `_cortex.RouteAsync` (i.e. the next `AskIno()` invocation) will see it
   — there is no silo restart and no Cortex-side cache invalidation
   needed because Cortex always pulls live from Discovery.
4. **Wire.** Initial synapses fire via `IFirePort.FireAsync(NeuronInitialised(neuronId, seedPayload))` — the new neuron's canonical handler picks up
   the seed.
5. **Schedule.** If `spec.Schedule is { Period: var p }`, register a
   durable Orleans Reminder named `"tick"` on the new grain via
   `IGrainReminderRegistry.RegisterOrUpdateReminder`.

`NeuronSpec` is a `[GenerateSerializer]` record with: `Id`, `DisplayName`,
`Description`, `CanonicalSynapseType`, `Body` (Roslyn source), `Schedule`,
`InitialSynapses` (seed payloads), `RiskHints`.

### 2.4 Orleans hygiene — `IIncomingGrainCallFilter`s + `RequestContext` + `IRemindable`

Two new filters in `Ino.Core.Hosting/Filters/`:

- `InoInstanceContextFilter` — reads `RequestContext.Get("ino.userId")` and
  `"ino.sessionId"`, validates them against the activation's grain key, and
  short-circuits with `InoInstanceMismatch` if a cross-user call leaks. This
  is a security boundary, not just a sanity check.
- `BrainTraceFilter` — wraps every grain call, emits a `BrainPulse` event
  (`{ traceparent, fromGrain, toGrain, methodName, durationMs, status }`)
  onto the `InoBrainStream` Orleans Stream. The Flutter `/brain` screen
  subscribes to this stream over a new gRPC server-stream RPC
  `WatchBrainActivity` (see §4.4).

`RequestContext` propagates `traceparent` automatically across cross-silo
hops; we add `ino.userId` and `ino.sessionId` keys for the filter.

`IRemindable` is implemented by autonomic neurons (FlightMonitor, Creator-
spawned monitors) so their schedules survive silo restarts. This is the same
pattern as `iaw/src/Core/Agents/Agent.Scheduling.cs` (already migrated to
Orleans 10.1 DurableJobs). For Creator-spawned neurons, the Reminder is
registered in step 5 of `CreateAsync`.

Verified via Context7 (`/dotnet/orleans` query 2026-05-04): the
`IIncomingGrainCallFilter.Invoke` pattern with `IIncomingGrainCallContext`
is current; `RequestContext.Set/Get/Clear` is the supported propagation
mechanism; `RegisterOrUpdateReminder` + `IRemindable.ReceiveReminder` is
the Reminder API.

### 2.5 Travel ← tripradar bridge

tripradar today (per `domains/travel/tripradar/CLAUDE.md` §Architecture):
- TripRadar.Server publishes Kafka topic on price changes.
- TripRadar.Bot's `FlightPriceConsumer : BackgroundService`
  (`domains/travel/tripradar/src/TripRadar.Bot/Notifications/`) consumes Kafka
  and sends Telegram via `TelegramBotService`.

ino mirror:
- `FlightMonitorNeuron` (already in topology, currently a stub) becomes a
  pure-code `Neuron<FlightMonitorEvent>` that hosts an internal Kafka
  consumer (`Confluent.Kafka` 2.x — verify via Context7 before writing) and
  fires `FlightPriceChanged` on each event.
- `TelegramChannelNeuron` (new, in `Ino.Domains.Travel` for v0.1 — moves
  to a top-level `Ino.Channels.Telegram` once a second domain needs it) is
  the canonical handler for `NotifyTelegram` synapse and wraps
  `Telegram.BotAPI`'s `ITelegramBotClient`. The bot token comes from the
  same Aspire parameter the existing `clients/Telegram/Ino.Telegram.Host`
  uses (`telegram-bot-token`).
- `PlanTripPlan` gets a new tool method, `watch_flight_price`, that fires
  `WatchFlightPrice` → `FlightMonitor` persists an entry in its journal.
  Later when `FlightPriceChanged` fires, a small reactor neuron
  (`FlightAlertNeuron`) joins it with the user's notification preferences
  and fires `NotifyTelegram` with the formatted message.
- tripradar stays its own Aspire service; ino subscribes to its Kafka topic
  via `Aspire.Hosting`'s Kafka resource ref. No code merge between tripradar
  and ino's Travel — only protocol (Kafka topic, JWT exchange).

This same `Kafka-fanout → fire synapse → reactor → NotifyTelegram` pattern
is what `Creator` will produce when asked to spawn a generic monitor (e.g.
"watch dotnet 11 preview 3 release"). Creator's `NeuronSpec` for that case
declares: external trigger source (HTTP poll or webhook), the synapse it
fires, and the channel synapse it ultimately wires to (Telegram by default,
configurable).

### 2.6 Brain UI redesign at `/brain`

Current `/brain` (Slice B.1, just shipped):
- three_js scene with static topology, OrbitControls, auto-rotate.
- Chat composer + Run Travel demo button at bottom.
- Pulse on the active domain when an intent is in flight.

New surface (this spec):
- **Chat collapsed to icon (left)**, **voice icon (right)**, in a single
  bottom bar that's mostly transparent. The composer expands inline when
  the chat icon is tapped (animated width transition; reuses
  `AnimatedContainer`). Voice icon stays — taps `InoBloc.StartRecording`.
- **Top-right toggle panel**: two checkboxes — `[ ] group by domain` and
  `[ ] highlight experience`. Default both off (raw three_js view, neurons
  shown by node-type colour only). When `group by domain` is on, the
  current domain-anchor layout applies (today's default — gets demoted to
  opt-in). When `highlight experience` is on, the gold halos brighten and
  the experience-membership edges become opaque; non-experience neurons
  dim to 30%.
- **Click-to-inspect**: three_js raycaster on tap → side drawer slides in
  from the right with neuron / synapse / experience details, recent fires,
  link to Aspire trace. Reuses the `inspector_drawer.dart` shape.
- **Timeline scrubber at bottom** (above the composer): a 60-second
  rolling window of `BrainPulse` events as colour bands per domain.
  Drag-to-replay rewinds the brain to that timestamp — particles re-play,
  neurons re-pulse. Pause button freezes the brain on a moment.
- **Multi-trace overlay**: each `traceparent` gets a distinct hue. Multiple
  concurrent ino-instances show as multiple coloured trails at once. The
  legend explains the trace colour scheme.

UI files to add:
- `clients/ino.flutter/lib/screens/brain/brain_composer.dart` — collapsed
  composer + voice (split out from `brain_screen.dart`).
- `clients/ino.flutter/lib/screens/brain/brain_inspector_drawer.dart`
  — side drawer for click-to-inspect.
- `clients/ino.flutter/lib/screens/brain/brain_timeline.dart` — scrubber.
- `clients/ino.flutter/lib/screens/brain/brain_layout.dart` — extract
  layout strategies (`groupByDomain` vs flat) into a strategy class.
- `clients/ino.flutter/lib/state/brain_bloc.dart` — Bloc for brain UI
  state (active domain, mode toggles, pulses, scrubber position).

Existing files modified:
- `clients/ino.flutter/lib/screens/brain/brain_screen.dart` — reduced to
  the canvas + overlay assembly.
- `clients/ino.flutter/lib/screens/brain/brain_topology.dart` — adds
  `experienceMembership: Map<String, List<String>>` so the highlight
  toggle has data.
- `clients/ino.flutter/lib/app.dart` — the `/brain` route remains; no
  routing changes.

## 3. Components and file layout

### 3.1 New on the silo side
| Path | Purpose |
|---|---|
| `src/Ino.Core/InoNeuron.cs` | grain class merging Cortex + Creator |
| `src/Ino.Core/InoEvent.cs` | journal event for `InoNeuron` |
| `src/Ino.Core/Capabilities/ICortexCapability.cs` | routing capability |
| `src/Ino.Core/Capabilities/ICreatorCapability.cs` | spawn capability |
| `src/Ino.Core/Genesis/CreatorCapability.cs` | implements ICreatorCapability |
| `src/Ino.Core/Genesis/NeuronSpec.cs` | typed spec for new neurons |
| `src/Ino.Core/Genesis/IRiskGate.cs` + impl | high-risk → Proposal gate |
| `src/Ino.Core.Hosting/Filters/InoInstanceContextFilter.cs` | RequestContext binding |
| `src/Ino.Core.Hosting/Filters/BrainTraceFilter.cs` | per-call BrainPulse emit |
| `src/Ino.Core.Hosting/Streams/InoBrainStream.cs` | Orleans Stream config |
| `src/Ino.Mcp/InoMcpService.cs` | gRPC server for `AskIno()` |
| `domains/travel/Ino.Domains.Travel/Neurons/FlightMonitorNeuron.cs` | Kafka subscriber → fires `FlightPriceChanged` |
| `domains/travel/Ino.Domains.Travel/Neurons/FlightAlertNeuron.cs` | reactor — joins price change with user prefs, fires `NotifyTelegram` |
| `domains/travel/Ino.Domains.Travel/Neurons/TelegramChannelNeuron.cs` | canonical handler for `NotifyTelegram` |
| `domains/travel/Ino.Domains.Travel/Synapses/FlightPriceChanged.cs` | typed synapse |
| `domains/travel/Ino.Domains.Travel/Synapses/NotifyTelegram.cs` | typed synapse |
| `domains/travel/Ino.Domains.Travel/Synapses/WatchFlightPrice.cs` | typed synapse |

### 3.2 Modified on the silo side
| Path | Change |
|---|---|
| `src/Ino.Kernel/CortexNeuron.cs` | becomes thin wrapper that delegates to `ICortexCapability`, OR is deleted and replaced once `InoNeuron` is the entry. Decision: **delete** once `InoNeuron` is in place; tests migrate. |
| `iaw/src/Agents.CSharp/CreatorNeuron.cs` | becomes thin wrapper that delegates to `ICreatorCapability` initially; later removed. |
| `src/Ino.Aspire.Hosting/AddIno.cs` | wires the two filters + the Stream + the MCP project. |
| `src/Ino.AppHost/Program.cs` | adds `Ino.Mcp` resource + Kafka resource + Telegram parameter for the bridge. |
| `src/Ino.Gateway.Grpc/InoChatService.cs` | `Chat()` handler routes through `IInoNeuron.AskAsync` instead of directly into Cortex. |

### 3.3 New on the Flutter side
See §2.6 file list.

### 3.4 New gRPC contracts
| Method | Direction | Shape |
|---|---|---|
| `AskIno` | unary | `AskInoRequest → InoResponse` (replaces direct `Chat` for MCP callers; `Chat` stays for Flutter compatibility) |
| `WatchBrainActivity` | server-stream | `BrainWatchRequest → stream BrainPulse` |
| `InspectGrain` | unary | `InspectRequest{ grainId } → InspectResponse{ kind, label, domain, recentFires[10], experiences[], traceparents[] }` — new RPC, used by the click-to-inspect side drawer (§2.6). The drawer reuses `inspector_drawer.dart`'s visual shell but gets its data from this RPC, distinct from the existing Proposals / Routing tabs. |

## 4. Data flow walkthroughs

### 4.1 Existing experience hits, no Creator
`AskIno("plan a trip to Bali")` → gateway → `InoNeuron.AskAsync` →
`_cortex.RouteAsync` returns `RoutingMatch{ ExperienceId="travel.plan-trip" }` →
`InoNeuron` resolves `IPlanTripPlan(userId)` → existing 6-hop flow. Creator
never invoked. BrainTraceFilter emits pulses for every grain hop; brain
UI shows the trail.

### 4.2 No experience matches → Creator spawns
`AskIno("watch dotnet 11 preview 3 release, ping me on Telegram")` →
`_cortex.RouteAsync` returns `RoutingMatch{ CanHandle=false }` →
`_cortex.DraftSpecAsync` produces `NeuronSpec{ Id="genesis.github-release-monitor.dotnet-11-p3", Schedule=ofMinutes(15), Body=<Roslyn source>, InitialSynapses=[NotifyTelegram seed] }` →
`_creator.CreateAsync` runs the 5-step flow → returns
`NeuronCreationResult.Activated(grainId)` → first Reminder tick fires →
neuron polls GitHub, finds no release yet → next tick → eventually finds
release → fires `ReleaseDetected` → reactor fires `NotifyTelegram` →
`TelegramChannelNeuron` sends message. Brain UI shows the new neuron
born live; user receives Telegram notification.

### 4.3 High-risk Creator path
Same as 4.2 but `IRiskGate.RequiresApprovalAsync` returns `true` because
the spec spends money or contacts an external API not on the safelist.
`CreateAsync` writes a `Proposal` and returns `PendingApproval`. The
brain UI shows a ghost-grey neuron silhouette in the Genesis lobe.
Inspector → Proposals tab shows `Pending`. User approves → the proposal
handler calls back into `CreatorCapability.MaterialiseAsync(proposalId)`
which runs steps 2-5 → ghost solidifies in the brain.

### 4.4 Brain live trace
Flutter opens server-stream `WatchBrainActivity` on `/brain` mount.
`BrainTraceFilter` emits `BrainPulse{ traceparent, inoInstanceId,
fromGrain, toGrain, methodName, durationMs, status }` on every
silo-side grain call. `inoInstanceId` is sourced from
`RequestContext.Get("ino.sessionId")` (the per-user session id from
§2.1).

Hue mapping in Flutter is per-`inoInstanceId`, not per-`traceparent`:
```
hueIndex = stableHash(inoInstanceId) mod 12
trailColor = palette[hueIndex]
```
This means *one ino-instance shows in one consistent colour across all
its intents*. Multiple concurrent ino-instances for the same user show
as distinct trails. The autonomic mind (`sessionId="autonomic"`) gets
a reserved palette slot (slot 0) so it's always recognisable.

`traceparent` is still carried on every pulse for trace-detail lookup
(click a particle → Aspire trace), but it does not drive the hue.

Timeline replay re-emits the same pulses from a buffer at the
scrubbed timestamp; the buffer keys on `inoInstanceId` so replay
preserves per-instance colours.

## 5. Phase plan — sub-projects, ordered

These slices are independently deliverable but build on each other.
Each ships its own commit, its own verification (build → test →
`aspire run` → browser check → Aspire traces).

### Slice C.1 — `InoNeuron` + `AskIno()` MCP entry (architectural backbone)
Refactor only. No new behaviour. Cortex stays functional, but the
entry point goes through `InoNeuron`. Risk gate stubbed (always low-risk).
BDD: existing `domains/travel/Ino.Domains.Travel/Features/*.feature`
must still pass. New: `Features/ino-ask.feature` covering the routing
boundary.

### Slice C.2 — Creator capability behind a flag
`CreatorCapability` lands wired but disabled by default behind
`INO_CREATOR_ENABLED=false`. Flip the flag in tests to exercise the
flow. Adds `Features/genesis-create.feature` with scenarios for the
"watch github release" and "monitor my X" archetypes.

### Slice C.3 — Orleans hygiene filters + `BrainPulse` stream
`InoInstanceContextFilter`, `BrainTraceFilter`, `InoBrainStream`. Adds
`WatchBrainActivity` server-stream RPC. Flutter subscribes but only
logs events — no UI yet (proves the wire).

### Slice C.4 — Brain UI redesign (the visual layer)
Collapsed composer + voice + toggles + click-to-inspect side drawer +
multi-trace particle overlay (hooks into Slice C.3's stream). Existing
domain-pulse logic from B.1 stays as fallback when the stream is empty.

### Slice C.5 — Timeline scrubber
Rolling 60s buffer of `BrainPulse` events, drag-to-replay re-runs the
particle animation from the buffer at the scrubbed timestamp. Pause
button freezes. No backend changes — purely Flutter.

### Slice C.6 — Travel ← tripradar Kafka bridge
`FlightMonitorNeuron` subscribes to tripradar's Kafka topic.
`TelegramChannelNeuron` lands. `PlanTripPlan.watch_flight_price` tool
becomes real. Adds `Features/flight-price-watch.feature`. tripradar
itself is unchanged — only the topic contract is consumed.

### Slice C.7 — Creator generalisation: GitHub release monitor demo
End-to-end demo of `AskIno("watch dotnet 11 preview 3 release, ping
me on Telegram")` → Creator spawns → 15-minute Reminder ticks →
fires Telegram. Demo asset for the Phase 5 video.

Slices C.1 → C.7 is roughly 7-10 dev days. Each is ~1 day plus
verification.

## 6. Out of scope

Scoped out for this design (deferred to follow-up specs):
- Multi-tenant brain (one brain per user; sharing is a future epic).
- Creator generating non-monitoring neurons (e.g. UI-rendering
  neurons, autonomous decision-makers). v0.1 Creator targets the
  monitor + reactor + channel pattern only.
- Cross-cluster ino-instance migration (live migration of a running
  ino across silos).
- Cortex deletion of `CortexNeuron.cs` (kept as a thin shim through
  C.1 to keep the diff reviewable; final deletion in a follow-up).
- ML-driven Risk Gate (v0.1 uses a hand-rolled rules engine over
  spec capabilities; ML scoring is a follow-up).

## 7. Verification per slice

Every slice must pass the doctrine in `CLAUDE.md` §"Verification loop":

1. `dotnet build ino.slnx` green.
2. `dotnet test ino.slnx` green.
3. `aspire run` (foreground) — every resource Healthy in dashboard.
4. Browser-render the relevant scenario; check Aspire **Structured
   Logs** + **Traces** for end-to-end `traceparent` propagation.
5. UI slices: capture screenshots into `reviews/slice-c-N-*.png`.

Build+test alone is NOT done.

## 8. Open questions deferred to plan-writing

- Roslyn-compiled grain registration with Orleans' source-generated
  `GrainType.Name` — `[Alias("…")]` per generated grain to keep
  Cortex resolution stable. Verify pattern in writing-plans phase.
- `IInoMcp` host placement — adjacent to `Ino.Gateway.Grpc` or new
  `Ino.Mcp` project? Decided in C.1 plan.
- `InoBrainStream` provider — memory streams in dev (already
  configured by `AddIAW`), Orleans Streams over Azure Storage Queues
  in prod. Decided in C.3 plan.
- Telegram bot share between `clients/Telegram/Ino.Telegram.Host` and
  `TelegramChannelNeuron` — single token, two consumers; token-routing
  layer or duplicate channel? Decided in C.6 plan.
