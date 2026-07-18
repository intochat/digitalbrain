# ino.flutter Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Flutter app with Rive persona talking to ino via gRPC. Chat works end-to-end through the persona-first experience.

**Architecture:** Flutter client (`ino.flutter/`) connects to a new gRPC service (`iaw/Grpc/`) via proto contract. The gRPC service is an Orleans client that calls `InoCommandDispatcher` on the cluster. The Flutter app uses BLoC for state, rfw for dynamic UI composition, and Rive for the animated persona. Aspire hosts both the gRPC service and the Flutter dev server.

**Tech Stack:** Flutter 3.41.6, Dart 3.11.4, gRPC (`grpc` ^5.1.0, `Grpc.AspNetCore`), rfw 1.1.3, Rive 0.14.5, flutter_bloc, GoRouter, .NET 11, Orleans 10, Aspire 13.2

**Spec:** `docs/superpowers/specs/2026-04-10-flutter-grpc-persona-design.md`

---

## File Map

### C# — New project: `iaw/Grpc/`

| File | Responsibility |
|---|---|
| `iaw/Grpc/Grpc.csproj` | Project file — Grpc.AspNetCore, Grpc.AspNetCore.Web, Orleans client |
| `iaw/Grpc/Program.cs` | ASP.NET Core host — AddGrpc, UseGrpcWeb, MapGrpcService, health checks |
| `iaw/Grpc/Protos/ino.proto` | Canonical proto contract — shared source of truth |
| `iaw/Grpc/Services/InoService.cs` | gRPC service impl — Chat, StreamEvents, StreamPersonaState |

### C# — Modified files

| File | Change |
|---|---|
| `iaw/Aspire/Aspire.csproj` | Add ProjectReference to Grpc.csproj |
| `iaw/Aspire/AppHost.cs` | Add gRPC resource + Flutter executable resource |
| `ino.slnx` | Add Grpc project |
| `Directory.Packages.props` | Add Grpc.AspNetCore, Grpc.AspNetCore.Web, Google.Protobuf, Grpc.Tools versions |

### Flutter — New project: `ino.flutter/`

| File | Responsibility |
|---|---|
| `ino.flutter/pubspec.yaml` | Dependencies: grpc, protobuf, rive, rfw, flutter_bloc, go_router |
| `ino.flutter/analysis_options.yaml` | Linting rules |
| `ino.flutter/lib/main.dart` | Entry point — bootstrap BLoCs, gRPC channel |
| `ino.flutter/lib/app.dart` | MaterialApp.router, theme, GoRouter config |
| `ino.flutter/lib/grpc/ino_client.dart` | GrpcOrGrpcWebClientChannel wrapper, service stub factory |
| `ino.flutter/lib/grpc/generated/` | protoc Dart output (ino.pb.dart, ino.pbgrpc.dart, etc.) |
| `ino.flutter/lib/persona/persona_widget.dart` | RiveWidget + RiveWidgetController, ViewModel bindings |
| `ino.flutter/lib/persona/persona_state.dart` | PersonaEmotion enum, PersonaStateModel |
| `ino.flutter/lib/state/persona_bloc.dart` | PersonaBloc — gRPC PersonaState stream → Rive ViewModel |
| `ino.flutter/lib/state/ino_bloc.dart` | InoBloc — chat messages, gRPC calls, event stream |
| `ino.flutter/lib/ui/ino_runtime.dart` | rfw Runtime setup, LocalWidgetLibrary registrations |
| `ino.flutter/lib/ui/components/chat_bubble.dart` | ChatBubble rfw component (user + ino messages) |
| `ino.flutter/lib/screens/onboarding/onboarding_screen.dart` | Persona intro → license → name → domain pick |
| `ino.flutter/lib/screens/home/home_screen.dart` | Persona + chat input + rfw skill surface |
| `ino.flutter/assets/rive/ino_persona.riv` | Placeholder Rive artboard (morphing shape, emotion states) |
| `ino.flutter/web/index.html` | Flutter web entry for Telegram mini-app |
| `ino.flutter/test/state/ino_bloc_test.dart` | InoBloc unit tests |
| `ino.flutter/test/state/persona_bloc_test.dart` | PersonaBloc unit tests |
| `ino.flutter/test/grpc/ino_client_test.dart` | gRPC client wrapper tests |

---

## Task 1: Install protoc tooling

**Files:** None (system tooling)

- [ ] **Step 1: Install protoc via winget or choco**

```bash
# Option A: Download from GitHub releases (recommended on Windows)
# Download protoc-29.5-win64.zip from https://github.com/protocolbuffers/protobuf/releases
# Extract to a directory on PATH, e.g. C:\tools\protoc\
# Verify:
protoc --version
# Expected: libprotoc 29.5 (or similar)
```

If protoc is not installable via package manager, ask the user to install it manually.

- [ ] **Step 2: Install Dart protoc plugin**

```bash
dart pub global activate protoc_plugin
```

Expected: `Activated protoc_plugin X.X.X.`

- [ ] **Step 3: Verify both tools work**

```bash
protoc --version && dart pub global list | grep protoc
```

Expected: protoc version + `protoc_plugin` in global list.

---

## Task 2: Proto contract

**Files:**
- Create: `iaw/Grpc/Protos/ino.proto`

- [ ] **Step 1: Create the proto directory**

```bash
mkdir -p E:/ino/iaw/Grpc/Protos
```

- [ ] **Step 2: Write the proto contract**

Create `iaw/Grpc/Protos/ino.proto`:

```protobuf
syntax = "proto3";

option csharp_namespace = "Ino.Grpc";

package ino.v1;

service Ino {
  rpc Chat(ChatRequest) returns (ChatResponse);
  rpc StreamEvents(EventSubscription) returns (stream InoEvent);
  rpc StreamPersonaState(PersonaSubscription) returns (stream PersonaState);
  rpc FireSynapse(FireRequest) returns (FireResponse);
  rpc GetTimeline(TimelineQuery) returns (stream TimelineEvent);
  rpc ListSkills(ListSkillsRequest) returns (ListSkillsResponse);
  rpc InstallSkill(InstallSkillRequest) returns (InstallSkillResponse);
  rpc GetSkillUI(SkillUIRequest) returns (SkillUIResponse);
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

message InoEvent {
  string type = 1;
  string source_neuron = 2;
  bytes payload = 3;
  int64 timestamp = 4;
}

message PersonaSubscription {
  string user_id = 1;
}

message PersonaState {
  string emotion = 1;
  float energy = 2;
  float confidence = 3;
  map<string, float> domain_affinity = 4;
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
```

- [ ] **Step 3: Commit**

```bash
git add iaw/Grpc/Protos/ino.proto
git commit -m "feat(grpc): add ino.v1 proto contract with Chat, StreamEvents, PersonaState"
```

---

## Task 3: gRPC C# service project

**Files:**
- Create: `iaw/Grpc/Grpc.csproj`
- Create: `iaw/Grpc/Program.cs`
- Create: `iaw/Grpc/Services/InoService.cs`
- Modify: `Directory.Packages.props`
- Modify: `ino.slnx`
- Modify: `iaw/Aspire/Aspire.csproj`
- Modify: `iaw/Aspire/AppHost.cs`

- [ ] **Step 1: Add gRPC package versions to Directory.Packages.props**

Add these lines to the `<ItemGroup>` in `Directory.Packages.props`:

```xml
<PackageVersion Include="Grpc.AspNetCore" Version="2.71.0" />
<PackageVersion Include="Grpc.AspNetCore.Web" Version="2.71.0" />
<PackageVersion Include="Google.Protobuf" Version="3.30.2" />
<PackageVersion Include="Grpc.Tools" Version="2.71.0" />
```

**IMPORTANT:** Before writing these versions, use Context7 to verify the latest stable versions of these packages. The versions above are estimates — use whatever Context7 reports as current.

- [ ] **Step 2: Create Grpc.csproj**

Create `iaw/Grpc/Grpc.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Grpc.AspNetCore" />
    <PackageReference Include="Grpc.AspNetCore.Web" />
  </ItemGroup>

  <ItemGroup>
    <Protobuf Include="Protos/ino.proto" GrpcServices="Server" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Aspire.Client\Aspire.Client.csproj" />
    <ProjectReference Include="..\..\features\ino-new\InoNew.Core\InoNew.Core.csproj" />
  </ItemGroup>

</Project>
```

The `Protobuf` item tells `Grpc.Tools` to auto-generate C# server stubs from `ino.proto` at build time.

- [ ] **Step 3: Create Program.cs**

Create `iaw/Grpc/Program.cs`:

```csharp
using Aspire.IAW;
using Ino.Grpc.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddIAWClient();
builder.Services.AddGrpc();

var app = builder.Build();

app.UseGrpcWeb();
app.MapGrpcService<InoService>().EnableGrpcWeb();
app.MapDefaultEndpoints();

app.Run();
```

- [ ] **Step 4: Create InoService.cs**

Create `iaw/Grpc/Services/InoService.cs`:

```csharp
using Grpc.Core;
using Ino.Grpc;
using InoNew.Core;

namespace Ino.Grpc.Services;

public class InoService(IClusterClient clusterClient) : Ino.Grpc.Ino.InoBase
{
    readonly InoCommandDispatcher _dispatcher = new(clusterClient);

    public override async Task<ChatResponse> Chat(ChatRequest request, ServerCallContext context)
    {
        var reply = await _dispatcher.ExecuteScriptToStringAsync(
            $"chat {request.Message}", context.CancellationToken);

        return new ChatResponse
        {
            Reply = reply.Trim(),
            NeuronId = "cortex"
        };
    }

    public override async Task StreamEvents(
        EventSubscription request,
        IServerStreamWriter<InoEvent> responseStream,
        ServerCallContext context)
    {
        // Phase 1: send a heartbeat every 5 seconds to keep the stream alive.
        // Phase 2: wire to Orleans event streams via AgentEventForwarder pattern.
        while (!context.CancellationToken.IsCancellationRequested)
        {
            await Task.Delay(5000, context.CancellationToken);
            await responseStream.WriteAsync(new InoEvent
            {
                Type = "heartbeat",
                SourceNeuron = "system",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, context.CancellationToken);
        }
    }

    public override async Task StreamPersonaState(
        PersonaSubscription request,
        IServerStreamWriter<PersonaState> responseStream,
        ServerCallContext context)
    {
        // Phase 1: emit idle state, then respond to chat activity.
        // Phase 2: wire to actual neuron firing events.
        await responseStream.WriteAsync(new PersonaState
        {
            Emotion = "idle",
            Energy = 0.5f,
            Confidence = 1.0f
        }, context.CancellationToken);

        while (!context.CancellationToken.IsCancellationRequested)
        {
            await Task.Delay(10000, context.CancellationToken);
        }
    }

    public override Task<FireResponse> FireSynapse(FireRequest request, ServerCallContext context)
        => Task.FromResult(new FireResponse { Ok = false, SynapseId = "" });

    public override async Task GetTimeline(
        TimelineQuery request,
        IServerStreamWriter<TimelineEvent> responseStream,
        ServerCallContext context)
    {
        var result = await _dispatcher.ExecuteScriptToStringAsync("timeline", context.CancellationToken);
        await responseStream.WriteAsync(new TimelineEvent
        {
            Sequence = 0,
            Kind = "timeline_dump",
            Source = "system",
            Target = "",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }, context.CancellationToken);
    }

    public override Task<ListSkillsResponse> ListSkills(ListSkillsRequest request, ServerCallContext context)
        => Task.FromResult(new ListSkillsResponse());

    public override Task<InstallSkillResponse> InstallSkill(InstallSkillRequest request, ServerCallContext context)
        => Task.FromResult(new InstallSkillResponse { Ok = false });

    public override Task<SkillUIResponse> GetSkillUI(SkillUIRequest request, ServerCallContext context)
        => Task.FromResult(new SkillUIResponse());
}
```

- [ ] **Step 5: Add Grpc project to solution**

Add to `ino.slnx` after the MCP project line:

```xml
<Project Path="iaw/Grpc/Grpc.csproj" />
```

- [ ] **Step 6: Add Grpc project reference to Aspire.csproj**

Add to the `<ItemGroup>` with ProjectReferences in `iaw/Aspire/Aspire.csproj`:

```xml
<ProjectReference Include="..\Grpc\Grpc.csproj" />
```

- [ ] **Step 7: Wire gRPC + Flutter into AppHost.cs**

Add these lines before `builder.Build().Run();` in `iaw/Aspire/AppHost.cs`:

```csharp
builder.AddProject<Projects.Grpc>("grpc")
    .WithReference(iaw.AsClient())
    .WithHttpEndpoint(port: 5400, name: "grpc-direct", isProxied: false)
    .WaitFor(assistant);

builder.AddExecutable("ino-flutter", "flutter", "../../ino.flutter",
        "run", "-d", "chrome", "--web-port", "8080", "--web-hostname", "0.0.0.0")
    .WithHttpEndpoint(port: 8080, name: "http")
    .WithExplicitStart();
```

- [ ] **Step 8: Build and verify**

```bash
cd E:/ino && dotnet build ino.slnx
```

Expected: Build succeeded. 0 errors. The Grpc project should compile, auto-generating C# stubs from `ino.proto`.

- [ ] **Step 9: Commit**

```bash
git add iaw/Grpc/ ino.slnx Directory.Packages.props iaw/Aspire/Aspire.csproj iaw/Aspire/AppHost.cs
git commit -m "feat(grpc): add gRPC service project with Aspire hosting and proto codegen"
```

---

## Task 4: Flutter project scaffold

**Files:**
- Create: `ino.flutter/` (entire Flutter project via `flutter create`)
- Modify: `ino.flutter/pubspec.yaml`
- Modify: `ino.flutter/analysis_options.yaml`
- Create: `ino.flutter/lib/main.dart` (overwrite generated)
- Create: `ino.flutter/lib/app.dart`

- [ ] **Step 1: Create Flutter project**

```bash
cd E:/ino && flutter create ino.flutter --org com.ino --project-name ino_flutter --platforms web,windows,android,ios
```

Expected: `All done!` message. Creates `ino.flutter/` with default Flutter scaffold.

- [ ] **Step 2: Replace pubspec.yaml dependencies**

Replace the `dependencies` and `dev_dependencies` sections in `ino.flutter/pubspec.yaml`:

```yaml
name: ino_flutter
description: ino — personal intelligence
publish_to: 'none'
version: 0.1.0

environment:
  sdk: ^3.11.0

dependencies:
  flutter:
    sdk: flutter
  grpc: ^5.1.0
  protobuf: ^6.0.0
  rive: ^0.14.5
  rfw: ^1.1.3
  flutter_bloc: ^9.0.0
  go_router: ^15.0.0
  web_socket_channel: ^3.0.0

dev_dependencies:
  flutter_test:
    sdk: flutter
  flutter_lints: ^6.0.0
  bloc_test: ^10.0.0
  mocktail: ^1.0.4

flutter:
  uses-material-design: true
  assets:
    - assets/rive/
```

**IMPORTANT:** Use Context7 to verify latest versions of `grpc`, `rive`, `rfw`, `flutter_bloc`, `go_router` before writing. The versions above are estimates.

- [ ] **Step 3: Run pub get**

```bash
cd E:/ino/ino.flutter && flutter pub get
```

Expected: `Got dependencies!`

- [ ] **Step 4: Create assets directory**

```bash
mkdir -p E:/ino/ino.flutter/assets/rive
```

- [ ] **Step 5: Write main.dart**

Overwrite `ino.flutter/lib/main.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/app.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/state/ino_bloc.dart';
import 'package:ino_flutter/state/persona_bloc.dart';

void main() {
  final client = InoClient(host: 'localhost', port: 5400);

  runApp(
    MultiBlocProvider(
      providers: [
        BlocProvider(create: (_) => InoBloc(client: client)),
        BlocProvider(create: (_) => PersonaBloc(client: client)),
      ],
      child: const InoApp(),
    ),
  );
}
```

- [ ] **Step 6: Write app.dart**

Create `ino.flutter/lib/app.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:ino_flutter/screens/home/home_screen.dart';
import 'package:ino_flutter/screens/onboarding/onboarding_screen.dart';

final _router = GoRouter(
  initialLocation: '/onboarding',
  routes: [
    GoRoute(
      path: '/onboarding',
      builder: (context, state) => const OnboardingScreen(),
    ),
    GoRoute(
      path: '/home',
      builder: (context, state) => const HomeScreen(),
    ),
  ],
);

class InoApp extends StatelessWidget {
  const InoApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp.router(
      title: 'ino',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF6C63FF),
          brightness: Brightness.dark,
        ),
        useMaterial3: true,
      ),
      routerConfig: _router,
    );
  }
}
```

- [ ] **Step 7: Commit**

```bash
cd E:/ino && git add ino.flutter/
git commit -m "feat(flutter): scaffold ino.flutter project with dependencies and app shell"
```

---

## Task 5: gRPC Dart client

**Files:**
- Create: `ino.flutter/lib/grpc/generated/` (protoc output)
- Create: `ino.flutter/lib/grpc/ino_client.dart`
- Create: `ino.flutter/test/grpc/ino_client_test.dart`

- [ ] **Step 1: Copy proto file to Flutter project**

```bash
mkdir -p E:/ino/ino.flutter/protos
cp E:/ino/iaw/Grpc/Protos/ino.proto E:/ino/ino.flutter/protos/
```

- [ ] **Step 2: Generate Dart stubs**

```bash
mkdir -p E:/ino/ino.flutter/lib/grpc/generated
cd E:/ino/ino.flutter && protoc --dart_out=grpc:lib/grpc/generated -Iprotos protos/ino.proto
```

Expected: generates `ino.pb.dart`, `ino.pbenum.dart`, `ino.pbgrpc.dart`, `ino.pbjson.dart` in `lib/grpc/generated/`.

- [ ] **Step 3: Write InoClient wrapper**

Create `ino.flutter/lib/grpc/ino_client.dart`:

```dart
import 'package:grpc/grpc_or_grpcweb.dart';
import 'package:ino_flutter/grpc/generated/ino.pbgrpc.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart';

export 'package:ino_flutter/grpc/generated/ino.pb.dart';

class InoClient {
  InoClient({required String host, required int port})
      : _channel = GrpcOrGrpcWebClientChannel.toSingleEndpoint(
          host: host,
          port: port,
          transportSecure: false,
        ),
        _stub = InoClient._createStub(host, port);

  final GrpcOrGrpcWebClientChannel _channel;
  final InoClient_ _stub;

  static InoClient_ _createStub(String host, int port) {
    final channel = GrpcOrGrpcWebClientChannel.toSingleEndpoint(
      host: host,
      port: port,
      transportSecure: false,
    );
    return InoClient_(channel);
  }

  Future<ChatResponse> chat(String message, {String userId = 'default'}) {
    return _stub.chat(ChatRequest()
      ..message = message
      ..userId = userId);
  }

  Stream<InoEvent> streamEvents({String userId = 'default'}) {
    return _stub.streamEvents(EventSubscription()..userId = userId);
  }

  Stream<PersonaState> streamPersonaState({String userId = 'default'}) {
    return _stub.streamPersonaState(PersonaSubscription()..userId = userId);
  }

  Future<void> shutdown() => _channel.shutdown();
}
```

**NOTE:** The generated client stub class name depends on the proto service name. After running protoc, check the actual generated class name in `ino.pbgrpc.dart` — it may be `InoClient` (conflicting with our wrapper) or `InoServiceClient`. Adjust the wrapper accordingly. If there's a conflict, rename the wrapper class to `InoGrpcClient`.

- [ ] **Step 4: Write test**

Create `ino.flutter/test/grpc/ino_client_test.dart`:

```dart
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/grpc/ino_client.dart';

void main() {
  group('InoClient', () {
    test('creates without error', () {
      // Verifies the client can be constructed (does not connect yet)
      final client = InoClient(host: 'localhost', port: 5400);
      expect(client, isNotNull);
    });
  });
}
```

- [ ] **Step 5: Run test**

```bash
cd E:/ino/ino.flutter && flutter test test/grpc/ino_client_test.dart
```

Expected: 1 test passed.

- [ ] **Step 6: Commit**

```bash
cd E:/ino && git add ino.flutter/protos/ ino.flutter/lib/grpc/ ino.flutter/test/grpc/
git commit -m "feat(flutter): gRPC Dart client with proto codegen and InoClient wrapper"
```

---

## Task 6: BLoC state management

**Files:**
- Create: `ino.flutter/lib/state/ino_bloc.dart`
- Create: `ino.flutter/lib/state/persona_bloc.dart`
- Create: `ino.flutter/lib/persona/persona_state.dart`
- Create: `ino.flutter/test/state/ino_bloc_test.dart`
- Create: `ino.flutter/test/state/persona_bloc_test.dart`

- [ ] **Step 1: Write PersonaStateModel**

Create `ino.flutter/lib/persona/persona_state.dart`:

```dart
enum PersonaEmotion {
  sleeping,
  waking,
  idle,
  listening,
  thinking,
  acting,
  responding,
  celebrating,
  confused,
  evolving,
}

class PersonaStateModel {
  const PersonaStateModel({
    this.emotion = PersonaEmotion.idle,
    this.energy = 0.5,
    this.confidence = 1.0,
    this.domainAffinity = const {},
  });

  final PersonaEmotion emotion;
  final double energy;
  final double confidence;
  final Map<String, double> domainAffinity;

  PersonaStateModel copyWith({
    PersonaEmotion? emotion,
    double? energy,
    double? confidence,
    Map<String, double>? domainAffinity,
  }) {
    return PersonaStateModel(
      emotion: emotion ?? this.emotion,
      energy: energy ?? this.energy,
      confidence: confidence ?? this.confidence,
      domainAffinity: domainAffinity ?? this.domainAffinity,
    );
  }
}
```

- [ ] **Step 2: Write InoBloc**

Create `ino.flutter/lib/state/ino_bloc.dart`:

```dart
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';

// Events
sealed class InoEvent {}

class SendMessage extends InoEvent {
  SendMessage(this.message);
  final String message;
}

class MessageReceived extends InoEvent {
  MessageReceived(this.reply);
  final String reply;
}

// State
class InoState {
  const InoState({this.messages = const [], this.isLoading = false});

  final List<ChatMessage> messages;
  final bool isLoading;

  InoState copyWith({List<ChatMessage>? messages, bool? isLoading}) {
    return InoState(
      messages: messages ?? this.messages,
      isLoading: isLoading ?? this.isLoading,
    );
  }
}

class ChatMessage {
  const ChatMessage({required this.text, required this.isUser});
  final String text;
  final bool isUser;
}

// Bloc
class InoBloc extends Bloc<InoEvent, InoState> {
  InoBloc({required InoClient client})
      : _client = client,
        super(const InoState()) {
    on<SendMessage>(_onSendMessage);
    on<MessageReceived>(_onMessageReceived);
  }

  final InoClient _client;

  Future<void> _onSendMessage(SendMessage event, Emitter<InoState> emit) async {
    final userMsg = ChatMessage(text: event.message, isUser: true);
    emit(state.copyWith(
      messages: [...state.messages, userMsg],
      isLoading: true,
    ));

    try {
      final response = await _client.chat(event.message);
      add(MessageReceived(response.reply));
    } catch (e) {
      add(MessageReceived('Error: $e'));
    }
  }

  void _onMessageReceived(MessageReceived event, Emitter<InoState> emit) {
    final inoMsg = ChatMessage(text: event.reply, isUser: false);
    emit(state.copyWith(
      messages: [...state.messages, inoMsg],
      isLoading: false,
    ));
  }
}
```

- [ ] **Step 3: Write PersonaBloc**

Create `ino.flutter/lib/state/persona_bloc.dart`:

```dart
import 'dart:async';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/persona/persona_state.dart';

// Events
sealed class PersonaEvent {}

class PersonaStarted extends PersonaEvent {}

class PersonaUpdated extends PersonaEvent {
  PersonaUpdated(this.state);
  final PersonaStateModel state;
}

class PersonaEmotionChanged extends PersonaEvent {
  PersonaEmotionChanged(this.emotion);
  final PersonaEmotion emotion;
}

// Bloc
class PersonaBloc extends Bloc<PersonaEvent, PersonaStateModel> {
  PersonaBloc({required InoClient client})
      : _client = client,
        super(const PersonaStateModel(emotion: PersonaEmotion.sleeping)) {
    on<PersonaStarted>(_onStarted);
    on<PersonaUpdated>(_onUpdated);
    on<PersonaEmotionChanged>(_onEmotionChanged);
  }

  final InoClient _client;
  StreamSubscription<PersonaState>? _subscription;

  Future<void> _onStarted(PersonaStarted event, Emitter<PersonaStateModel> emit) async {
    emit(state.copyWith(emotion: PersonaEmotion.waking, energy: 0.3));

    await Future<void>.delayed(const Duration(seconds: 1));
    emit(state.copyWith(emotion: PersonaEmotion.idle, energy: 0.5));

    try {
      _subscription = _client.streamPersonaState().listen((grpcState) {
        final emotion = PersonaEmotion.values.firstWhere(
          (e) => e.name == grpcState.emotion,
          orElse: () => PersonaEmotion.idle,
        );
        add(PersonaUpdated(PersonaStateModel(
          emotion: emotion,
          energy: grpcState.energy,
          confidence: grpcState.confidence,
          domainAffinity: grpcState.domainAffinity,
        )));
      });
    } catch (_) {
      // gRPC not available yet — stay idle
    }
  }

  void _onUpdated(PersonaUpdated event, Emitter<PersonaStateModel> emit) {
    emit(event.state);
  }

  void _onEmotionChanged(PersonaEmotionChanged event, Emitter<PersonaStateModel> emit) {
    emit(state.copyWith(emotion: event.emotion));
  }

  @override
  Future<void> close() {
    _subscription?.cancel();
    return super.close();
  }
}
```

- [ ] **Step 4: Write InoBloc test**

Create `ino.flutter/test/state/ino_bloc_test.dart`:

```dart
import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/state/ino_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:mocktail/mocktail.dart';

class MockInoClient extends Mock implements InoClient {}

void main() {
  late MockInoClient mockClient;

  setUp(() {
    mockClient = MockInoClient();
  });

  group('InoBloc', () {
    test('initial state has no messages', () {
      final bloc = InoBloc(client: mockClient);
      expect(bloc.state.messages, isEmpty);
      expect(bloc.state.isLoading, isFalse);
    });

    blocTest<InoBloc, InoState>(
      'SendMessage adds user message and sets loading',
      build: () {
        when(() => mockClient.chat(any())).thenAnswer(
          (_) async => ChatResponse()..reply = 'hello back',
        );
        return InoBloc(client: mockClient);
      },
      act: (bloc) => bloc.add(SendMessage('hello')),
      expect: () => [
        isA<InoState>()
            .having((s) => s.messages.length, 'messages', 1)
            .having((s) => s.messages.first.isUser, 'isUser', true)
            .having((s) => s.isLoading, 'loading', true),
        isA<InoState>()
            .having((s) => s.messages.length, 'messages', 2)
            .having((s) => s.messages.last.isUser, 'isUser', false)
            .having((s) => s.messages.last.text, 'reply', 'hello back')
            .having((s) => s.isLoading, 'loading', false),
      ],
    );
  });
}
```

- [ ] **Step 5: Write PersonaBloc test**

Create `ino.flutter/test/state/persona_bloc_test.dart`:

```dart
import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/state/persona_bloc.dart';
import 'package:ino_flutter/persona/persona_state.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:mocktail/mocktail.dart';

class MockInoClient extends Mock implements InoClient {}

void main() {
  late MockInoClient mockClient;

  setUp(() {
    mockClient = MockInoClient();
  });

  group('PersonaBloc', () {
    test('initial state is sleeping', () {
      final bloc = PersonaBloc(client: mockClient);
      expect(bloc.state.emotion, PersonaEmotion.sleeping);
    });

    blocTest<PersonaBloc, PersonaStateModel>(
      'PersonaEmotionChanged updates emotion',
      build: () => PersonaBloc(client: mockClient),
      act: (bloc) => bloc.add(PersonaEmotionChanged(PersonaEmotion.thinking)),
      expect: () => [
        isA<PersonaStateModel>()
            .having((s) => s.emotion, 'emotion', PersonaEmotion.thinking),
      ],
    );
  });
}
```

- [ ] **Step 6: Run tests**

```bash
cd E:/ino/ino.flutter && flutter test test/state/
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
cd E:/ino && git add ino.flutter/lib/state/ ino.flutter/lib/persona/persona_state.dart ino.flutter/test/state/
git commit -m "feat(flutter): InoBloc and PersonaBloc with gRPC stream integration"
```

---

## Task 7: Rive persona widget

**Files:**
- Create: `ino.flutter/lib/persona/persona_widget.dart`
- Create: `ino.flutter/assets/rive/ino_persona.riv` (placeholder)

- [ ] **Step 1: Create placeholder Rive persona**

Since we cannot generate `.riv` files programmatically, create a **fallback widget** that renders a morphing shape animation using pure Flutter when no `.riv` file is available. The real `.riv` file will be authored in the Rive editor later.

Create `ino.flutter/lib/persona/persona_widget.dart`:

```dart
import 'dart:math';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/persona/persona_state.dart';
import 'package:ino_flutter/state/persona_bloc.dart';

class PersonaWidget extends StatefulWidget {
  const PersonaWidget({super.key, this.size = 200});

  final double size;

  @override
  State<PersonaWidget> createState() => _PersonaWidgetState();
}

class _PersonaWidgetState extends State<PersonaWidget>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 3),
    )..repeat();
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<PersonaBloc, PersonaStateModel>(
      builder: (context, personaState) {
        return AnimatedBuilder(
          animation: _controller,
          builder: (context, child) {
            return CustomPaint(
              size: Size(widget.size, widget.size),
              painter: _PersonaPainter(
                animation: _controller.value,
                emotion: personaState.emotion,
                energy: personaState.energy,
              ),
            );
          },
        );
      },
    );
  }
}

class _PersonaPainter extends CustomPainter {
  _PersonaPainter({
    required this.animation,
    required this.emotion,
    required this.energy,
  });

  final double animation;
  final PersonaEmotion emotion;
  final double energy;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final baseRadius = size.width * 0.35;
    final morphAmount = energy * 0.3;

    final color = _colorForEmotion(emotion);
    final paint = Paint()
      ..color = color.withValues(alpha: 0.8)
      ..style = PaintingStyle.fill;

    final glowPaint = Paint()
      ..color = color.withValues(alpha: 0.2)
      ..style = PaintingStyle.fill
      ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 20);

    // Glow
    canvas.drawCircle(center, baseRadius * 1.3, glowPaint);

    // Morphing shape — sine-wave deformation
    final path = Path();
    const segments = 64;
    for (var i = 0; i <= segments; i++) {
      final angle = (i / segments) * 2 * pi;
      final morphOffset = sin(angle * 3 + animation * 2 * pi) * morphAmount * baseRadius;
      final breathe = sin(animation * 2 * pi) * 0.05 * baseRadius;
      final r = baseRadius + morphOffset + breathe;
      final x = center.dx + cos(angle) * r;
      final y = center.dy + sin(angle) * r;
      if (i == 0) {
        path.moveTo(x, y);
      } else {
        path.lineTo(x, y);
      }
    }
    path.close();
    canvas.drawPath(path, paint);
  }

  Color _colorForEmotion(PersonaEmotion emotion) {
    return switch (emotion) {
      PersonaEmotion.sleeping => const Color(0xFF3D3D6B),
      PersonaEmotion.waking => const Color(0xFF5B5BA0),
      PersonaEmotion.idle => const Color(0xFF6C63FF),
      PersonaEmotion.listening => const Color(0xFF7B8CFF),
      PersonaEmotion.thinking => const Color(0xFFFF9F43),
      PersonaEmotion.acting => const Color(0xFF00D2FF),
      PersonaEmotion.responding => const Color(0xFF6C63FF),
      PersonaEmotion.celebrating => const Color(0xFF2ECC71),
      PersonaEmotion.confused => const Color(0xFFE74C3C),
      PersonaEmotion.evolving => const Color(0xFFA855F7),
    };
  }

  @override
  bool shouldRepaint(_PersonaPainter oldDelegate) =>
      animation != oldDelegate.animation ||
      emotion != oldDelegate.emotion ||
      energy != oldDelegate.energy;
}
```

This is a pure-Flutter placeholder. When the real `.riv` file is authored, swap `CustomPaint` for `RiveWidget` with `RiveWidgetController` and ViewModel data binding. The `PersonaBloc` → widget binding pattern stays identical.

- [ ] **Step 2: Commit**

```bash
cd E:/ino && git add ino.flutter/lib/persona/persona_widget.dart
git commit -m "feat(flutter): morphing shape persona widget with emotion-driven colors"
```

---

## Task 8: rfw runtime and chat components

**Files:**
- Create: `ino.flutter/lib/ui/ino_runtime.dart`
- Create: `ino.flutter/lib/ui/components/chat_bubble.dart`

- [ ] **Step 1: Write chat bubble rfw component**

Create `ino.flutter/lib/ui/components/chat_bubble.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:rfw/rfw.dart';

LocalWidgetLibrary createChatWidgets() {
  return LocalWidgetLibrary(<String, LocalWidgetBuilder>{
    'ChatBubble': (BuildContext context, DataSource source) {
      final text = source.v<String>(['text']) ?? '';
      final isUser = source.v<bool>(['isUser']) ?? false;

      return Align(
        alignment: isUser ? Alignment.centerRight : Alignment.centerLeft,
        child: Container(
          margin: const EdgeInsets.symmetric(vertical: 4, horizontal: 8),
          padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 14),
          constraints: const BoxConstraints(maxWidth: 300),
          decoration: BoxDecoration(
            color: isUser
                ? Theme.of(context).colorScheme.primary
                : Theme.of(context).colorScheme.surfaceContainerHighest,
            borderRadius: BorderRadius.only(
              topLeft: const Radius.circular(16),
              topRight: const Radius.circular(16),
              bottomLeft: Radius.circular(isUser ? 16 : 4),
              bottomRight: Radius.circular(isUser ? 4 : 16),
            ),
          ),
          child: Text(
            text,
            style: TextStyle(
              color: isUser
                  ? Theme.of(context).colorScheme.onPrimary
                  : Theme.of(context).colorScheme.onSurface,
            ),
          ),
        ),
      );
    },
  });
}
```

- [ ] **Step 2: Write rfw runtime setup**

Create `ino.flutter/lib/ui/ino_runtime.dart`:

```dart
import 'package:rfw/formats.dart';
import 'package:rfw/rfw.dart';
import 'package:ino_flutter/ui/components/chat_bubble.dart';

Runtime createInoRuntime() {
  final runtime = Runtime();
  runtime.update(
    const LibraryName(<String>['core']),
    createCoreWidgets(),
  );
  runtime.update(
    const LibraryName(<String>['material']),
    createMaterialWidgets(),
  );
  runtime.update(
    const LibraryName(<String>['ino']),
    createChatWidgets(),
  );
  return runtime;
}
```

- [ ] **Step 3: Commit**

```bash
cd E:/ino && git add ino.flutter/lib/ui/
git commit -m "feat(flutter): rfw runtime with chat bubble component library"
```

---

## Task 9: Onboarding screen

**Files:**
- Create: `ino.flutter/lib/screens/onboarding/onboarding_screen.dart`

- [ ] **Step 1: Write onboarding screen**

Create `ino.flutter/lib/screens/onboarding/onboarding_screen.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';
import 'package:ino_flutter/persona/persona_state.dart';
import 'package:ino_flutter/persona/persona_widget.dart';
import 'package:ino_flutter/state/persona_bloc.dart';

class OnboardingScreen extends StatefulWidget {
  const OnboardingScreen({super.key});

  @override
  State<OnboardingScreen> createState() => _OnboardingScreenState();
}

class _OnboardingScreenState extends State<OnboardingScreen> {
  int _step = 0;
  final _nameController = TextEditingController();

  @override
  void initState() {
    super.initState();
    context.read<PersonaBloc>().add(PersonaStarted());
  }

  @override
  void dispose() {
    _nameController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      body: Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const PersonaWidget(size: 250),
            const SizedBox(height: 40),
            AnimatedSwitcher(
              duration: const Duration(milliseconds: 500),
              child: _buildStep(),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildStep() {
    return switch (_step) {
      0 => _greeting(),
      1 => _nameInput(),
      2 => _ready(),
      _ => const SizedBox.shrink(),
    };
  }

  Widget _greeting() {
    return Column(
      key: const ValueKey('greeting'),
      children: [
        Text(
          "I'm ino — your personal intelligence.",
          style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                color: Colors.white70,
              ),
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 24),
        FilledButton(
          onPressed: () => setState(() => _step = 1),
          child: const Text('Continue'),
        ),
      ],
    );
  }

  Widget _nameInput() {
    return Column(
      key: const ValueKey('name'),
      children: [
        Text(
          'What should I call you?',
          style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                color: Colors.white70,
              ),
        ),
        const SizedBox(height: 16),
        SizedBox(
          width: 300,
          child: TextField(
            controller: _nameController,
            autofocus: true,
            style: const TextStyle(color: Colors.white),
            decoration: const InputDecoration(
              hintText: 'Your name',
              hintStyle: TextStyle(color: Colors.white30),
              border: OutlineInputBorder(),
            ),
            onSubmitted: (_) => _onNameSubmitted(),
          ),
        ),
        const SizedBox(height: 16),
        FilledButton(
          onPressed: _onNameSubmitted,
          child: const Text('Continue'),
        ),
      ],
    );
  }

  void _onNameSubmitted() {
    context.read<PersonaBloc>().add(
          PersonaEmotionChanged(PersonaEmotion.celebrating),
        );
    setState(() => _step = 2);
    Future.delayed(const Duration(seconds: 2), () {
      if (mounted) context.go('/home');
    });
  }

  Widget _ready() {
    final name = _nameController.text.isNotEmpty
        ? _nameController.text
        : 'there';
    return Text(
      key: const ValueKey('ready'),
      'Nice to meet you, $name. Let\'s go.',
      style: Theme.of(context).textTheme.headlineSmall?.copyWith(
            color: Colors.white70,
          ),
      textAlign: TextAlign.center,
    );
  }
}
```

- [ ] **Step 2: Commit**

```bash
cd E:/ino && git add ino.flutter/lib/screens/onboarding/
git commit -m "feat(flutter): persona-first onboarding — greeting, name, transition to home"
```

---

## Task 10: Home screen with chat

**Files:**
- Create: `ino.flutter/lib/screens/home/home_screen.dart`

- [ ] **Step 1: Write home screen**

Create `ino.flutter/lib/screens/home/home_screen.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/persona/persona_state.dart';
import 'package:ino_flutter/persona/persona_widget.dart';
import 'package:ino_flutter/state/ino_bloc.dart';
import 'package:ino_flutter/state/persona_bloc.dart';

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  final _inputController = TextEditingController();
  final _scrollController = ScrollController();

  @override
  void dispose() {
    _inputController.dispose();
    _scrollController.dispose();
    super.dispose();
  }

  void _sendMessage() {
    final text = _inputController.text.trim();
    if (text.isEmpty) return;
    _inputController.clear();

    context.read<PersonaBloc>().add(
          PersonaEmotionChanged(PersonaEmotion.thinking),
        );
    context.read<InoBloc>().add(SendMessage(text));
  }

  void _scrollToBottom() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scrollController.hasClients) {
        _scrollController.animateTo(
          _scrollController.position.maxScrollExtent,
          duration: const Duration(milliseconds: 300),
          curve: Curves.easeOut,
        );
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      body: SafeArea(
        child: Column(
          children: [
            // Persona — shrinks as messages grow
            BlocBuilder<InoBloc, InoState>(
              builder: (context, state) {
                final personaSize = state.messages.isEmpty ? 250.0 : 120.0;
                return AnimatedContainer(
                  duration: const Duration(milliseconds: 500),
                  height: personaSize,
                  child: PersonaWidget(size: personaSize),
                );
              },
            ),

            // Messages
            Expanded(
              child: BlocConsumer<InoBloc, InoState>(
                listener: (context, state) {
                  _scrollToBottom();
                  if (!state.isLoading && state.messages.isNotEmpty) {
                    context.read<PersonaBloc>().add(
                          PersonaEmotionChanged(PersonaEmotion.idle),
                        );
                  }
                },
                builder: (context, state) {
                  return ListView.builder(
                    controller: _scrollController,
                    padding: const EdgeInsets.symmetric(horizontal: 16),
                    itemCount: state.messages.length,
                    itemBuilder: (context, index) {
                      final msg = state.messages[index];
                      return _ChatBubble(message: msg);
                    },
                  );
                },
              ),
            ),

            // Loading indicator
            BlocBuilder<InoBloc, InoState>(
              builder: (context, state) {
                if (!state.isLoading) return const SizedBox.shrink();
                return const Padding(
                  padding: EdgeInsets.all(8),
                  child: SizedBox(
                    width: 24,
                    height: 24,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                );
              },
            ),

            // Input
            Padding(
              padding: const EdgeInsets.all(12),
              child: Row(
                children: [
                  Expanded(
                    child: TextField(
                      controller: _inputController,
                      style: const TextStyle(color: Colors.white),
                      decoration: InputDecoration(
                        hintText: 'Talk to ino...',
                        hintStyle: const TextStyle(color: Colors.white30),
                        filled: true,
                        fillColor: Colors.white10,
                        border: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(24),
                          borderSide: BorderSide.none,
                        ),
                        contentPadding: const EdgeInsets.symmetric(
                          horizontal: 20,
                          vertical: 12,
                        ),
                      ),
                      onSubmitted: (_) => _sendMessage(),
                    ),
                  ),
                  const SizedBox(width: 8),
                  IconButton.filled(
                    onPressed: _sendMessage,
                    icon: const Icon(Icons.send_rounded),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _ChatBubble extends StatelessWidget {
  const _ChatBubble({required this.message});
  final ChatMessage message;

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: message.isUser ? Alignment.centerRight : Alignment.centerLeft,
      child: Container(
        margin: const EdgeInsets.symmetric(vertical: 4),
        padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 14),
        constraints: const BoxConstraints(maxWidth: 300),
        decoration: BoxDecoration(
          color: message.isUser
              ? Theme.of(context).colorScheme.primary
              : Theme.of(context).colorScheme.surfaceContainerHighest,
          borderRadius: BorderRadius.only(
            topLeft: const Radius.circular(16),
            topRight: const Radius.circular(16),
            bottomLeft: Radius.circular(message.isUser ? 16 : 4),
            bottomRight: Radius.circular(message.isUser ? 4 : 16),
          ),
        ),
        child: Text(
          message.text,
          style: TextStyle(
            color: message.isUser
                ? Theme.of(context).colorScheme.onPrimary
                : Theme.of(context).colorScheme.onSurface,
          ),
        ),
      ),
    );
  }
}
```

- [ ] **Step 2: Commit**

```bash
cd E:/ino && git add ino.flutter/lib/screens/home/
git commit -m "feat(flutter): home screen with persona, chat messages, and input"
```

---

## Task 11: Build, verify, and integration smoke test

**Files:** None new — verification only.

- [ ] **Step 1: Build the C# solution**

```bash
cd E:/ino && dotnet build ino.slnx
```

Expected: 0 errors.

- [ ] **Step 2: Build the Flutter app**

```bash
cd E:/ino/ino.flutter && flutter build web
```

Expected: Build successful. Output in `build/web/`.

- [ ] **Step 3: Run Flutter tests**

```bash
cd E:/ino/ino.flutter && flutter test
```

Expected: All tests pass.

- [ ] **Step 4: Run C# tests**

```bash
cd E:/ino && dotnet test ino.slnx
```

Expected: All existing tests pass. No regressions.

- [ ] **Step 5: Start Aspire and verify gRPC resource appears**

```bash
aspire start
```

Check the Aspire dashboard at https://localhost:17280 — the `grpc` resource should appear and become Healthy. The `ino-flutter` resource should appear but NOT auto-start (ExplicitStart).

- [ ] **Step 6: Test gRPC service responds**

Use grpcurl or a simple test client to verify the Chat endpoint works:

```bash
# If grpcurl is available:
grpcurl -plaintext -d '{"message": "hello", "user_id": "test"}' localhost:5400 ino.v1.Ino/Chat
```

Expected: `{ "reply": "...", "neuronId": "cortex" }`

If grpcurl is not available, use the Flutter app:
```bash
cd E:/ino/ino.flutter && flutter run -d chrome --web-port 8080
```

Navigate through onboarding, type "hello" in chat. Verify the persona morphs and a response appears.

- [ ] **Step 7: Stop Aspire**

```bash
aspire stop
```

- [ ] **Step 8: Final commit if any fixes were needed**

```bash
cd E:/ino && git add -A && git status
# Only commit if there are changes from fixes
git commit -m "fix(flutter): integration fixes from smoke test"
```

---

## Summary

| Task | What it delivers | Key files |
|---|---|---|
| 1 | protoc + Dart plugin installed | (system) |
| 2 | Proto contract — shared source of truth | `iaw/Grpc/Protos/ino.proto` |
| 3 | gRPC C# service + Aspire wiring | `iaw/Grpc/`, `AppHost.cs`, `ino.slnx` |
| 4 | Flutter project scaffold | `ino.flutter/pubspec.yaml`, `main.dart`, `app.dart` |
| 5 | Dart gRPC client with codegen | `ino.flutter/lib/grpc/` |
| 6 | BLoC state management | `ino_bloc.dart`, `persona_bloc.dart` + tests |
| 7 | Morphing shape persona (placeholder) | `persona_widget.dart` |
| 8 | rfw runtime + chat components | `ino_runtime.dart`, `chat_bubble.dart` |
| 9 | Persona-first onboarding | `onboarding_screen.dart` |
| 10 | Home screen with chat | `home_screen.dart` |
| 11 | Build + integration smoke test | (verification) |
