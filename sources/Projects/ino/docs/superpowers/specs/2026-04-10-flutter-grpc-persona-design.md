# ino.flutter — Flutter + gRPC + Rive Persona Design

**Date:** 2026-04-10
**Status:** Approved

## Decisions

| Decision | Choice | Why |
|---|---|---|
| Wire protocol | gRPC + gRPC-Web | Typed proto contracts, Dart codegen via dart.dev publisher, `GrpcOrGrpcWebClientChannel` auto-selects native vs web |
| Structural UI | rfw v1.1.3 (Remote Flutter Widgets) | Google-official, neurons compose pre-registered components, safe, near-native perf |
| Animation/persona | Rive v0.14.5 + Data Binding ViewModels | Living persona, emotion-driven morphing, GPU-accelerated C++ renderer |
| UI generation model | Template-based (v1) | Domain templates (form, list, chat, dashboard), consistent, no code generation |
| First-launch | Persona-first | The persona IS ino — not a decoration. Intro → license → chat through the entity |
| Project location | `E:\ino\ino.flutter\` (repo root) | Matches ino.windows pattern, separate build toolchain |
| State management | BLoC (flutter_bloc) | VGV-recommended, gRPC streams map to BLoC events naturally |
| Navigation | GoRouter | VGV-recommended, deep linking for skill screens |
| Voice capture | `record` v6.2.0 | 6-platform including web, streaming via startStream() |
| Voice playback | `just_audio` v0.10.5 | StreamAudioSource for server TTS, cross-platform |
| Rejected: SignalR | Dart client has no Web support | signalr_netcore doesn't work in browser (Telegram mini-app) |
| Rejected: dart_eval | 10-50x perf penalty, limited widgets | rfw does composition better, Rive does visuals better |

## Architecture

```
ino.flutter (Flutter web/mobile/desktop)
├── Rive Persona (emotion states, domain affinity morphing)
├── rfw Runtime (dynamic UI from server descriptions)
├── Flutter Widgets (text input, forms, scrollable lists — overlay)
└── gRPC Client (GrpcOrGrpcWebClientChannel)
        │
        │ proto contract (ino.v1)
        ▼
iaw/Grpc/ (ASP.NET Core gRPC service)
├── Grpc.AspNetCore + Grpc.AspNetCore.Web (browser support)
├── Orleans client → cluster
├── WebSocket /ws/audio (voice streaming fallback for web)
└── Maps gRPC calls to InoCommandDispatcher + neuron grain calls
        │
        ▼
Orleans Silo (Agents.Host)
├── AgentRegistryGrain (persistent, vector+keyword search)
├── NeuronGrain (L1 universal — per-key: prompt+tools+script)
├── SynapseStoreGrain (decay-tagged memory per receiver)
└── Domain Modules (Travel: 13 neurons with API integrations)
```

## Proto contract

```protobuf
syntax = "proto3";
package ino.v1;

service Ino {
  // Core
  rpc Chat(ChatRequest) returns (ChatResponse);
  rpc StreamEvents(EventSubscription) returns (stream InoEvent);

  // Synapses
  rpc FireSynapse(FireRequest) returns (FireResponse);
  rpc GetTimeline(TimelineQuery) returns (stream TimelineEvent);

  // Skills / Domain modules
  rpc ListSkills(ListSkillsRequest) returns (ListSkillsResponse);
  rpc InstallSkill(InstallSkillRequest) returns (InstallSkillResponse);
  rpc GetSkillUI(SkillUIRequest) returns (SkillUIResponse);

  // Persona state
  rpc StreamPersonaState(PersonaSubscription) returns (stream PersonaState);

  // Voice
  rpc StreamAudio(stream AudioChunk) returns (TranscriptResponse);
  rpc StreamConversation(stream AudioChunk) returns (stream AudioChunk);
}

message PersonaState {
  string emotion = 1;
  float energy = 2;
  float confidence = 3;
  map<string, float> domain_affinity = 4;
}

message InoEvent {
  string type = 1;
  string source_neuron = 2;
  bytes payload = 3;
  int64 timestamp = 4;
}

message ChatRequest {
  string message = 1;
  string user_id = 2;
}

message ChatResponse {
  string reply = 1;
  string neuron_id = 2;
}

message EventSubscription {
  string user_id = 1;
  repeated string event_types = 2;
}

message FireRequest {
  string verb = 1;
  map<string, string> args = 2;
  string target_neuron = 3;
}

message FireResponse {
  bool ok = 1;
  string synapse_id = 2;
}

message TimelineQuery {
  int32 limit = 1;
  int32 min_decay = 2;
}

message TimelineEvent {
  int64 sequence = 1;
  string kind = 2;
  string source = 3;
  string target = 4;
  int64 timestamp = 5;
  int32 decay = 6;
}

message ListSkillsRequest {
  string domain = 1;
  string query = 2;
}

message ListSkillsResponse {
  repeated SkillInfo skills = 1;
}

message SkillInfo {
  string id = 1;
  string name = 2;
  string domain = 3;
  string description = 4;
  bool installed = 5;
}

message InstallSkillRequest {
  string skill_id = 1;
}

message InstallSkillResponse {
  bool ok = 1;
  string neuron_id = 2;
}

message SkillUIRequest {
  string skill_id = 1;
}

message SkillUIResponse {
  bytes rfw_description = 1;
  bytes rfw_data = 2;
}

message PersonaSubscription {
  string user_id = 1;
}

message TranscriptResponse {
  string text = 1;
  float confidence = 2;
}

message AudioChunk {
  bytes data = 1;
  int32 sample_rate = 2;
  int32 channels = 3;
}
```

## Flutter project structure

```
ino.flutter/
├── pubspec.yaml
├── protos/                         # .proto files (shared with iaw/Grpc/)
├── lib/
│   ├── main.dart
│   ├── app.dart                    # MaterialApp, theme, GoRouter
│   ├── grpc/
│   │   ├── generated/              # protoc output
│   │   └── ino_client.dart         # GrpcOrGrpcWebClientChannel wrapper
│   ├── persona/
│   │   ├── persona_widget.dart     # RiveWidget + RiveWidgetController
│   │   └── persona_state.dart      # emotion/energy/confidence model
│   ├── ui/
│   │   ├── ino_runtime.dart        # rfw Runtime with registered components
│   │   ├── components/             # LocalWidgetLibrary registrations
│   │   │   ├── neuron_card.dart
│   │   │   ├── chat_bubble.dart
│   │   │   ├── flight_card.dart
│   │   │   ├── hotel_card.dart
│   │   │   └── timeline_entry.dart
│   │   └── templates/              # .rfwtxt template descriptions
│   │       ├── chat.rfwtxt
│   │       ├── search_form.rfwtxt
│   │       ├── results_list.rfwtxt
│   │       └── dashboard.rfwtxt
│   ├── screens/
│   │   ├── onboarding/             # persona intro → license → chat
│   │   ├── home/                   # persona + chat (primary)
│   │   ├── skills/                 # skill browser (phase 2)
│   │   └── settings/               # memory tier, subscriptions
│   ├── state/
│   │   ├── ino_bloc.dart           # gRPC stream → UI state
│   │   ├── persona_bloc.dart       # PersonaState → Rive ViewModel
│   │   └── skill_bloc.dart         # installed skills, rfw descriptions
│   └── voice/
│       ├── audio_transport.dart    # abstract: gRPC (native) vs WebSocket (web)
│       ├── grpc_audio.dart
│       └── websocket_audio.dart
├── assets/rive/
│   ├── ino_persona.riv
│   └── travel/
├── test/
├── web/                            # Flutter web (Telegram mini-app)
└── analysis_options.yaml
```

## Aspire hosting

```csharp
// In iaw/Aspire/AppHost.cs:

// gRPC service — Orleans client, serves proto contract
builder.AddProject<Projects.Grpc>("grpc")
    .WithReference(iaw.AsClient())
    .WithHttpEndpoint(port: 5400, name: "grpc-direct", isProxied: false)
    .WaitFor(assistant);

// Flutter web dev server
builder.AddExecutable("ino-flutter", "flutter", "../../ino.flutter",
        "run", "-d", "chrome", "--web-port", "8080", "--web-hostname", "0.0.0.0")
    .WithHttpEndpoint(port: 8080, name: "http")
    .WithExplicitStart();
```

## gRPC service project

```
iaw/Grpc/
├── Grpc.csproj                     # Grpc.AspNetCore + Grpc.AspNetCore.Web
├── Program.cs                      # AddGrpc, UseGrpcWeb, MapGrpcService, WebSocket /ws/audio
├── Protos/
│   └── ino.proto
├── Services/
│   └── InoService.cs               # gRPC impl → Orleans client → neuron calls
└── Mapping/
    └── EventMapper.cs              # Orleans events → proto messages
```

## Persona states (Rive state machine)

| State | Trigger | Rive ViewModel |
|---|---|---|
| Sleeping | App cold start | `energy: 0.1` |
| Waking | User opens app | `energy: 0.3, emotion: "greeting"` |
| Idle | Waiting | `energy: 0.5, emotion: "idle"` |
| Listening | User typing or speaking | `energy: 0.6, emotion: "listening"` |
| Thinking | Neuron chain firing | `energy: 0.8, emotion: "thinking"` |
| Acting | Tool executing | `energy: 0.9, emotion: "acting"` |
| Responding | Response ready | `energy: 0.7, emotion: "responding"` |
| Celebrating | Task success | `energy: 1.0, emotion: "celebrating"` |
| Confused | Error / low confidence | `energy: 0.4, emotion: "confused"` |
| Evolving | Skill installed / synapse strengthened | `energy: 0.9, emotion: "evolving"` |

Domain affinity morphing: as synapse strengths develop per domain, `PersonaState.domain_affinity` feeds Rive ViewModel properties that alter the persona's visual characteristics.

## First-launch flow

1. Black screen → persona fades in (Sleeping → Waking)
2. Persona greeting: "I'm ino — your personal intelligence."
3. License agreement (scroll-to-accept)
4. "What should I call you?" — persona listens, user types name, persona celebrates
5. Domain cards appear around persona (Travel available, others coming soon)
6. User taps Travel → persona evolves, travel neurons register, templates load
7. Home screen: persona + chat + rfw skill surface

## Voice tiers

| Tier | Direction | Technology | Latency | Phase |
|---|---|---|---|---|
| V1 | User speaks → text | `record` v6.2.0 → PCM16 16kHz → Whisper (existing) | ~2-3s | 2 |
| V2 | ino speaks back | Azure TTS → audio stream → `just_audio` | ~1s | 3 |
| V3 | Real-time conversation | Azure Voice Live API (STT+LLM+TTS bundled) | <1s | 3 |

Voice transport: gRPC client streaming on native (mobile/desktop), WebSocket `/ws/audio` on web (gRPC-Web doesn't support client streaming). Abstract behind `AudioTransport` interface.

## Dependencies

```yaml
# pubspec.yaml
dependencies:
  flutter: { sdk: flutter }
  grpc: ^5.1.0
  protobuf: ^6.0.0
  rive: ^0.14.5
  rfw: ^1.1.3
  flutter_bloc: ^9.0.0
  go_router: ^15.0.0
  record: ^6.2.0
  just_audio: ^0.10.5
  web_socket_channel: ^3.0.0
```

```xml
<!-- iaw/Grpc/Grpc.csproj -->
<PackageReference Include="Grpc.AspNetCore" />
<PackageReference Include="Grpc.AspNetCore.Web" />
<PackageReference Include="Google.Protobuf" />
<PackageReference Include="Grpc.Tools" />
```

## Phases

### Phase 1: Foundation (current)

Flutter project scaffold, proto contract (Chat, StreamEvents, StreamPersonaState), gRPC service with gRPC-Web, Aspire hosting, Rive persona (placeholder morphing shapes, 3-4 emotions), persona-first onboarding, chat screen with rfw template, BLoC state management.

### Phase 2: Travel Domain + Voice

AgentRegistryGrain persistence, AgentRecord extended (Domain, UISchema, ScriptSource, Origin), ITokenBudget grain (IAW #53), travel neurons (FlightSearch, HotelSearch, PlaceDiscovery), rfw domain templates (flight card, hotel card, search form), skill install flow, voice V1 (speech-to-text).

### Phase 3: Deep Experience

Vision neurons (Currency, Budget with receipt scanning), proactive neurons (Weather, PriceWatch, Recommendations), voice V2/V3 (TTS + voice-to-voice), memory tier enforcement (decay floors per subscription), nightly consolidation pass, domain affinity morphing, persona evolution persistence.
