# ino — 200 Domain Cards, Dynamic Persona, and Living System Design

**Date:** 2026-04-11
**Status:** Draft
**Scope:** Domain integration cards (RFW), dynamic persona with Rive animations, Genesis model (Creator/ino), Timeline/Branch/Brain View, self-aware telemetry

---

## 1. Overview

ino is an AI-native OS that integrates into every existing OS. This spec defines:

1. **203 domain integration cards** across 25 domains, pushed via RFW into a universal chat surface
2. **Dynamic persona system** with Rive state machine animations, find/create/cache pipeline
3. **Genesis model** — Creator (neuron #0) and ino (neuron #1) as the two primordial neurons
4. **Timeline, Branch, and Brain View** as user-facing features with updated terminology
5. **Self-aware telemetry** — ino can visualize its own metrics, growth, and behavioral patterns

The chat surface is universal. No new screens per domain. Skills push specialized RFW cards into chat. The persona sits at the top of the screen, always visible, always reacting. The contextual UI fills the bottom, generated live by ino.

---

## 2. User-Facing Terminology

All internal code stays as-is (neurons, synapses, grains). The user-facing UI uses lightweight brain metaphor:

| Internal (Code) | User-Facing | Example in UI |
|---|---|---|
| Neuron / Agent | **Skill** | "ino learned a new skill: Uber" |
| Synapse / Message | **Signal** | "3 signals between Uber and Maps" |
| Synapse (persisted) | **Memory** | "ino remembers your home address" |
| Time Travel | **Timeline** | "Open timeline" — scrub through moments |
| Timeline Event | **Moment** | "Rewind to this moment" |
| Parallel Universe / What-If | **Branch** | "Create a branch: take the train instead" |
| System Visualization | **Brain View** | "Open brain view" — zoomable map |

---

## 3. Genesis Model — Two Primordial Neurons

### 3.1 Creator (neuron #0)

The first neuron. Exists before everything else. Its only job: create other neurons on demand.

- Never talks to the user directly
- Never makes decisions about user intent
- Receives creation requests from ino via signals
- Creates skill neurons: system prompt + tool selection + Roslyn script
- Persists new skills to `AgentRegistry` (L1 creation — no silo restart)

### 3.2 ino (neuron #1)

The second neuron. Created by Creator at system birth. User-facing companion.

- Communicates with the user
- Interprets user intent and routes to skills
- When a needed skill doesn't exist, fires a signal to Creator: "I need an UberSkill"
- Creator creates it, ino connects to it, user sees the result
- Never creates neurons directly — always delegates to Creator

### 3.3 System Birth Sequence

```
t=0.000s  Creator activated (neuron #0)
t=0.001s  Creator creates ino (neuron #1)
t=0.003s  User signs in with Google
t=0.005s  ino → Creator: "need GoogleAuth skill"
t=0.006s  Creator creates GoogleAuth (#2), Gmail (#3), Calendar (#4), Drive (#5)
t=1.000s  User: "get me to work"
t=1.002s  ino → Creator: "need transport skill — Uber API available"
t=1.004s  Creator creates UberSkill (#6), asks user for OAuth consent
t=1.010s  ino → UberSkill: "call ride Home → Office"
t=1.015s  Uber ride card appears in chat
```

Every skill in the system was created by Creator. The timeline traces this growth from Day 0.

---

## 4. Domain Research — 203 Apps, 25 Domains

### 4.1 Summary Statistics

| Metric | Count |
|---|---|
| Total apps cataloged | 203 |
| Domain clusters | 25 |
| READY (public API + OAuth/key) | 105 (52%) |
| PLANNED (limited/partner API) | 55 (27%) |
| VISION (no public API) | 43 (21%) |

### 4.2 Domain Clusters (sorted by integration readiness)

| Domain | Apps | READY | Key Apps |
|---|---|---|---|
| Developer Tools | 8 | 8 | GitHub, GitLab, Vercel, AWS, Azure, Jira |
| Productivity | 10 | 10 | Drive, Docs, Sheets, Office 365, Notion, Todoist, Trello, Asana |
| Cloud Storage | 5 | 4 | Dropbox, OneDrive, Google Photos, Box |
| Social Media | 10 | 8 | Instagram, TikTok, Facebook, X, Reddit, LinkedIn, Pinterest |
| Messaging | 10 | 8 | WhatsApp, Telegram, Discord, Slack, Teams, Zoom, Meet |
| Finance | 10 | 7 | PayPal, Revolut, Wise, Coinbase, Binance, Stripe |
| Home & IoT | 7 | 5 | Google Home, Alexa, SmartThings, Hue, Nest |
| Shopping | 10 | 6 | Amazon, eBay, Etsy, Shopee, AliExpress, Zalando |
| Navigation | 6 | 4 | Google Maps, Apple Maps, HERE, Mapbox |
| Music | 8 | 5 | Spotify, Apple Music, Deezer, SoundCloud, Podcasts |
| Weather | 4 | 4 | Weather.com, AccuWeather, Dark Sky, Windy |
| Travel | 10 | 5 | Booking.com, Expedia, Skyscanner, Tripadvisor, Flightradar |
| Fitness | 8 | 4 | Strava, Garmin, Fitbit, WHOOP |
| Email | 5 | 3 | Gmail, Outlook, Yahoo Mail |
| Creative | 7 | 4 | Canva, Adobe CC, Figma, Unsplash |
| Education | 8 | 4 | Coursera, Udemy, Google Translate, ChatGPT |
| Payments | 8 | 3 | Google Pay, Alipay, WeChat Pay, Klarna |
| Gaming | 6 | 3 | Steam, Xbox, Roblox |
| News | 7 | 3 | Google News, NYT, Reuters, Feedly |
| Grocery | 7 | 3 | Amazon Fresh, Walmart, Mercado Libre |
| Food Delivery | 8 | 1 | Uber Eats (only READY); DoorDash, Deliveroo (PLANNED) |
| Utilities | 6 | 2 | Google Calendar, 1Password, Shazam |
| Transport | 10 | 2 | Uber, Google Maps (READY); Lyft, Grab, Bolt (PLANNED) |
| Video | 8 | 2 | YouTube, Twitch (READY); Netflix, Disney+ (VISION) |
| Health | 8 | 0 | All PLANNED or VISION — Apple Health, Headspace, Calm |

### 4.3 Auth Landscape

~80% of READY apps use OAuth2. Three auth tiers:

1. **Google ecosystem (instant)** — Gmail, Drive, Calendar, Maps, YouTube, Photos, Meet, Home. All unlocked by Google Sign-In.
2. **OAuth2 consent (one-tap per service)** — Spotify, Uber, GitHub, Notion, Slack, Figma, Strava, +90 more. ino asks user to authorize, stores token.
3. **API key (auto-managed)** — Weather, News, Maps, Translate. No user interaction needed; ino manages keys internally.

### 4.4 Regional Champions

Global product must account for non-US dominance:
- **China:** Alipay, WeChat Pay, DiDi
- **India:** PhonePe, Paytm, Swiggy, Zomato, Flipkart
- **SEA:** Grab, Shopee
- **LATAM:** Rappi, Mercado Libre

---

## 5. Card Design System

### 5.1 Architecture

All domain cards are RFW (Remote Flutter Widgets) pushed from the backend into the universal chat surface. No new Flutter screens per domain.

```
User message → ino interprets → routes to skill →
skill calls API → returns structured data →
ino selects RFW card template → pushes card into chat
```

### 5.2 Card Anatomy

Every card shares a common structure:

```
┌─────────────────────────────────┐
│ [Icon] Domain · Action   [TAG]  │  ← Header: skill icon, title, readiness tag
├─────────────────────────────────┤
│                                 │
│  [Domain-specific content]      │  ← Body: varies per domain
│  Maps, charts, controls,        │
│  progress bars, device grids    │
│                                 │
└─────────────────────────────────┘
```

Tags: `READY` (green), `PLANNED` (yellow), `VISION` (gray) — shown in Skills tab, not in normal chat.

### 5.3 Card Types by Domain (8 examples, 203 total)

| Card | Content | Key Elements |
|---|---|---|
| **Ride** (Uber) | Mini route map, pickup/dropoff, ETA, price | Animated car dot, green/red endpoint dots |
| **Now Playing** (Spotify) | Album art gradient, track/artist, controls | Play/pause/skip, audio visualizer bars |
| **Portfolio** (Coinbase/Robinhood) | Total value, daily change, sparkline chart | Green/red per-ticker, mini area chart |
| **Weather** (AccuWeather) | Current temp + icon, 5-day forecast row | Large temp, condition icon, forecast grid |
| **Smart Home** (Google Home) | 2x2 device grid with on/off state | Per-device icon, toggle state, room name |
| **Food Order** (Uber Eats) | Restaurant, items, progress bar, ETA, total | Order status badge, delivery progress |
| **PR Review** (GitHub) | PR title, author, file count, +/- lines, CI | Green/red diff counts, CI status badge |
| **Calendar** (Google) | Next 2-3 events with color-coded left border | Time, title, meeting link, color by calendar |

### 5.4 Stitch Generation Plan

Each domain gets a Stitch project with 6-10 card variations:
- 25 Stitch projects (one per domain)
- ~8 cards per project average
- Total: ~200 screens in Stitch
- Design system (`.stitch/DESIGN.md`) ensures visual consistency
- Generated HTML serves as the RFW card reference for Flutter implementation

Stitch generation uses the `stitch-loop` pattern: one project per domain, iterating through card variations, maintaining design system coherence via `DESIGN.md`.

### 5.5 Persona Affects Card Style

Cards adapt their visual style to the active persona:
- **Jarvis**: clean, minimal, data-dense, monochrome accents
- **Luna**: colorful, rounded corners, friendly copy, emoji
- **Cortex**: green-on-black, terminal-style, no decoration
- **Coach**: bold metrics, progress bars, comparison charts

The card template system supports persona-keyed style tokens (border-radius, color palette, typography weight, copy tone).

---

## 6. Dynamic Persona System

### 6.1 Core Concept

The persona is user-assignable. User says "you are Jarvis" and ino transforms — animation, voice, behavior, card style, proactivity level all shift.

### 6.2 Persona Components

Each persona consists of:

| Component | Description | Storage |
|---|---|---|
| **Rive animation** | State machine with 12 states, persona-specific artwork | Blob storage, keyed by persona slug |
| **Voice profile** | Tone, formality, humor level, regional accent | Memory (decay=100) |
| **Behavior traits** | Trait weights: formal/casual, proactive/reactive, verbose/terse | Memory (decay=100) |
| **System prompt modifier** | Appended to ino's base prompt to shift personality | Memory (decay=100) |
| **Card style tokens** | Border-radius, palette, typography overrides | Memory (decay=100) |

### 6.3 Rive State Machine — 12 Universal States

Every persona implements the same 12 states with different artwork:

| State | Trigger | Jarvis Example | Luna Example |
|---|---|---|---|
| Sleeping | No activity | Dim holographic ring | Crescent moon, slow breathing |
| Idle | Waiting for input | Gentle ring pulse | Soft glow, floating |
| Listening | User typing/speaking | Ring brightens, leans in | Moon opens eyes |
| Thinking | LLM call active | Rotating holographic ring | Pulsing moon phases |
| Acting | Tool/API call | Ring fragments, reassembles | Stars shooting outward |
| Speaking | Streaming response | Ring projects text glyphs | Moon radiates warmth |
| Celebrating | Task completed | Ring flashes gold | Confetti burst |
| Confused | Error/ambiguity | Ring flickers red | Moon tilts, question mark |
| Searching | Querying APIs | Ring scans horizontally | Telescope animation |
| Creating | Generating content | Ring builds new structure | Paint strokes |
| Urgent | Alert/time-critical | Ring pulses red rapidly | Moon turns red |
| Welcoming | New user/morning | Ring expands warmly | Sunrise animation |

State transitions are driven by real system signals: `LlmCallStarted` → Thinking, `ToolInvoked` → Acting, `SynapseFired` → signals pulse on the ring, etc.

### 6.4 Find/Create/Cache Pipeline

```
User: "you are Jarvis"
  │
  ▼
1. Parse Intent (~200ms)
   Extract: persona name, traits, voice, visual reference
  │
  ▼
2. Cache Lookup (~50ms)
   Key: persona:{slug}
   Store: Memory with decay=100 (permanent until deleted)
  │
  ├─ HIT → 3a. Load (~300ms)
  │         Stream .riv file from blob storage
  │         Cross-fade from current persona (1.5s transition)
  │         Bind state machine inputs to system signals
  │
  └─ MISS → 3b. Create (~5-15s first time)
            PersonaCreator skill generates:
              - Rive file from template state machine + style transfer
              - Voice profile from trait analysis
              - Behavior weights from persona description
            Save all to Memory (decay=100)
            Then proceed to 3a (Load)
  │
  ▼
4. Activate (~100ms)
   State machine inputs bound to system signals
   Persona is live
```

### 6.4.1 Rive Generation Strategy — Rive Editor MCP Server

**Verified 2026-04-11:** Rive ships an official **Editor MCP Server** (early access) that connects the Rive Editor to AI tools via Model Context Protocol. This changes the persona creation story fundamentally.

**Three paths, updated with MCP reality:**

1. **Rive Editor MCP (recommended, available now in early access):** PersonaCreator skill connects to the Rive Editor via MCP and creates persona animations from natural language descriptions. The MCP server provides 132+ tools for creating State Machines (with hundreds of states/layers), Shapes, Layouts, View Models, Transitions, and Conditions — all programmable from LLM prompts. Example: "Create a State Machine with 12 states: sleeping (dim holographic ring, slow pulse), idle (gentle ring pulse), thinking (rotating holographic fragments), acting (ring shatters and reassembles)..." The Rive Editor builds the animation, exports `.riv`, ino caches it.

   - **Pros:** Full creative control per persona. Each persona gets genuinely unique artwork, not just parameter swaps. The LLM describes what it wants in natural language, Rive builds it.
   - **Cons:** Requires Rive Editor running as a service (headless or cloud). Early access API surface may change. Currently recommended with Cursor; integration into ino's Orleans silo needs an MCP client bridge.
   - **Architecture:** `PersonaCreator skill` → `MCP client` → `Rive Editor MCP Server` → `.riv file` → blob storage → Flutter runtime loads and binds state machine inputs.

2. **Direct .riv binary generation (v2, for offline/edge):** The `.riv` format is fully documented — binary, little-endian, varuint-encoded objects and properties, file header "RIVE" (4 bytes), major version 7. An LLM could generate `.riv` files directly by emitting the binary representation. The `rive-code-generator-wip` tool (official, on GitHub) already parses `.riv` and extracts components to Dart/JSON, proving the format is machine-readable. Reverse path (JSON → `.riv`) is feasible. This path enables fully offline persona generation without the Rive Editor.

3. **Template + runtime inputs (fallback/fast path):** Ship 5 preset `.riv` templates (one per preset persona). PersonaCreator maps custom personas to the closest template and manipulates state machine inputs at runtime via Flutter's `SMINumber`, `SMIBool`, `SMITrigger` APIs. Inputs control color, speed, glow intensity, orbit count — but the shape language stays fixed per template. Used as a fast fallback while the MCP-generated animation loads, or when Rive Editor MCP is unavailable.

**Recommended approach:** Path (3) for instant response (template loads in ~300ms), then Path (1) generates the real persona animation in background (~5-15s via Rive Editor MCP). Once ready, cross-fade from template to custom animation. Cache the custom `.riv` forever. Next time = instant.

**Self-improvement loop:** After the initial generation, ino can iteratively improve the animation. If the user says "Jarvis should be more angular" or "make the ring spin faster," PersonaCreator sends follow-up MCP commands to edit the existing Rive file, re-exports, and updates the cache. The animation literally improves over time based on user feedback — just like ino's skills improve.

**Integration architecture:**
```
PersonaCreator skill (Orleans grain)
  → fires MCP request via ino's MCP client bridge
  → Rive Editor MCP Server (headless, runs as Aspire resource)
    → creates artboard + 12 state machine states + shapes + transitions
    → exports .riv binary
  → .riv saved to blob storage (keyed: persona:{slug})
  → Flutter runtime: RiveAnimation.network(blobUrl)
    → binds SMIBool/SMITrigger to system signals
    → persona is live
```

Sources verified:
- Rive MCP Integration: https://rive.app/docs/editor/mcp/integration
- Rive .riv format spec: https://rive.app/docs/runtimes/advanced-topic/format
- Rive Flutter state machines: https://help.rive.app/runtimes/state-machines
- rive-code-generator-wip: https://github.com/rive-app/rive-code-generator-wip
- RiveMCP (132 tools): https://rivemcp.stunning.gg/

### 6.5 Preset Personas

Ship with 5 presets (cached from day one, no generation delay):

| Persona | Vibe | Traits | Use Case |
|---|---|---|---|
| **Jarvis** | Calm, precise, British wit | formal, analytical, proactive | Power users, executives |
| **Luna** | Warm, creative, encouraging | playful, creative, empathetic | Creative work, casual use |
| **Cortex** | Fast, technical, no-nonsense | efficient, direct, technical | Developers, engineers |
| **Coach** | Motivating, pushy, competitive | intense, motivating, tracking | Fitness, productivity |
| **Sage** | Thoughtful, philosophical, calm | wise, patient, reflective | Learning, decision-making |

Custom personas: user says "you are my dog Rex" or "you are a pirate" → PersonaCreator generates on first use, cached forever after.

### 6.6 Persona Affects Everything

The active persona modifies every surface:

| Surface | How It Changes |
|---|---|
| Chat voice | Copy tone: "Shall I arrange transport, sir?" vs "Want a ride?" |
| Card style | Visual tokens: border radius, color warmth, data density |
| Brain View theme | Layout style: holographic vs organic vs grid |
| Timeline narration | Event descriptions adapt: formal vs casual vs metric-focused |
| Branch language | "I've modeled an alternative" vs "What if we tried..." |
| Proactivity | How aggressively ino anticipates needs |

---

## 7. Screen Layout

### 7.1 Structure

```
┌──────────────────────────────────┐
│         PERSONA ZONE             │  ← Always visible, always reacting
│   [Rive animation + status]      │     Emotion, activity pips, one-line status
│   "Arranging transport, sir."    │
├──────────────────────────────────┤
│                                  │
│       CONTEXTUAL UI ZONE         │  ← Generated live by ino
│                                  │     Cards, charts, briefings, telemetry
│   Whatever ino is doing right    │     Changes based on current activity
│   now appears here               │
│                                  │
├──────────────────────────────────┤
│  [Chat] [Timeline] [Branch]      │  ← Bottom nav: 5 tabs
│  [Brain View] [Skills]           │
└──────────────────────────────────┘
```

### 7.2 Persona Zone Behavior

- Shrinks when content is dense (long chat thread)
- Expands when idle (morning briefing, waiting)
- Orb ring color shifts with emotion state
- Activity pips pulse with signal frequency
- One-line status shows what ino is actively doing: "Calling Uber API..." / "Idle · 3 skills active"

### 7.3 Contextual UI Zone

ino generates what to show based on context:
- **Morning idle**: daily briefing card (weather, calendar, portfolio, unread emails)
- **Active task**: the relevant domain card (ride, music, order status)
- **Telemetry request**: live-built charts, counters, sparklines
- **Timeline scrub**: state snapshot at the selected moment
- **Branch comparison**: side-by-side outcome grid

The UI is not designed statically — ino decides what to render based on the current signal state.

---

## 8. Timeline

### 8.1 Concept

A horizontal scrubber showing your day as moments. Drag to any moment to see exactly what ino was doing and why.

### 8.2 Elements

- **Timeline track**: horizontal bar from day start to now (or selected date range)
- **Moment dots**: colored by type (green = user action, purple = ino action, orange = API call, gray = future/faded)
- **Scrubber handle**: draggable, shows tooltip with moment description
- **State panel** (below scrubber): two columns — Active Skills + Signals at this moment
- **"Why?" tap**: tap any signal to get ino's explanation of why it did that

### 8.3 System Growth View

The timeline doubles as a system growth trace:
- Scroll to Day 0: see only Creator + ino
- Scroll forward: watch skills being created one by one
- Moment dots appear as the system grows
- Brain View syncs with timeline scrubber — rewind the brain to any past state

### 8.4 Existing Implementation

`features/timetravel/` already has:
- `TimelineGrain` with ring-buffer storage (cap 10,000 events), decay filtering
- `TimelineCallFilter` capturing `SynapseFired` events
- MCP tools: `timetravel_list_events`, `timetravel_count_by_kind`, `timetravel_get_state_at`, `timetravel_latest_sequence`
- Flutter `TimeTravelBloc` with scrubber and snapshot caching

Rename requires a merge: `TimeTravelBloc` (scrubber + snapshots) and `TimelineBloc` (live event stream) currently coexist as separate BLoCs. Merge into a single `TimelineBloc` with two modes: **Live** (streaming new events, play/pause, decay filter) and **Scrub** (historical navigation, snapshot caching, state-at queries). The mode toggle replaces the current separate tabs. `time_travel_screen.dart` and `timeline_screen.dart` merge into one unified `timeline_screen.dart`.

---

## 9. Branch

### 9.1 Concept

Fork at any moment on the timeline. Explore what would have happened if you'd made a different choice. Compare reality vs the branch side-by-side.

### 9.2 Flow

```
1. User scrubs timeline to a moment (e.g., "Called Uber at 9:00")
2. Taps "Branch" → dialog: "What would you change?"
3. User: "Take the train instead"
4. ino forks the timeline at that moment
5. ino replays from fork point with the modified event
6. Side-by-side comparison appears:
   Reality (Uber: $14.50, 12 min, on time)
   Branch  (Train: $2.50, 28 min, 2 min late)
7. ino summarizes: "Saved $12 but lost 16 min and was late to standup"
```

### 9.3 Visual Elements

- **Fork diagram**: trunk line → fork point (red dot) → two diverging branches
- **Branch labels**: "Reality: ..." and "Branch: ..." with color coding
- **Comparison grid**: side-by-side event lists, shared events grayed, divergent events highlighted
- **Summary line**: ino's assessment of the tradeoff

### 9.4 Existing Implementation

`features/ino-new/InoNew.Core/IUniverse.cs` already has:
- `ForkAsync(sourceTimeline, checkpointSequence, modifiedEvent)`
- `ReplayAsync()` → `ReplayResult`
- `CompareAsync(otherUniverseId)` → `UniverseDiff`
- Flutter `UniverseBloc` with fork dialog and diff view

Rename: `UniverseBloc` → `BranchBloc`, `universes_screen.dart` → `branch_screen.dart`, UI copy "Universe" → "Branch" throughout.

---

## 10. Brain View

### 10.1 Concept

Zoomable visualization of all skills, signals, and memory. A living neural map that grows as the user adds integrations.

### 10.2 Elements

- **Central node**: ino (largest, glowing)
- **Skill nodes**: sized by usage frequency, colored by readiness (green=connected, yellow=planned, gray=vision)
- **Signal edges**: lines between nodes, pulsing when active, opacity = signal frequency
- **Glow effects**: nodes glow brighter with more activity
- **Toolbar**: "All Skills" / "Active Only" / "By Domain" filter buttons
- **Zoom controls**: +, -, Reset button (snaps to default view showing all nodes)

### 10.3 Interaction

- **Zoom in**: see signal details, individual message labels on edges
- **Zoom out**: see the full brain, domain clusters emerge
- **Reset**: snap to default zoom level showing all visible nodes
- **Tap node**: show skill detail card (last used, signal count, auth status)
- **Timeline sync**: drag the timeline scrubber → brain view rewinds to show which skills were active at that moment, edges pulse for signals that were flowing

### 10.4 Growth Visualization

- Gray nodes (VISION) light up yellow (PLANNED) when an API becomes available
- Yellow nodes light up green (CONNECTED) when the user authorizes the service
- New nodes appear with a brief expansion animation when Creator creates a skill
- The brain literally grows denser over time — observable in timeline scroll

### 10.5 Implementation

Extends existing `neural_map.dart` component. Add:
- Pinch-to-zoom with `InteractiveViewer` widget
- Reset button via `TransformationController.value = Matrix4.identity()`
- Domain clustering layout algorithm (force-directed or radial by domain)
- Timeline sync via `TimelineBloc` state changes driving node visibility

---

## 11. Self-Aware Telemetry

### 11.1 Concept

ino can visualize its own metrics, growth, and behavioral patterns. Not a dashboard — it's ino explaining itself in chat, building charts live.

### 11.2 Telemetry Queries (natural language → live chart)

| User Says | ino Builds |
|---|---|
| "What are my most used skills?" | Horizontal bar chart of skill invocation counts |
| "Show me your response time" | Sparkline of p50 latency over time |
| "What are my most used behaviors?" | Bar chart of behavior frequency (schedule check, send message, etc.) |
| "How have you grown?" | Growth timeline: skills over time, signals over time |
| "What patterns do you notice?" | Text analysis: "You always play music after calling a ride" |
| "Show me your telemetry" | Live counter dashboard: skills active, signals today, avg response |

### 11.3 Data Source

All telemetry comes from ino's own OpenTelemetry data, already flowing to the Aspire dashboard:
- `gen_ai.*` spans for LLM calls
- gRPC spans for API calls (via `grpc_interceptor.dart`)
- BLoC events (via `bloc_observer.dart`)
- Custom metrics: `ino.grpc.requests`, `ino.grpc.duration`, `ino.chat.messages`, `ino.errors`

New metrics to add:
- `ino.skills.active` — gauge of currently active skills
- `ino.skills.invocations` — counter per skill
- `ino.signals.total` — counter of all signals
- `ino.memories.count` — gauge of memory entries by decay tier
- `ino.persona.switches` — counter of persona changes

### 11.4 Chart Generation

Charts are RFW cards, same as domain cards. ino selects the right chart type based on the query:
- Bar chart for ranked lists
- Sparkline for time series
- Counter cards for current state
- Growth timeline for historical trends
- Text card for pattern analysis

### 11.5 Behavioral Pattern Detection

ino notices patterns in the user's behavior and can report them:
- Time correlations: "You check portfolio at market open (9:30) every weekday"
- Sequence patterns: "You always play music after calling a ride"
- Frequency anomalies: "You used GitHub 3x more this week — shipping something?"
- Optimization suggestions: "Your average Uber wait is 4 min — leaving 4 min earlier would save $3/ride on surge pricing"

---

## 12. Auth Architecture

### 12.1 Auth Cascade

```
Google Sign-In (primary)
  │
  ├─ Instant: Gmail, Drive, Calendar, Maps, YouTube, Photos, Meet, Home
  │
  ├─ OAuth2 Consent (one-tap per service):
  │   Spotify, Uber, GitHub, Notion, Slack, Figma, Strava, +90 more
  │
  └─ API Key (auto-managed, no user interaction):
      Weather, News, Maps, Translate
```

### 12.2 GoogleAuth Skill

Created by Creator at first sign-in. Manages:
- Google OAuth2 tokens (access + refresh)
- Token refresh on expiry
- Scope expansion when new Google services are needed
- OAuth2 consent flow delegation for third-party services that support "Sign in with Google"

### 12.3 Per-Service Auth

When ino needs a service that requires separate auth:
1. ino checks if auth exists in Memory
2. If not, shows auth card in chat: "To use Uber, I need your permission. [Authorize]"
3. User taps → OAuth2 consent flow in webview
4. Token stored as Memory (decay=100, permanent)
5. Future requests use cached token, refresh as needed

---

## 13. Stitch Generation Pipeline

### 13.1 Scale

- 25 Stitch projects (one per domain)
- ~8 card variations per project
- ~200 total screens
- One shared DESIGN.md for visual consistency

### 13.2 Design System (DESIGN.md)

```markdown
# ino Card Design System

## Color Palette
- Primary: #6C63FF (ino purple)
- Surface: #161b22 (dark card background)
- Border: #21262d (subtle separation)
- Ready: #2ecc71 (green — connected)
- Planned: #f0ad4e (yellow — API exists)
- Vision: #555555 (gray — future)
- Accent per domain (varies)

## Typography
- Card title: 11px, weight 600, #e6e6e6
- Body text: 11px, #8b949e
- Metric value: 16-22px, weight 700-800
- Tag: 9px, weight 600, colored background

## Card Structure
- Border radius: 14px (card), 8px (inner elements)
- Header: icon + title + readiness tag, bottom border
- Body: domain-specific content
- Subtle hover: border-color shift to #6C63FF, translateY(-2px)

## Persona Variants
- Jarvis: reduce border-radius to 8px, monochrome accents, data-dense
- Luna: increase border-radius to 20px, warmer colors, emoji in copy
- Cortex: 0px border-radius, green-on-black, monospace font
- Coach: bold metrics, progress bars, comparison charts
```

### 13.3 Generation Order

Priority by integration readiness (ship what works first):

1. **Productivity** (10 cards) — 100% READY
2. **Developer Tools** (8 cards) — 100% READY
3. **Messaging** (10 cards) — 80% READY
4. **Social Media** (10 cards) — 80% READY
5. **Finance** (10 cards) — 70% READY
6. **Music** (8 cards) — 63% READY
7. **Home & IoT** (7 cards) — 71% READY
8. **Shopping** (10 cards) — 60% READY
9. **Travel** (10 cards) — 50% READY
10. **Remaining 16 domains** — as API availability permits

### 13.4 Stitch Workflow Per Domain

```
1. create_project(title="ino-{domain}")
2. Apply DESIGN.md to project
3. For each card variation:
   a. enhance prompt with domain keywords + design system
   b. generate_screen_from_text(projectId, prompt, MOBILE)
   c. Review generated screen
   d. edit_screens if needed for consistency
4. Export HTML + screenshots to .stitch/designs/{domain}/
5. Translate winning patterns to RFW card templates
```

---

## 14. Website Visualization (VitePress)

The existing VitePress site (`website/`) keeps its current design. Add:

### 14.1 Interactive Brain View

Embed an interactive brain view on the "How It Works" page showing all 25 domain clusters. Same visual language as the Flutter Brain View:
- SVG-based (like existing `HowItWorksDiagram.vue`)
- Domain nodes clustered by category
- Click to expand domain → see individual app skills
- Zoom via mouse wheel / pinch
- Reset button returns to overview

### 14.2 Growth Animation

Hero section animation showing system growth:
- Starts with 2 dots (Creator + ino)
- Dots appear one by one, edges form
- Accelerates as the system grows
- Final state: dense neural network
- Loops every 10 seconds

---

## 15. Implementation Notes

### 15.1 What Already Exists

| Feature | Location | Status |
|---|---|---|
| Timeline capture | `features/timetravel/` | Shipped — grain, filter, MCP tools |
| Universe/Branch | `features/ino-new/InoNew.Core/IUniverse.cs` | Implemented — fork, replay, compare |
| Flutter app | `ino.flutter/` | 6 screens, 6 BLoCs, gRPC, OTel |
| Persona widget | `ino.flutter/lib/persona/persona_widget.dart` | CustomPaint with 12 emotions |
| Neural map | `ino.flutter/lib/ui/components/neural_map.dart` | Basic graph visualization |
| Agent registry | `iaw/Core/Registry/AgentRegistryGrain.cs` | L1 creation, hybrid search |
| VitePress site | `website/` | Live with animated diagrams |
| Stitch MCP | `.mcp.json` | Configured, not yet used |
| OTel pipeline | `lib/telemetry/` | Full traces + logs to Aspire |

### 15.2 What Needs Building

| Feature | Effort | Dependencies |
|---|---|---|
| Creator neuron (neuron #0) | Medium | AgentRegistry persistence (#7) |
| PersonaCreator skill | Medium | Rive Editor MCP Server (early access), MCP client bridge |
| Rive Editor as Aspire resource | Medium | Rive Editor MCP Server headless mode, Aspire `AddResource` |
| 5 preset .riv templates (fallback) | Medium | Rive Editor (manual or MCP-assisted) |
| 200 Stitch card designs | Large (batch) | Stitch API key, design system |
| RFW card templates from Stitch | Medium per domain | Stitch designs complete |
| Brain View zoom/reset | Low | Existing neural_map.dart |
| Timeline/Branch rename | Low | Find-replace + BLoC rename |
| Self-aware telemetry cards | Medium | New OTel metrics |
| Behavioral pattern detection | Medium | Signal history analysis |
| Auth cascade (GoogleAuth skill) | Medium | Google OAuth2 setup |
| Website brain view | Medium | Existing VitePress components |

### 15.3 Rename Checklist

User-facing only (internal code stays neurons/synapses):

- [ ] `time_travel_screen.dart` → merge into `timeline_screen.dart`
- [ ] `TimeTravelBloc` → merge into `TimelineBloc`
- [ ] `universes_screen.dart` → `branch_screen.dart`
- [ ] `UniverseBloc` → `BranchBloc`
- [ ] All UI copy: "Time Travel" → "Timeline", "Universe" → "Branch", "What-If" → "Branch"
- [ ] Bottom nav labels updated
- [ ] gRPC service method names stay unchanged (internal)
