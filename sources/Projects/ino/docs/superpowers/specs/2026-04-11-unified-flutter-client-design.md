# Unified Flutter Client Migration

**Date:** 2026-04-11
**Status:** Approved
**Approach:** B (outside-in — wiring first, then features with real data)

## Problem

ino has 5 client surfaces (Telegram mini app, browser, ino-windows console, Flutter app, DevUI) but they're fragmented. Telegram and browser show an old hex1b TUI via xterm.js. The Windows client is a standalone hex1b console. The Flutter app exists with real screens (onboarding, chat, skills) but nobody uses it. DevUI is a Blazor tool being removed. The core ino features — time travel, parallel universes, synapse firing — have no visual representation.

## Decisions

1. **Single Flutter codebase** (`ino.flutter/`) for all surfaces — web and native Windows desktop
2. **Telegram mini app** serves Flutter web build as static files from ASP.NET `wwwroot/`
3. **ino-windows** replaced by Flutter Windows desktop (`flutter run -d windows`), later MSIX-packaged
4. **DevUI** removed (done)
5. **gRPC service merged into Telegram** — single origin, no CORS, one ngrok tunnel
6. **Real data from day one** — all gRPC stubs replaced with real Orleans grain calls before Flutter visualization screens ship

## Architecture After Migration

### Single Service Topology

```
Flutter Web (browser / Telegram WebApp)
  └─ same-origin gRPC-Web ─┐
                            ▼
                   Telegram Service (ASP.NET)
                   ├── Static files (Flutter web build)
                   ├── gRPC-Web → InoService
                   ├── POST /webhook (Telegram bot)
                   ├── POST /ino (inline commands)
                   ├── WebSocket /ws/audio (web audio fallback)
                   └── ngrok tunnel
                            ▲
Flutter Windows (desktop)   │
  └─ gRPC (native) ────────┘

Orleans Silo (assistant) ← InoService calls grains directly via IClusterClient
```

### Aspire Resources After Migration

| Resource | Type | Notes |
|----------|------|-------|
| `assistant` | Orleans silo | Unchanged |
| `telegram` | ASP.NET — bot + gRPC + Flutter web + audio | Absorbs gRPC project |
| `mcp` | MCP tools server | Unchanged |
| `ino-flutter-win` | `flutter run -d windows` | Replaces `ino-windows`, `WithExplicitStart` |
| `website` | VitePress docs | Unchanged |
| `ngrok` | Tunnel to telegram | Unchanged |

**Removed resources:** `devui`, `grpc`, `ino-windows`, `ino-flutter` (web dev server)

### Endpoint Layout (Telegram Service)

| Path | Handler |
|------|---------|
| `/` | Flutter web app (static files) |
| `/webhook` | Telegram bot updates |
| `/ino` | POST command dispatcher |
| gRPC-Web | `InoService` (all RPCs) |
| `/ws/audio` | WebSocket audio fallback |

## gRPC Service — Proto Definition

### Existing RPCs (implementations upgraded)

| RPC | Current State | Target State |
|-----|--------------|--------------|
| `Chat` | Working via dispatcher | Keep as-is |
| `StreamEvents` | Heartbeat stub | Live-tail via `ITimelineCaptureGrain.SubscribeAsync(observer)` |
| `StreamPersonaState` | Static idle | Derive from recent event activity |
| `FireSynapse` | Returns `{Ok: false}` | Real synapse via `INeuron.FireAsync()` → `SynapseReceipt` |
| `GetTimeline` | Text dump | `ITimelineReader.GetEventsInRangeAsync()` with real events |
| `ListSkills` | Working via registry | Keep as-is |
| `InstallSkill` | Working | Keep as-is |
| `GetSkillUI` | Returns basic RFW | Keep as-is |
| `TranscribeAudio` | Working via Whisper | Keep as-is |

### New RPCs (parallel universes + time-travel)

```protobuf
rpc ForkUniverse(ForkRequest) returns (ForkResponse);
rpc ReplayUniverse(ReplayRequest) returns (ReplayResponse);
rpc CompareUniverses(CompareRequest) returns (CompareResponse);
rpc GetUniverseTimeline(UniverseTimelineQuery) returns (stream TimelineEvent);
rpc GetUniverseInfo(UniverseInfoRequest) returns (UniverseInfoResponse);
rpc GetStateAt(StateAtRequest) returns (StateAtResponse);
```

All map 1:1 to existing Orleans grain methods:
- `IUniverse.ForkAsync`, `ReplayAsync`, `CompareAsync`, `GetTimelineAsync`, `GetInfoAsync`
- `ITimelineReader.GetStateAtAsync(sequence)`

## Flutter App — Screen Architecture

### Navigation

Bottom nav bar with 5 tabs: **Chat | Timeline | Time Travel | Universes | Skills**

GoRouter routes:
- `/onboarding` — first-launch only
- `/home` — chat with persona widget
- `/timeline` — live event stream
- `/timetravel` — scrubber + state snapshot
- `/universes` — fork/compare view
- `/skills` — skill browser

### New Screen 1: Timeline (`/timeline`)

Vertical event stream showing ino activity in real time.

Each event card shows:
- Event kind icon (neuron activation, synapse fire, tool invocation, LLM call, self-improvement L1/L2/L3)
- Source and target neuron names
- Timestamp + decay badge (hot=red, warm=orange, cold=blue)
- Expandable payload detail

Top controls: decay filter slider (30-100), kind filter chips, pause/resume live-tail.

**BLoC: `TimelineBloc`** — subscribes via `StreamEvents` for live data, loads history via `GetTimeline` on init.

### New Screen 2: Time Travel (`/timetravel`)

Horizontal scrubber bar at top (video-timeline style) showing full event sequence range. Drag to any point → below shows system state at that moment:
- Which neurons were active
- Which synapses had fired recently
- Mini neural map showing connections at that instant

**BLoC: `TimeTravelBloc`** — holds scrub position, fetches `StateAtResponse` on scrub, caches nearby positions for smooth scrubbing.

### New Screen 3: Parallel Universes (`/universes`)

Split-view comparison. Left = source timeline, right = forked universe.
- Shared events (dimmed, both sides)
- Divergence point (highlighted marker)
- Exclusive events per side (color-coded)

Actions: Fork (pick checkpoint), Replay, Compare (pick two universes).

**BLoC: `UniverseBloc`** — manages universe list, fork/replay/compare via gRPC.

### Modified: Chat Screen

Persona widget now reacts to real `StreamPersonaState` data — emotion derived from recent event patterns (thinking during LLM calls, acting during tool use, idle when quiet).

## Deletions

| Item | Action |
|------|--------|
| `iaw/DevUI/` | Delete (already emptied) |
| `iaw/Grpc/` | Delete (merged into Telegram) |
| `ino.windows/` | Delete (replaced by Flutter Windows) |
| `Grpc.csproj` in `ino.slnx` | Remove |
| `Ino.Windows.csproj` in `ino.slnx` | Remove |
| `grpc` resource in `AppHost.cs` | Remove |
| `ino-windows` resource in `AppHost.cs` | Replace with `ino-flutter-win` |
| `ino-flutter` executable resource in `AppHost.cs` | Remove (web is pre-built static) |
| `DevUI.csproj` in `Aspire.csproj` | Remove (already done) |
| `Grpc.csproj` in `Aspire.csproj` | Remove |
| `Ino.Windows.csproj` in `Aspire.csproj` | Remove |
| `iaw/Telegram/wwwroot/index.html` | Overwritten by Flutter web build |

## Flutter Config Injection

- **Flutter web:** detects own origin via `Uri.base`, connects gRPC-Web to same origin. Zero config.
- **Flutter Windows:** receives gRPC endpoint via command-line argument or environment variable from Aspire service discovery.

## Build Workflow

1. `flutter build web --release` in `ino.flutter/` → output in `build/web/`
2. Copy `build/web/*` → `iaw/Telegram/wwwroot/`
3. `dotnet build ino.slnx`
4. `aspire start`
5. Flutter web available at Telegram service URL (and via ngrok in Telegram)
6. Flutter Windows: start `ino-flutter-win` from Aspire dashboard
