# ino UI v3 — Generative Rive + RFW shell (design)

**Date:** 2026-05-04
**Status:** Draft — 3 of 6 open decisions locked; §10 still has #4–#6
**Branch base:** master @ 9de7d43
**Companion to:** [`product-vision-final.md`](../../product-vision-final.md) decision 4 (Persona) + decision 5 (RFW) — this spec is the v3 evolution of both.

## Decision log

| # | Decision | Status | Locked on |
|---|---|---|---|
| 1 | **UIComposer placement** — single `UIComposer` in the kernel silo (peer to Cortex). Per-active-domain palette injection happens at prompt-build time. | ✅ Locked | 2026-05-04 |
| 2 | **Design-system files** — **per-domain `.riv`** (`travel-design.riv`, `taxi-design.riv`, …). Kernel ships a baseline `ino-design.riv` with the chrome/persona components every domain reuses. Domains can extend or override by artboard name. | ✅ Locked | 2026-05-04 |
| 3 | **Streaming Compose** — pattern α: skeleton-then-data. First chunk = complete `.rfwtxt` skeleton; subsequent chunks = `DynamicContent` deltas streamed into the same `RemoteWidget` mount. No token-stream parse, no chunked sub-docs. | ✅ Locked | 2026-05-04 |
| 4 | Designer commitment for per-domain `.riv` authoring | ⏳ Open — see §10.4 |
| 5 | `RfwValidator` strategy (Dart helper at build vs C# grammar) | ⏳ Open — see §10.5 |
| 6 | Persona widget unification (top-of-screen vs inline) | ⏳ Open — see §10.6 |

---

## 0. Lineage

| Iteration | What ships | Author |
|---|---|---|
| **v1 (current v0.1)** | Marketplace `emoji.riv` rendered raw, no VM binding; CustomPaint fallback; hand-coded Flutter widgets registered in per-domain RFW libraries (`ino.flights`, `ino.hotels`, …). Cards are static — LLM picks a *type* and supplies *fields*. | `clients/ino.flutter/lib/ui/ino_runtime.dart`, `lib/persona/persona_widget.dart` |
| **v2 (planned, not shipped)** | Authored `ino-persona.riv` with mood/energy/pulse VM properties driven from BLoC; PersonaEvolver L1 generating mapping scripts; 19 Travel neurons composing same RFW card types. | `docs/superpowers/plans/2026-04-16-ino-persona-rive-living-experiences-plan.md` |
| **v3 (this spec)** | LLM emits **whole `.rfwtxt` per turn** that composes a small registered set of **responsive Rive components** wired to live VM properties. Rive owns motion + responsiveness; RFW owns structure + branching; LLM owns composition. | this doc |

The v0.1 contract — "experience cards as RFW, chrome as native Flutter" — is preserved. v3 widens what RFW can compose by registering Rive components as RFW widgets, and changes who writes the `.rfwtxt` from "neuron `RfwTemplateSource` Roslyn script" to "LLM, per turn, against a typed schema".

---

## 1. The bet

A small library of **well-authored, responsive Rive components** registered as RFW widgets is enough surface for an LLM to compose visually-rich, motion-rich UI per request, without any new Flutter rebuild and without the LLM authoring binary `.riv` files.

Three things make this newly possible in May 2026:

1. **Rive Responsive Layouts** (video `APoSyYlFD8g`, *Responsive Layouts have landed*). One artboard reflows to any container; State Machine breakpoints fire on width/height/aspect-ratio. We don't need N variants of each component.
2. **Rive Scripting / Luau** (video `M6kGkR-7JTE`, *Scripting is Live in Rive*). Designers can encode behaviour (Converters, custom Layouts, Path effects, Nodes) inside `.riv` that previously had to be Dart glue. The Flutter↔Rive seam shrinks.
3. **Rive Flutter Data Binding** (already at `rive: ^0.14.5`). `RiveWidgetController.dataBind(DataBind.auto())` returns a `ViewModelInstance` with typed reactive properties (`number`, `string`, `color`, `trigger`, `image`, `enumerator`, `artboard`, nested `viewModel`). This is the runtime API the LLM's parameters land in.

Path C from the brainstorm — LLM emits Luau that ships in a fresh `.riv` — is **out of scope**. Rive AI Coding Agent is editor-only with no programmatic API; `.riv` is binary; Luau is bundled at export. Defer to a "scripted design-system component" pipeline (post-v0.1, see §11).

---

## 2. North-star scenario

User opens the trip itinerary view and asks:

> *"Reshape this into a story I can scroll — give the Tokyo days hero treatment, fold the small things."*

What happens:

1. `InoNeuron` calls `IUIComposer.ComposeAsync(payload, intent)` (new gateway endpoint) with the trip data + intent + an `IUIPalette` describing the registered Rive component vocabulary.
2. `UIComposer` (LLM-backed, BDD-mocked in dev) emits an `.rfwtxt` document in the `ino.composed` library namespace, plus a `DynamicContent` JSON map.
3. The Flutter shell receives `(rfwBytes, data)` and mounts a single `RemoteWidget` over the existing chat thread slot.
4. The Rive components inside — `RiveHero`, `RiveDayCard`, `RiveBadge`, `RivePersonaInline` — bind to VM properties from `data.*` and reflow themselves to the column/row RFW gives them.
5. Persona orb (top-of-screen) is still the v2 PersonaWidget; v3 only extends *what's inside the chat slot*.

If Playwright can drive that, intercept the `Compose` gRPC frame, assert the `.rfwtxt` parses, and screenshot a frame mid-pulse — v3 is shipping.

---

## 3. Architecture — four layers

```
┌────────────────────────────────────────────────────────────────────┐
│  L4. UIComposer (Cortex peer, LLM-backed, BDD-mocked)              │
│      input:  (intent, payload, IUIPalette)                         │
│      output: (rfwBytes, DynamicContent JSON)                       │
└────────────────┬───────────────────────────────────────────────────┘
                 │ gRPC: rpc Compose(ComposeRequest) → ComposeResponse
                 ▼
┌────────────────────────────────────────────────────────────────────┐
│  L3. Generative shell (Flutter)                                    │
│      RemoteWidget(runtime, data, widget=ino.composed:root)         │
│      DynamicContent rebuilt per turn                               │
└────────────────┬───────────────────────────────────────────────────┘
                 │
                 ▼
┌────────────────────────────────────────────────────────────────────┐
│  L2. RFW widget libraries                                          │
│      core.widgets, material  (existing, unchanged)                 │
│      ino.chat, ino.flights, … (existing v1 cards, kept)            │
│      ino.rive  (NEW — Rive components as RFW widgets)              │
└────────────────┬───────────────────────────────────────────────────┘
                 │
                 ▼
┌────────────────────────────────────────────────────────────────────┐
│  L1. Rive design systems (per-domain + kernel baseline)            │
│      ino-design.riv          (kernel — chrome + persona components)│
│      travel-design.riv       (Travel domain components)            │
│      taxi-design.riv         (Taxi domain components)              │
│      …                       (one per installed domain)            │
│      Authored in Rive Editor; binary; each domain ships its own    │
│      asset; kernel baseline acts as fallback.                      │
└────────────────────────────────────────────────────────────────────┘
```

### 3.1 Why the LLM never authors `.riv`

`.riv` is binary and Claude can't edit it. The Rive AI Agent that *can* generate Luau is editor-only and exposes no API. So the design-system component vocabulary is a **fixed contract** at any given app build; the LLM composes against it. New components require a designer round-trip + `flutter build web` (acceptable: same cadence as adding new RFW widget types today).

### 3.2 Why this is bigger than v1's RFW

v1's RFW lets the LLM pick a card *type* and supply fields. v3's RFW lets the LLM **compose arbitrary structure** — `Column`s of `RiveHero` over `Row`s of `RiveDayCard` over conditional `RiveBadge`s, `...for` loops over trip legs, `switch` on persona mood — using the rfw DSL's full expressive surface (imports, widget defs with local state, `set state.x = y`, event triggers, list interpolation). The Rive components are atoms; the composition is per-turn and unique.

---

## 4. The Rive design systems (per-domain + kernel baseline)

Each domain ships **one `.riv`**: `clients/ino.flutter/assets/rive/<domain>-design.riv`. The kernel ships `ino-design.riv` as the always-present baseline. The composer picks an artboard by `(domain, name)` with kernel as fallback when the active domain doesn't define that artboard. This aligns with the marketplace install model: install a domain → its `.riv` lands in `assets/rive/`, its schema lands in the palette, the LLM can immediately compose against it.

### 4.1 Kernel baseline (`ino-design.riv`)

Components every domain reuses. Authored once. Stable contract.

| Artboard | Purpose | VM properties (typed) |
|---|---|---|
| `Hero` | Full-bleed scene; title + mood imagery | `string title`, `string subtitle`, `image background`, `enum mood {dawn, day, dusk, night}`, `color accent` |
| `Tile` | Generic content tile (icon + 3 lines, tappable) | `enum kind {flight,hotel,place,activity,generic}`, `string line1`, `string line2`, `string line3`, `color accent`, `trigger tap` |
| `Badge` | Confidence / status pip | `string label`, `number value0to1`, `color tone` |
| `PersonaInline` | Small inline orb mirroring the global persona | `enum mood`, `number energy`, `trigger pulse` |
| `Spacer` | Motion-aware decorative gap | `number height`, `enum motif {dots,wave,plain}` |

### 4.2 Per-domain extensions (initial set)

`travel-design.riv` (authored by the Travel domain author):

| Artboard | Purpose | VM properties |
|---|---|---|
| `DayCard` | One itinerary day; collapsible on small breakpoints | `string label`, `number index`, `string summary`, `viewModel persona`, `trigger expand` |
| `FlightStrip` | Origin → destination with airline glyph and timing | `string origin`, `string dest`, `string airline`, `string depart`, `string duration`, `number layovers` |
| `MapPin` | Place pin with motion-aware label | `number lat`, `number lon`, `string label`, `enum tone {primary,muted,warning}` |

`taxi-design.riv` (authored by the Taxi domain author):

| Artboard | Purpose | VM properties |
|---|---|---|
| `RidePill` | Live ride state | `enum state {requested,assigned,enroute,arrived,onTrip}`, `string driver`, `number etaMinutes` |
| `FareBreakdown` | Animated fare reveal | `number base`, `number surge`, `number total`, `string currency` |

Domains can also **override** a kernel artboard by name (e.g. Travel ships its own `Hero` with parallax sky; lookup falls through to kernel only when not defined locally).

### 4.3 Authoring discipline (every `.riv`)

- `Fit.layout` on the root artboard; Rive Layouts internally (Rows/Columns).
- State Machine breakpoints at `<320px width`, `<600px width`, `>1200px width` switching between compact / regular / expanded variants.
- Luau **Converters** for value mapping (seconds→hh:mm, currency formatting, distance units) — keeps logic out of Dart.
- Luau **Layout** scripts for non-trivial arrangements (mosaic packing, timeline density) — opt-in per artboard.
- Every exported VM property is documented in a generated `<domain>-design.schema.json` sibling. The schema is what `IUIPalette` ships to the composer prompt.
- Naming convention: PascalCase artboards, camelCase properties, no spaces. Enforced by the schema generator.
- Cross-domain palette merge rule: the `IUIPalette` exposed to a single Compose call is `kernel ⊕ active-domain ⊕ also-installed-domains-the-intent-touches`, with later wins (so Travel's `Hero` shadows kernel's `Hero` when Travel is active).

---

## 5. The RFW widget library (`ino.rive`)

New file: `clients/ino.flutter/lib/ui/rive_widgets.dart`. Registered alongside existing libraries in `ino_runtime.dart`:

```dart
runtime.update(const LibraryName(<String>['ino', 'rive']), createRiveWidgets());
```

### 5.1 Wrapper pattern

Each artboard is exposed as one RFW widget under `ino.rive`, taking a `domain` source field that selects which `.riv` to resolve against. The wrapper:

1. Loads each `<domain>-design.riv` lazily on first reference; caches `rive.File` per (domain, file). Kernel baseline `ino-design.riv` is preloaded at app start.
2. Resolves artboard by `(domain, name)` — falls back to kernel baseline when the per-domain file doesn't define `name`.
3. Constructs a `RiveWidgetController` per mount.
4. Calls `controller.dataBind(DataBind.byName(<vmName>))` and pulls typed property handles.
5. Subscribes to `data.<path>` from the RFW DataSource and writes to VM properties on change. **Reactive on every `DynamicContent.update(...)`** — this is what makes streaming pattern α work without remounting.
6. Wires VM `trigger` listeners back to RFW events (`source.handler(...)`).
7. Disposes everything on unmount.

Sketch (`Tile`):

```dart
'Tile': (BuildContext context, DataSource source) {
  return _RiveArtboard(
    domain: source.v<String>(<Object>['domain']) ?? 'kernel',
    artboard: 'Tile',
    bindings: <String, Object?>{
      'kind':   source.v<String>(<Object>['kind']) ?? 'generic',
      'line1':  source.v<String>(<Object>['line1']) ?? '',
      'line2':  source.v<String>(<Object>['line2']) ?? '',
      'line3':  source.v<String>(<Object>['line3']) ?? '',
      'accent': source.v<int>(<Object>['accent']),
    },
    triggers: <String, HandlerTrigger?>{
      'tap': source.voidHandler(<Object>['onTap']),
    },
  );
},
```

`_RiveArtboard` is one widget — the per-artboard wrappers are thin shape-checks around it. The State class owns the Rive controller + VM bindings + reactive subscriptions. **No closures or imperative logic ever live in `.rfwtxt`** — RFW's design rules forbid it. All behaviour is either VM-property reactivity or pre-declared events.

### 5.2 Performance budget

- **One `rive.File` per app process.** All wrappers share it. Disposing per-widget disposes only the `RiveWidgetController` and `ViewModelInstance`.
- **Image swaps** (`ViewModelInstanceAssetImage`) decode through `Factory.rive` and may be expensive; for v3, default `image` properties are pre-baked and runtime swaps are opt-in per artboard.
- Aspire OTel metric `ino.ui.rive_mounts` per turn; budget warning at >24 mounts/turn (one screen-full).

---

## 6. The LLM composition contract

### 6.1 `IUIPalette` — what the LLM sees

```csharp
public sealed record UIPalette(
    IReadOnlyList<RiveComponentSchema> RiveComponents,  // merged: kernel ⊕ domains
    IReadOnlyDictionary<string, string> ComponentOriginByName, // "Hero" → "travel"
    IReadOnlyList<RfwLibrarySchema> ExtraLibraries,
    IReadOnlyList<DataBindingExample> Examples);
```

`RiveComponentSchema` is generated from per-domain `<domain>-design.schema.json` files (see §7). Each entry includes the artboard name, originating domain, the VM property catalogue with type + nullability + valid enum values, and a one-line designer-authored description. The palette is roughly 800–2500 tokens (grows linearly with installed-domain count) — small enough to ship in every Compose prompt without context bloat. The composer prompt makes the `domain` field on each Rive widget mandatory so cross-domain composition is unambiguous.

### 6.2 Prompt skeleton (BDD-mockable)

```
You compose UI for the ino assistant.

PALETTE (Rive components — bind via data.<path>):
- Hero(title: string, subtitle: string?, background: image-ref, mood: dawn|day|dusk|night, accent: hex)
- DayCard(label, index: number, summary, persona: PersonaInlineRef, expand: trigger)
- Tile(kind: flight|hotel|place|activity, line1, line2, line3, accent, tap: event)
- Badge(label, value0to1: number, tone)
- PersonaInline(mood, energy: number, pulse: trigger)

DSL: rfw 1.x. You may use core.widgets and material. You MUST emit:
  import core.widgets; import material; import ino.rive;
  widget root = …;

CONSTRAINTS: no closures; events only via the listed triggers; data refs must
exist in DATA below; one Hero per screen max; Tiles wrap at small breakpoints.

INTENT: <intent string>
DATA (will be bound as DynamicContent): <flat JSON>
```

### 6.3 Output shape — streaming pattern α (skeleton-then-data)

The Compose RPC is a **server-streaming** gRPC method. Two frame types over a single call:

```protobuf
message ComposeFrame {
  oneof body {
    ComposeSkeleton skeleton = 1;  // first frame, exactly one
    DataDelta       data     = 2;  // zero-or-more, ordered
  }
}

message ComposeSkeleton {
  bytes  rfw_bytes        = 1;   // complete .rfwtxt, ready to parse
  string initial_data_json = 2;  // initial DynamicContent state (may be sparse)
  repeated EventSpec events = 3;
}

message DataDelta {
  // JSON merge-patch (RFC 7396) applied to DynamicContent root.
  string patch_json = 1;
}
```

Sequence per turn:

1. Server: skeleton emitted ASAP — composer LLM is prompted to output structure first, payload second; structure is small, fast.
2. Client: `RemoteWidget` mounts immediately on skeleton. Cells with no data show their **Rive idle/loading variants** (designer-authored — `state == empty` is part of every artboard's State Machine).
3. Server: as capability synapses (flight-search, hotel-search, …) return, their results are merge-patched into `DynamicContent` and streamed as `DataDelta` frames. Each delta lands in the same mounted tree — no re-parse, no re-mount.
4. Closing the gRPC stream signals "complete".

The skeleton itself is server-side rfw-parsed via `RfwValidator` (§7.3) before the first frame is emitted, so an LLM regression cannot ship broken bytes.

#### 6.3.1 Anim schema v1 — opt-in tween metadata for numeric fields

Numeric VM fields (Badge `value0to1`, PersonaInline `energy`, Spacer `height`, …) opt into a curve-driven tween via two paired sibling fields:

```
<field>AnimDurMs   : int  (>0 enables tween; null/0/negative ⇒ snap)
<field>AnimCurve   : enum (linear | easeIn | easeOut | easeInOut | easeOutCubic; unknown ⇒ easeOut)
```

The composer writes the anim leaves before — or alongside — the value mutation. The LocalWidgetBuilder packages them into an `AnimSpec` passed to `ViewModelHandle.writeNumber(name, value, anim:)`. Bare value writes (no anim siblings present) snap as before.

For demo / fallback rendering (`DemoRiveDesignRegistry`), a per-field `_TweenedNumber` helper drives an `AnimationController` with the supplied curve + duration. For live Rive (`AssetRiveDesignRegistry`), `anim` is **advisory only** — the designer-authored State Machine owns visual timing, so the LLM-supplied tween params are dropped at the silo boundary and the value is written instantaneously to the `ViewModelInstance`. This keeps the contract uniform across both registries while preserving the rule that designers — not the LLM — control motion in production.

The schema is locked at v1; future expansions (color tweens, `delayMs`, stagger metadata) bump to `@anim-schema:v2` and ship with explicit migration. The `.feature` files for each scenario carry the `@anim-schema:v1` tag so cortex / breadth search can find scenarios by schema version.

### 6.4 Fail-safe degradation

If `Compose` fails (LLM down, skeleton parse rejects, palette mismatch): server emits a single skeleton frame containing the v1 hand-coded card composition for the intent + a final empty data delta. The Flutter client never sees an error state from this path; it just gets old-style RFW in the same frame envelope. **This is the only intentional fallback in the system.** No silent error masking elsewhere.

---

## 7. New components in the codebase

### 7.1 Server side

| Path | Project | Role |
|---|---|---|
| `src/Ino.Kernel/UI/UIComposer.cs` | Ino.Kernel | LLM-backed composer; `IChatClient` streaming + skeleton/delta emission |
| `src/Ino.Kernel/UI/IUIPalette.cs` | Ino.Kernel | Palette interface; merges per-domain schemas at request time based on installed/active domains |
| `src/Ino.Kernel/UI/RfwValidator.cs` | Ino.Kernel | Parses + validates skeleton `.rfwtxt` against the merged library set; rejects unknown widgets/properties or unknown `(domain, artboard)` references |
| `src/Ino.Kernel.Contracts/ComposedUI.cs` | Ino.Kernel.Contracts | `ComposeRequest` + `ComposeFrame` (skeleton/delta) records |
| `src/Ino.Gateway.Grpc/Protos/ui.proto` | Ino.Gateway.Grpc | `rpc Compose(ComposeRequest) returns (stream ComposeFrame)` |
| `clients/ino.flutter/tool/rive_schema_gen.dart` | Flutter (build-time tool) | Reads every `assets/rive/*-design.riv`, emits sibling `*-design.schema.json` |

### 7.2 Client side

| Path | Role |
|---|---|
| `clients/ino.flutter/lib/ui/rive_widgets.dart` | New `createRiveWidgets()` LocalWidgetLibrary; one `_RiveArtboard` State class + thin per-artboard wrappers |
| `clients/ino.flutter/lib/ui/rive_design_registry.dart` | Resolves `(domain, artboard)` → `BindableArtboard`; preloads kernel baseline; lazy-loads per-domain on first reference |
| `clients/ino.flutter/lib/ui/composed_view.dart` | Subscribes to `Compose` server-stream; mounts `RemoteWidget` on skeleton; merge-patches `DynamicContent` on each delta |
| `clients/ino.flutter/lib/ui/ino_runtime.dart` | Add `ino.rive` registration |
| `clients/ino.flutter/lib/grpc/ino_client.dart` | New `Stream<ComposeFrame> compose(intent, payload)` method |
| `clients/ino.flutter/assets/rive/ino-design.riv` | Kernel-baseline asset (designer task) |
| `clients/ino.flutter/assets/rive/travel-design.riv` | Travel-domain asset (designer task) |
| `clients/ino.flutter/assets/rive/taxi-design.riv` | Taxi-domain asset (designer task) |

### 7.3 RFW validator subagent

`RfwValidator` runs the same `rfw` parser the client uses (the Dart parser has a Dart-only spec; we either ship a small dotnet helper that shells `dart run` against a tiny validator script during build/test, or — preferred — embed the rfw textual grammar in a Pidgin/ANTLR grammar inside `Ino.Kernel`. Decision deferred to slice 1, see §10.5).

---

## 8. Data flow per turn

```
Flutter:                          Server:
─────────                         ──────
user types  ─chat.send──────────► InoNeuron.HandleAsync
                                     │
                                     ├─ plans, fires capability synapses
                                     │  (StreamEvents continues to push journal events)
                                     │
                                     └─ Compose(intent, sketchPayload, palette)
                                        │
                                        ├─ stream-prompt LLM (skeleton FIRST,
                                        │  payload SECOND); BDD mock returns canned
                                        │
                                        ├─ skeleton emitted as full token stream finishes
                                        │  RfwValidator.Parse(skeleton, mergedLibraries)
                                        │     ↓ rejects → emit v1 fallback skeleton
                                        │
ComposeFrame {skeleton}      ◄───────── ok: server-stream emits skeleton frame
   │
   ▼
ComposedView mounts RemoteWidget
   │  cells render Rive idle/empty variants
   │  rive.File preload: kernel baseline now;
   │  per-domain on first reference
   │
   ▼                                  (concurrently, server-side)
                                     ├─ as capability synapses return:
                                     │     - merge-patch payload into accumulator
                                     │     - emit ComposeFrame {data: patch}
                                     │
ComposeFrame {data} ×N       ◄──────  
   │
   ▼
DynamicContent.update(merge-patch)
   │
   ▼
RFW reactivity drives _RiveArtboard
   │  per-mount: VM property writes,
   │  Rive State Machine transitions to non-empty states
   │
   ▼
trigger fires → RFW event → ino_bloc → gRPC chat.send (next turn)
```

OTel: every `Compose` is a span; each delta a child span. Spans link to the parent `Chat` via `traceparent`. Counters: `ino.ui.compose.attempts`, `ino.ui.compose.fallbacks`, `ino.ui.compose.deltas`. Histograms: `ino.ui.compose.skeleton_ms` (target p95 < 600ms), `ino.ui.compose.last_delta_ms` (target p95 < 1500ms).

---

## 9. Performance + reliability budget

Honoring saved feedback "IAW/ino closed loop must be fast (<1 min), not slow multi-LLM chains":

- **Skeleton latency** — p95 < 600ms. Composer LLM uses a small fast model (Sonnet 4.6 or smaller) prompted to emit structure first. Skeleton is small and predictable, so the prompt finishes quickly.
- **Last-delta latency** — p95 < 1500ms. Capability synapses run in parallel; their results land as merge-patches as they return. The user sees motion + structure immediately and content fills in.
- **No multi-step UI chains.** One Compose per turn, one LLM call. The deltas are just data — they don't go through the LLM. If the composer wants to "iterate on layout", that's a code smell — palette must be expressive enough that one shot suffices.
- **BDD mocking.** Per saved feedback "Neuron behavior tests use Gherkin/Reqnroll", every Compose scenario gets a `.feature` file under `src/Ino.Kernel.Tests/Features/UIComposer.feature`. The mock client emits a canned skeleton + scripted delta sequence so streaming flows are testable too. Dev loop instant; prod flips to real LLM via `Ino.Llm.Provider` config.
- **No core changes without approval.** This spec leaves `Ino.Core` untouched; all new code is in `Ino.Kernel` (host-side) and Flutter. Per saved feedback.

---

## 10. Open decisions

Three locked at top of doc. Three remain:

### 10.4 Designer commitment (open)

v3 only pays off if there's a real designer (or willing engineer) authoring `ino-design.riv` + per-domain `.riv`s properly with Layouts + Luau Converters. Per-domain compounds this — Travel and Taxi each need their own designer attention. Without commitment we ship placeholder rectangles and v3 looks worse than v1. Two viable shapes:

- **Shape A (cautious):** Ship behind `Ino.Ui.Composer.Enabled=false`. Author kernel baseline only at first; per-domain assets ship empty (composer falls through to kernel for everything). Domain assets land as designer time becomes available; flag flips per domain.
- **Shape B (forcing function):** Ship enabled with placeholder `.riv`s in every domain that just delegate to kernel artboards. Visible "needs designer" state in the UI. Marketplace incentive — domains without good designs look generic.

### 10.5 `RfwValidator` strategy (open)

- **A. Dart helper at build / test.** Spawn `dart run` against a tiny `tool/rfw_validate.dart` script that uses the actual `rfw` parser. Robust (always matches client). Slow (~200ms per call, dotnet ↔ dart IPC). Good for tests; risky as a per-request server-side check.
- **B. C# parser embedded in `Ino.Kernel`.** Pidgin or hand-rolled. Fast. Drift risk if rfw spec changes. Higher initial cost.
- **C. Hybrid.** C# parser in the request path; Dart helper in CI as a periodic conformance test that asserts B's parser matches A's parser on a corpus.

Recommendation: **C** — but it's the most code. Would accept B if you want to move faster.

### 10.6 Persona widget unification (open)

v0.1 has a native `PersonaWidget` at top-of-screen. v3 introduces `PersonaInline` inside composed output. They share state but render twice when both are visible. Three options:

- **Keep both.** Top-of-screen always shows the global persona; LLM places `PersonaInline` ad hoc. Decision 5 (chrome = native) preserved.
- **Demote top-of-screen.** Remove the always-on widget; LLM is responsible for placing a persona somewhere relevant per turn. Cleaner but means the persona disappears from screens where the LLM forgets.
- **Top-of-screen becomes a `RemoteWidget` of one cell.** Native shell wraps a Rive `PersonaInline` mount fed by BLoC. Best of both — kept on every screen, no double-render. Tiny refactor.

Recommendation: **third option** — but tell me if you want the second for purity.

---

## 11. Slices

Each slice is end-to-end and gated by the v0.1 verification loop (build → test → `aspire run` → browser verify → E2E).

### Slice U.1 — `ino.rive` library + kernel baseline, one artboard, hand-rolled `.rfwtxt`

**Build.** Author placeholder `ino-design.riv` with kernel `Hero` only (no Luau, basic Layouts, idle/empty State Machine variant). Implement `createRiveWidgets()` with the unified `_RiveArtboard` class + `Hero` thin wrapper. Add `rive_design_registry.dart` with kernel preload only. `composed_view.dart` mounts a hard-coded `.rfwtxt` referencing `Hero` with `domain: "kernel"`. No LLM, no streaming yet.

**Test.** Widget test (`flutter_test`) instantiates `RemoteWidget` with the hard-coded text + sample data, asserts `_RiveArtboard` is in the tree and `dataBind` is wired (mocktail on `RiveWidgetController`). Golden screenshot under `clients/ino.flutter/test/golden/hero_320.png`, `_600.png`, `_1200.png`.

**Done when.** Browser shows a Rive `Hero` driven from RFW data at three breakpoints. No LLM in the loop yet.

### Slice U.2 — full kernel baseline + schema generator

**Build.** All five kernel artboards in `ino-design.riv` (Hero, Tile, Badge, PersonaInline, Spacer). `tool/rive_schema_gen.dart` reads every `assets/rive/*-design.riv`, emits sibling `*-design.schema.json`. MSBuild target wires schema gen into the Flutter build (after `flutter pub get`, before `flutter build web`).

**Test.** Schema golden tests assert every artboard + property is exposed. Widget test + golden per kernel component.

### Slice U.3 — per-domain `.riv` plumbing + Travel design system

**Build.** `travel-design.riv` with `DayCard`, `FlightStrip`, `MapPin` — designer task. Lazy loader in `rive_design_registry.dart` resolves `(domain, name)` with kernel fallback. Schema generator picks up the new file automatically.

**Test.** Resolver unit test: `(travel, Hero)` → travel artboard if defined else kernel. `(taxi, MapPin)` → fails resolution (good — composer should not have requested it). Widget tests for travel-specific artboards.

### Slice U.4 — `UIComposer` + `RfwValidator`, BDD-mocked, server-streaming

**Build.** `Ino.Kernel/UI/UIComposer.cs` (server-streaming gRPC method emitting `ComposeFrame`s) + `IUIPalette` impl that merges per-installed-domain schemas at request time. `RfwValidator` (decision §10.5 — pick before this slice). `BddMockChatClient` returns canned skeleton + scripted delta sequence for Tokyo and Taxi scenarios. New `rpc Compose` in `ui.proto` with `stream` return.

**Test.** Reqnroll `.feature`: three scenarios (Tokyo trip skeleton + 4 deltas, Taxi quote skeleton + 2 deltas, palette-mismatch fallback). Server-side: assert every emitted skeleton validates. Per saved feedback "Neuron behavior tests use Gherkin/Reqnroll".

### Slice U.5 — Flutter wires streaming `Compose` into chat slot

**Build.** `ino_client.compose()` returns a `Stream<ComposeFrame>`. `ComposedView` mounts on first skeleton frame, applies merge-patch on each delta. Fallback to v1 RFW cards on skeleton parse failure (server-side already handled — client just renders whatever skeleton arrives). Per saved feedback "UI layer owns client UX, response agents stay platform-agnostic": no platform-specific reasoning leaks server-side.

**Test.** Aspire E2E test (`Ino.E2E.Flutter.Tests`) drives the Tokyo scenario, intercepts streamed gRPC frames, asserts skeleton-then-deltas sequence, screenshots after skeleton (cells empty) and after final delta (cells filled), asserts persona top-of-screen still pulses (no regression).

### Slice U.6 — Real LLM, latency budgets, observability

**Build.** Flip `Ino.Llm.Provider` to `xai`/`anthropic` in production AppHost; keep `bdd-mock` in dev. Add OTel counters/histograms from §8. Add p95 assertions to E2E (skeleton >600ms or last-delta >1500ms fails).

**Test.** Cross-domain trace filter test: filter Aspire traces by `service.name=Ino.Kernel` and assert each `Compose` span has `traceparent` linked to the parent `Chat`, with delta children correctly chained.

### Slice U.7 — Taxi domain `.riv` ships, marketplace install demo

**Build.** `taxi-design.riv` (`RidePill`, `FareBreakdown`). Marketplace-install path drops the asset into `assets/rive/`, Flutter rebuild picks it up. Demonstrates the per-domain `.riv` story end-to-end.

**Test.** Install Taxi → palette includes Taxi components; uninstall Taxi → palette excludes them; composer falls back to kernel `Tile` for ride concepts.

---

## 12. Out of scope (defer to post-v3)

- **Luau scripts authored by Claude.** Out (no runtime API). Future epic: "scripted design-system component" pipeline — Claude proposes Luau, designer-or-tool runs it through Rive editor + AI Agent, exports new `.riv`, ships via marketplace.
- **Per-user personalised design systems.** One `ino-design.riv` ships with the app. User-specific Rive content is post-v3.
- **LLM-driven theme / dark mode swap.** Goes through `ColorScheme`, not Rive.
- **Replacing chrome (Mind/Live/Trace tabs) with composed UI.** Decision 5 stands — chrome is native.
- **Multi-turn UI iteration.** One Compose per turn. If user says "redo that card", that's a fresh turn → fresh Compose.

---

## 13. Risks

- **Designer bottleneck × per-domain.** Per-domain `.riv` multiplies the designer ask. Mitigation tied to §10.4 — pick Shape A (cautious flag) until at least kernel + Travel land.
- **LLM emits broken skeleton.** Mitigation: server-side `RfwValidator` rejects → server emits v1-fallback skeleton. Counter `ino.ui.compose.fallbacks` ratio is the canary; >20% fallback over 24h triggers a model-prompt review.
- **Streaming starvation.** A delta that never arrives leaves cells in their empty/idle state forever. Mitigation: server attaches a deadline per delta source (capability synapse); on timeout emits a synthetic `{"<path>": {"state": "unavailable"}}` patch so designer-authored unavailable states render. No silent hangs.
- **Merge-patch ordering.** Out-of-order deltas could regress state. Mitigation: deltas carry monotonic sequence numbers; client drops or queues stale ones.
- **Rive Layouts breakpoints don't match Flutter container queries.** One artboard looks great at the design size but breaks at, say, 480px. Mitigation: golden screenshot tests at 320 / 600 / 1200 widths per artboard, per domain.
- **`rive: ^0.14.5` lacks Luau runtime.** If the shipping `rive_flutter` doesn't yet bundle the Luau VM, the design system can still ship (Layouts + DataBinding are enough). Luau Converters become a "later" thing. Confirm version + capability in slice U.1 via Context7.
- **CRLF gotcha** (existing). `RfwValidator` and the Compose pipeline must strip `\r` from skeleton bytes before sending — same fix as `InoService.TryBuildRfw`.
- **Cross-domain palette merge collisions.** Two domains define `Hero` with conflicting VM properties. Mitigation: schema generator detects collisions at build time and fails the build unless one is explicitly marked as overriding the other.

---

## 14. Why this is the most exciting v3

The v0.1 demo proves "ino is a real assistant with real ML". v3 proves something further: **the assistant's UI is itself an output of the assistant.** Every screen the user sees is a fresh composition the LLM made for that moment, against a vocabulary a designer hand-crafted. That collapses the historical split between "the app" and "the conversation" into a single moving thing. Nobody's shipped that with the motion-design fidelity Rive Layouts + Scripting now make possible.

Feedback loops that get unlocked:

- **Cortex routing learns visual idioms.** When two intents always compose to similar `Hero+Tile×n` layouts, Cortex can hint that to the composer for a faster prompt.
- **Persona preferences feed UI.** `PersonaStateModel.emotion=focus` → composer prefers compact `Tile` over hero treatments. Already typed; no new infra.
- **Marketplace components.** A travel domain author ships their own `travel-design.riv` with bespoke Tiles that Cortex's composer can pick when the active domain is Travel. This becomes the visual analogue of the "neurons compose" thesis.

That's the bet. Decisions in §10 gate the first slice.
