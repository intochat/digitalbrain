# Phase 2d: Voice V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Users speak to ino through the persona. Audio streams to the backend via gRPC client streaming (native) or WebSocket (web), Whisper transcribes, the transcribed text feeds into the existing chat pipeline. The persona reacts (listening → thinking → responding).

**Architecture:** Flutter captures audio via `record` v6.2.0 as PCM16 chunks. An `AudioTransport` abstraction selects gRPC client streaming (mobile/desktop — full HTTP/2) or WebSocket (web — gRPC-Web doesn't support client streaming). The gRPC service receives audio, writes to a temp file, calls the existing `IAudioTranscriptionService` (Whisper via Foundry Local), returns the transcript. The transcript is then fed into `Chat` as a normal text message.

**Tech Stack:** Flutter (`record` ^6.2.0), gRPC client streaming, WebSocket, Whisper (existing `FoundryLocalTranscriptionService`), ASP.NET Core

---

## File Map

### C# — Modified/New

| File | Change |
|---|---|
| `iaw/Grpc/Protos/ino.proto` | Add `rpc TranscribeAudio(stream AudioChunk) returns (TranscribeResponse)` |
| `iaw/Grpc/Program.cs` | Add Whisper provider, WebSocket endpoint `/ws/audio` |
| `iaw/Grpc/Services/InoService.cs` | Implement `TranscribeAudio` with client streaming |
| `iaw/Grpc/Grpc.csproj` | Add Concentus + NAudio for audio conversion |

### Flutter — New/Modified

| File | Change |
|---|---|
| `ino.flutter/lib/voice/audio_transport.dart` | Abstract transport: gRPC vs WebSocket |
| `ino.flutter/lib/voice/grpc_audio_transport.dart` | gRPC client streaming implementation |
| `ino.flutter/lib/voice/websocket_audio_transport.dart` | WebSocket implementation for web |
| `ino.flutter/lib/voice/audio_recorder.dart` | `record` package wrapper, PCM16 streaming |
| `ino.flutter/lib/state/ino_bloc.dart` | Add voice events: StartRecording, StopRecording, TranscriptReceived |
| `ino.flutter/lib/screens/home/home_screen.dart` | Add mic button, recording indicator |
| `ino.flutter/protos/ino.proto` | Sync with backend proto |
| `ino.flutter/lib/grpc/generated/` | Regenerate Dart stubs |
| `ino.flutter/pubspec.yaml` | Add `record: ^6.2.0` dependency |

---

## Task 1: Proto contract — add voice RPCs

**Files:**
- Modify: `iaw/Grpc/Protos/ino.proto`

- [ ] **Step 1: Read current proto file**

Read `iaw/Grpc/Protos/ino.proto`.

- [ ] **Step 2: Add voice RPC and messages**

Add to the `service Ino` block:

```protobuf
  rpc TranscribeAudio(stream AudioChunk) returns (TranscribeResponse);
```

Add the new message definitions:

```protobuf
message AudioChunk {
  bytes data = 1;
  int32 sample_rate = 2;
  int32 channels = 3;
  string format = 4;
}

message TranscribeResponse {
  string text = 1;
  float confidence = 2;
  bool ok = 3;
  string error = 4;
}
```

- [ ] **Step 3: Copy to Flutter and regenerate Dart stubs**

```bash
cp E:/ino/iaw/Grpc/Protos/ino.proto E:/ino/ino.flutter/protos/

export PATH="$PATH:/c/Users/vhorb/AppData/Local/Microsoft/WinGet/Packages/Google.Protobuf_Microsoft.Winget.Source_8wekyb3d8bbwe/bin"
cd E:/ino/ino.flutter && protoc --dart_out=grpc:lib/grpc/generated -Iprotos protos/ino.proto
```

- [ ] **Step 4: Build C# to verify proto compiles**

```bash
cd E:/ino && dotnet build ino.slnx
```

- [ ] **Step 5: Commit**

```bash
git add iaw/Grpc/Protos/ino.proto ino.flutter/protos/ ino.flutter/lib/grpc/generated/
git commit -m "feat(proto): add TranscribeAudio client streaming RPC with AudioChunk messages"
```

---

## Task 2: Backend — Whisper in gRPC service

**Files:**
- Modify: `iaw/Grpc/Program.cs`
- Modify: `iaw/Grpc/Services/InoService.cs`
- Modify: `iaw/Grpc/Grpc.csproj`

- [ ] **Step 1: Read existing files**

Read `iaw/Grpc/Program.cs`, `iaw/Grpc/Services/InoService.cs`, `iaw/Grpc/Grpc.csproj`.

- [ ] **Step 2: Add audio dependencies to Grpc.csproj**

Add package references for audio conversion (Concentus + NAudio are already in Directory.Packages.props):

```xml
<PackageReference Include="Concentus" />
<PackageReference Include="Concentus.Oggfile" />
<PackageReference Include="NAudio" />
```

- [ ] **Step 3: Add Whisper provider to Program.cs**

Read how Telegram's `Program.cs` registers the Whisper provider. Add the same pattern to the gRPC service's `Program.cs`:

```csharp
using Telegram; // for FoundryLocalTranscriptionService — or check its actual namespace

builder.Services.AddSingleton<IAudioConverter, AudioConverter>();
builder.AddWhisperProvider<FoundryLocalTranscriptionService>();
```

Check the actual namespace of `FoundryLocalTranscriptionService` and `AudioConverter` — they may be in `TelegramClient.Services` or similar. If they're in the Telegram project, the Grpc project needs a project reference to Telegram. If that creates a circular dependency, extract the transcription service to a shared location.

IMPORTANT: If `FoundryLocalTranscriptionService` and `AudioConverter` are in the Telegram project, DON'T add a reference to Telegram from Grpc. Instead, register the services directly:

```csharp
builder.Services.AddSingleton<Core.AI.IAudioConverter, AudioConverterForGrpc>();
builder.AddWhisperProvider<FoundryLocalTranscriptionService>();
```

Actually, check if `IAudioTranscriptionService` and `AddWhisperProvider` are in `Aspire.Client` (shared) or `Telegram` (specific). The `AddWhisperProvider<T>` extension is in `IAWClientExtensions` which is in `Aspire.Client` — so it's available. The `FoundryLocalTranscriptionService` class is in `Telegram` — this IS a problem.

**Resolution:** Create a minimal `AudioConverter` in the Grpc project (or reference the Telegram project if no circular dependency). Or better: move `FoundryLocalTranscriptionService` and `AudioConverter` to a shared location. But that's a bigger refactor.

**Simplest approach for V1:** Copy `AudioConverter` locally into the Grpc project (it's 40 lines). Reference `FoundryLocalTranscriptionService` — check if we can add the Telegram project reference without circularity (Telegram doesn't reference Grpc, so it should be fine).

- [ ] **Step 4: Add WebSocket endpoint for web audio**

In `Program.cs`, add after `app.UseGrpcWeb()`:

```csharp
app.UseWebSockets();

app.Map("/ws/audio", async (HttpContext context, IAudioTranscriptionService transcriber) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    using var ms = new MemoryStream();

    var buffer = new byte[4096];
    while (true)
    {
        var result = await ws.ReceiveAsync(buffer, context.RequestAborted);
        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
            break;
        ms.Write(buffer, 0, result.Count);
    }

    ms.Position = 0;
    var text = await transcriber.TranscribeAsync(ms, "audio.wav", context.RequestAborted);

    var responseBytes = System.Text.Encoding.UTF8.GetBytes(text);
    await ws.SendAsync(responseBytes, System.Net.WebSockets.WebSocketMessageType.Text, true, context.RequestAborted);
    await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, context.RequestAborted);
});
```

- [ ] **Step 5: Implement TranscribeAudio in InoService**

Add `IAudioTranscriptionService` to the constructor and implement the client streaming RPC:

```csharp
public class InoService(IClusterClient clusterClient, IAudioTranscriptionService transcriber)
    : global::Ino.Grpc.Ino.InoBase
{
    // ... existing fields ...

    public override async Task<TranscribeResponse> TranscribeAudio(
        IAsyncStreamReader<AudioChunk> requestStream,
        ServerCallContext context)
    {
        using var ms = new MemoryStream();
        
        await foreach (var chunk in requestStream.ReadAllAsync(context.CancellationToken))
        {
            ms.Write(chunk.Data.Span);
        }

        if (ms.Length == 0)
            return new TranscribeResponse { Ok = false, Error = "No audio data received" };

        ms.Position = 0;

        try
        {
            var text = await transcriber.TranscribeAsync(ms, "recording.wav", context.CancellationToken);
            return new TranscribeResponse { Ok = true, Text = text.Trim(), Confidence = 1.0f };
        }
        catch (Exception ex)
        {
            return new TranscribeResponse { Ok = false, Error = ex.Message };
        }
    }
}
```

NOTE: `IAudioTranscriptionService` is injected via constructor alongside `IClusterClient`. Check if it needs to be resolved from DI or if primary constructor injection works (it should with ASP.NET Core).

- [ ] **Step 6: Build and test**

```bash
cd E:/ino && dotnet build ino.slnx && dotnet test ino.slnx
```

- [ ] **Step 7: Commit**

```bash
git add iaw/Grpc/
git commit -m "feat(voice): TranscribeAudio gRPC endpoint with Whisper + WebSocket /ws/audio fallback"
```

---

## Task 3: Flutter — AudioTransport abstraction

**Files:**
- Create: `ino.flutter/lib/voice/audio_transport.dart`
- Create: `ino.flutter/lib/voice/grpc_audio_transport.dart`
- Create: `ino.flutter/lib/voice/websocket_audio_transport.dart`

- [ ] **Step 1: Read the gRPC client to understand stub methods**

Read `ino.flutter/lib/grpc/generated/ino.pbgrpc.dart` to find the generated `transcribeAudio` client streaming method signature.

- [ ] **Step 2: Create AudioTransport abstract class**

Create `ino.flutter/lib/voice/audio_transport.dart`:

```dart
import 'dart:typed_data';

abstract class AudioTransport {
  Future<String> transcribe(Stream<Uint8List> audioChunks, {int sampleRate = 16000, int channels = 1});
  Future<void> close();
}
```

- [ ] **Step 3: Create gRPC transport (native platforms)**

Create `ino.flutter/lib/voice/grpc_audio_transport.dart`:

```dart
import 'dart:typed_data';
import 'package:ino_flutter/grpc/generated/ino.pb.dart' as pb;
import 'package:ino_flutter/grpc/generated/ino.pbgrpc.dart' as grpc;
import 'package:grpc/grpc_or_grpcweb.dart';
import 'package:ino_flutter/voice/audio_transport.dart';

class GrpcAudioTransport implements AudioTransport {
  GrpcAudioTransport({required this.channel});

  final GrpcOrGrpcWebClientChannel channel;

  @override
  Future<String> transcribe(
    Stream<Uint8List> audioChunks, {
    int sampleRate = 16000,
    int channels = 1,
  }) async {
    final stub = grpc.InoClient(channel);

    final requestStream = audioChunks.map((chunk) => pb.AudioChunk()
      ..data = chunk
      ..sampleRate = sampleRate
      ..channels = channels
      ..format = 'pcm16');

    final response = await stub.transcribeAudio(requestStream);

    if (!response.ok) {
      throw Exception(response.error);
    }
    return response.text;
  }

  @override
  Future<void> close() async {}
}
```

- [ ] **Step 4: Create WebSocket transport (web platform)**

Create `ino.flutter/lib/voice/websocket_audio_transport.dart`:

```dart
import 'dart:typed_data';
import 'package:web_socket_channel/web_socket_channel.dart';
import 'package:ino_flutter/voice/audio_transport.dart';

class WebSocketAudioTransport implements AudioTransport {
  WebSocketAudioTransport({required this.wsUrl});

  final String wsUrl;

  @override
  Future<String> transcribe(
    Stream<Uint8List> audioChunks, {
    int sampleRate = 16000,
    int channels = 1,
  }) async {
    final channel = WebSocketChannel.connect(Uri.parse(wsUrl));
    await channel.ready;

    await for (final chunk in audioChunks) {
      channel.sink.add(chunk);
    }

    await channel.sink.close();

    final response = await channel.stream.first;
    return response as String;
  }

  @override
  Future<void> close() async {}
}
```

- [ ] **Step 5: Commit**

```bash
cd E:/ino && git add ino.flutter/lib/voice/
git commit -m "feat(voice): AudioTransport abstraction with gRPC and WebSocket implementations"
```

---

## Task 4: Flutter — Audio recorder wrapper

**Files:**
- Modify: `ino.flutter/pubspec.yaml`
- Create: `ino.flutter/lib/voice/audio_recorder.dart`

- [ ] **Step 1: Add record package to pubspec.yaml**

Add to dependencies in `pubspec.yaml`:

```yaml
  record: ^6.2.0
```

Run:
```bash
cd E:/ino/ino.flutter && flutter pub get
```

- [ ] **Step 2: Create AudioRecorderService**

Create `ino.flutter/lib/voice/audio_recorder.dart`:

```dart
import 'dart:async';
import 'dart:typed_data';
import 'package:record/record.dart';

class AudioRecorderService {
  final _recorder = AudioRecorder();
  StreamController<Uint8List>? _controller;
  StreamSubscription? _subscription;

  bool get isRecording => _controller != null;

  Future<bool> hasPermission() => _recorder.hasPermission();

  Stream<Uint8List> startRecording() {
    _controller = StreamController<Uint8List>();

    _startInternal();
    return _controller!.stream;
  }

  Future<void> _startInternal() async {
    final stream = await _recorder.startStream(
      const RecordConfig(
        encoder: AudioEncoder.pcm16bits,
        sampleRate: 16000,
        numChannels: 1,
        autoGain: true,
        echoCancel: true,
        noiseSuppress: true,
      ),
    );

    _subscription = stream.listen(
      (data) => _controller?.add(Uint8List.fromList(data)),
      onError: (error) => _controller?.addError(error),
      onDone: () => _controller?.close(),
    );
  }

  Future<void> stopRecording() async {
    await _subscription?.cancel();
    _subscription = null;
    await _recorder.stop();
    await _controller?.close();
    _controller = null;
  }

  Future<void> dispose() async {
    await stopRecording();
    _recorder.dispose();
  }
}
```

- [ ] **Step 3: Commit**

```bash
cd E:/ino && git add ino.flutter/pubspec.yaml ino.flutter/lib/voice/audio_recorder.dart
git commit -m "feat(voice): AudioRecorderService wrapping record package for PCM16 streaming"
```

---

## Task 5: Flutter — Voice events in InoBloc + mic button

**Files:**
- Modify: `ino.flutter/lib/state/ino_bloc.dart`
- Modify: `ino.flutter/lib/screens/home/home_screen.dart`
- Modify: `ino.flutter/lib/main.dart`

- [ ] **Step 1: Read existing InoBloc and home screen**

Read `ino.flutter/lib/state/ino_bloc.dart` and `ino.flutter/lib/screens/home/home_screen.dart` to understand current structure.

- [ ] **Step 2: Add voice events and state to InoBloc**

Add these events to the sealed `InoBlocEvent` class:

```dart
class StartRecording extends InoBlocEvent {}
class StopRecording extends InoBlocEvent {}
class _TranscriptReceived extends InoBlocEvent {
  _TranscriptReceived(this.text);
  final String text;
}
class _TranscriptFailed extends InoBlocEvent {
  _TranscriptFailed(this.error);
  final String error;
}
```

Add `isRecording` field to the state class:

```dart
class InoBlocState {
  const InoBlocState({this.messages = const [], this.isLoading = false, this.isRecording = false});
  final List<ChatMessage> messages;
  final bool isLoading;
  final bool isRecording;
  // update copyWith to include isRecording
}
```

Add the `AudioRecorderService` and `AudioTransport` to the bloc constructor:

```dart
class InoBloc extends Bloc<InoBlocEvent, InoBlocState> {
  InoBloc({
    required InoGrpcClient client,
    required AudioTransport audioTransport,
    required AudioRecorderService recorder,
  }) : _client = client,
       _audioTransport = audioTransport,
       _recorder = recorder,
       super(const InoBlocState()) {
    on<SendMessage>(_onSendMessage);
    on<_MessageReceived>(_onMessageReceived);
    on<_MessageFailed>(_onMessageFailed);
    on<StartRecording>(_onStartRecording);
    on<StopRecording>(_onStopRecording);
    on<_TranscriptReceived>(_onTranscriptReceived);
    on<_TranscriptFailed>(_onTranscriptFailed);
  }

  final InoGrpcClient _client;
  final AudioTransport _audioTransport;
  final AudioRecorderService _recorder;
```

Implement handlers:

```dart
  Future<void> _onStartRecording(StartRecording event, Emitter<InoBlocState> emit) async {
    final hasPermission = await _recorder.hasPermission();
    if (!hasPermission) return;
    _recorder.startRecording(); // stream is held internally, consumed on stop
    emit(state.copyWith(isRecording: true));
  }

  Future<void> _onStopRecording(StopRecording event, Emitter<InoBlocState> emit) async {
    if (!_recorder.isRecording) return;

    // Collect the audio stream before stopping
    final audioStream = _recorder.startRecording(); // This won't work — we need to capture the stream from start
    // CORRECTION: The stream was created in startRecording. We need to buffer it.
    // Better approach: store the stream reference in StartRecording, consume in StopRecording.
  }
```

ACTUALLY — the cleaner pattern is: `StartRecording` creates the stream and stores it. `StopRecording` stops the recorder (which closes the stream), then sends the collected stream to the transport.

Revise the bloc to store the recording stream:

```dart
  Stream<Uint8List>? _audioStream;

  Future<void> _onStartRecording(StartRecording event, Emitter<InoBlocState> emit) async {
    final hasPermission = await _recorder.hasPermission();
    if (!hasPermission) return;
    _audioStream = _recorder.startRecording();
    emit(state.copyWith(isRecording: true));
  }

  Future<void> _onStopRecording(StopRecording event, Emitter<InoBlocState> emit) async {
    if (!_recorder.isRecording || _audioStream == null) return;

    final stream = _audioStream!;
    _audioStream = null;
    await _recorder.stopRecording();
    emit(state.copyWith(isRecording: false, isLoading: true));

    try {
      final text = await _audioTransport.transcribe(stream);
      if (text.isNotEmpty) {
        add(_TranscriptReceived(text));
      } else {
        emit(state.copyWith(isLoading: false));
      }
    } catch (e) {
      add(_TranscriptFailed('$e'));
    }
  }

  void _onTranscriptReceived(_TranscriptReceived event, Emitter<InoBlocState> emit) {
    // Feed transcript into chat as if user typed it
    add(SendMessage(event.text));
  }

  void _onTranscriptFailed(_TranscriptFailed event, Emitter<InoBlocState> emit) {
    final errorMsg = ChatMessage(text: 'Voice error: ${event.error}', isUser: false);
    emit(state.copyWith(
      messages: [...state.messages, errorMsg],
      isLoading: false,
    ));
  }
```

IMPORTANT: The `_audioStream` approach has a subtlety — the `record` package's `startStream()` returns a stream that's already producing data. When we call `_audioTransport.transcribe(stream)` after stopping, the stream is already closed. We need to buffer the chunks during recording.

Better approach — buffer in the bloc:

```dart
  List<Uint8List> _audioBuffer = [];
  StreamSubscription? _audioSubscription;

  Future<void> _onStartRecording(StartRecording event, Emitter<InoBlocState> emit) async {
    final hasPermission = await _recorder.hasPermission();
    if (!hasPermission) return;

    _audioBuffer = [];
    final stream = _recorder.startRecording();
    _audioSubscription = stream.listen((chunk) => _audioBuffer.add(chunk));
    emit(state.copyWith(isRecording: true));
  }

  Future<void> _onStopRecording(StopRecording event, Emitter<InoBlocState> emit) async {
    if (!_recorder.isRecording) return;

    await _audioSubscription?.cancel();
    await _recorder.stopRecording();
    emit(state.copyWith(isRecording: false, isLoading: true));

    if (_audioBuffer.isEmpty) {
      emit(state.copyWith(isLoading: false));
      return;
    }

    final chunks = List<Uint8List>.from(_audioBuffer);
    _audioBuffer = [];

    try {
      final text = await _audioTransport.transcribe(Stream.fromIterable(chunks));
      if (text.isNotEmpty) {
        add(_TranscriptReceived(text));
      } else {
        emit(state.copyWith(isLoading: false));
      }
    } catch (e) {
      add(_TranscriptFailed('$e'));
    }
  }
```

Use this buffered approach.

- [ ] **Step 3: Update main.dart**

Add the AudioTransport and AudioRecorderService construction. Since platform detection at build time isn't trivial in Dart, use `kIsWeb` from `package:flutter/foundation.dart`:

```dart
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:ino_flutter/voice/audio_transport.dart';
import 'package:ino_flutter/voice/grpc_audio_transport.dart';
import 'package:ino_flutter/voice/websocket_audio_transport.dart';
import 'package:ino_flutter/voice/audio_recorder.dart';

void main() {
  final client = InoGrpcClient(host: 'localhost', port: 5400);

  final AudioTransport audioTransport = kIsWeb
      ? WebSocketAudioTransport(wsUrl: 'ws://localhost:5400/ws/audio')
      : GrpcAudioTransport(channel: client.channel);

  final recorder = AudioRecorderService();

  runApp(
    MultiBlocProvider(
      providers: [
        BlocProvider(create: (_) => InoBloc(
          client: client,
          audioTransport: audioTransport,
          recorder: recorder,
        )),
        BlocProvider(create: (_) => PersonaBloc(client: client)),
        BlocProvider(create: (_) => SkillsBloc(client: client)),
      ],
      child: const InoApp(),
    ),
  );
}
```

NOTE: `client.channel` needs to be exposed from `InoGrpcClient`. Add a getter if not present:
```dart
GrpcOrGrpcWebClientChannel get channel => _channel;
```

- [ ] **Step 4: Add mic button to home screen**

In the input bar area of `home_screen.dart`, add a mic toggle button next to the send button:

```dart
// In the input Row, before the send button:
IconButton(
  icon: Icon(
    state.isRecording ? Icons.stop_circle : Icons.mic,
    color: state.isRecording ? Colors.red : Colors.white54,
  ),
  onPressed: () {
    if (state.isRecording) {
      context.read<InoBloc>().add(StopRecording());
    } else {
      context.read<PersonaBloc>().add(PersonaEmotionChanged(PersonaEmotion.listening));
      context.read<InoBloc>().add(StartRecording());
    }
  },
),
```

The input bar should use `BlocBuilder<InoBloc, InoBlocState>` to access `state.isRecording`. Also show a recording indicator above the input when recording:

```dart
if (state.isRecording)
  const Padding(
    padding: EdgeInsets.all(8),
    child: Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        Icon(Icons.fiber_manual_record, color: Colors.red, size: 12),
        SizedBox(width: 8),
        Text('Recording...', style: TextStyle(color: Colors.red)),
      ],
    ),
  ),
```

- [ ] **Step 5: Update tests**

The existing `InoBloc` tests will break because the constructor now requires `audioTransport` and `recorder`. Update `test/state/ino_bloc_test.dart`:

Add mocks:
```dart
class MockAudioTransport extends Mock implements AudioTransport {}
class MockAudioRecorderService extends Mock implements AudioRecorderService {}
```

Update bloc construction in tests:
```dart
InoBloc(
  client: mockClient,
  audioTransport: MockAudioTransport(),
  recorder: MockAudioRecorderService(),
)
```

- [ ] **Step 6: Verify**

```bash
cd E:/ino/ino.flutter && flutter analyze --no-fatal-infos && flutter test
```

- [ ] **Step 7: Commit**

```bash
cd E:/ino && git add ino.flutter/
git commit -m "feat(voice): mic button, recording UI, voice→text→chat pipeline"
```

---

## Task 6: Integration verification

- [ ] **Step 1: Build everything**

```bash
cd E:/ino && dotnet build ino.slnx
cd E:/ino/ino.flutter && flutter build web
```

- [ ] **Step 2: Run all tests**

```bash
cd E:/ino && dotnet test ino.slnx
cd E:/ino/ino.flutter && flutter test
```

- [ ] **Step 3: Verify end-to-end** (manual with Aspire)

1. `aspire start`
2. Verify `grpc` resource is Healthy
3. Start Flutter: `flutter run -d chrome --web-port 8080` (or via Aspire)
4. Navigate to home screen
5. Tap mic button — should start recording (red indicator)
6. Speak, tap stop — should transcribe via Whisper and send as chat
7. `aspire stop`

---

## Summary

| Task | What it delivers |
|---|---|
| 1 | Proto: `TranscribeAudio` client streaming RPC + `AudioChunk`/`TranscribeResponse` messages |
| 2 | Backend: Whisper transcription in gRPC service + WebSocket `/ws/audio` fallback |
| 3 | Flutter: `AudioTransport` abstraction with gRPC and WebSocket implementations |
| 4 | Flutter: `AudioRecorderService` wrapping `record` package for PCM16 streaming |
| 5 | Flutter: Voice events in InoBloc, mic button in home screen, recording UI |
| 6 | Integration verification |
