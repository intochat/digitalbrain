# Native PersonaPlex Voice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local, native PersonaPlex audio-to-audio Voice tab without placing a transcription pipeline in the conversation path.

**Architecture:** A dedicated AI runtime project runs the four graph PersonaPlex stream and exposes a small session contract to Kernel. Kernel carries live PCM over a separate WebSocket, while Flutter captures, sends, receives, and plays PCM in a separate Voice destination. Aspire projects non-secret runtime configuration into Kernel and reports readiness through Kernel health/telemetry.

**Tech Stack:** .NET 11, ONNX Runtime CUDA and `OrtIoBinding`, ASP.NET Core WebSockets, .NET Aspire 13.5, Flutter/Dart, `record` 7.1.1, xUnit, Playwright.

**Spec:** `docs/superpowers/specs/2026-08-21-native-personaplex-voice-design.md`

## Global Constraints

- Never route PersonaPlex frames through `IAudioTranscriptionService`, `IChatClient`, `MapChatVoice`, or Orleans.
- Use 24,000 Hz mono PCM16 and exactly 1,920 samples per real-time frame.
- Require CUDA when PersonaPlex is enabled; expose an unavailable state instead of falling back.
- Keep the temporal KV cache GPU-resident through `OrtIoBinding`.
- Keep model artifacts and raw audio out of Git, logs, and journals.

---

### Task 1: Native contracts and configuration

**Files:**
- Create: `src/Modules/AI/Contracts/PersonaPlex/IPersonaPlexSession.cs`
- Create: `src/Modules/AI/Contracts/PersonaPlex/PersonaPlexAudioFrame.cs`
- Create: `src/Modules/AI/Contracts/PersonaPlex/PersonaPlexReadiness.cs`
- Create: `src/Modules/AI/Contracts/PersonaPlex/PersonaPlexSessionRequest.cs`
- Test: `tests/DigitalBrain.AI.PersonaPlex.Tests/PersonaPlexAudioFrameTests.cs`

**Interfaces:**
- Produces `PersonaPlexAudioFrame.Create(sequence, pcm16)` and `IPersonaPlexSession.ProcessAsync` for all later tasks.

- [ ] **Step 1: Write a failing test for valid fixed-size PCM frames.**

```csharp
[Fact]
public void CreateAcceptsExactly1920Samples()
{
    var frame = PersonaPlexAudioFrame.Create(1, new short[1920]);
    Assert.Equal(1, frame.Sequence);
}
```

- [ ] **Step 2: Run the test and verify it fails because the contract does not exist.**

Run: `dotnet test tests/DigitalBrain.AI.PersonaPlex.Tests --filter FullyQualifiedName~PersonaPlexAudioFrameTests`

- [ ] **Step 3: Implement the immutable contracts and reject a non-1920-sample frame.**

```csharp
public static PersonaPlexAudioFrame Create(long sequence, ReadOnlyMemory<short> pcm16)
    => pcm16.Length == 1920
        ? new PersonaPlexAudioFrame(sequence, pcm16)
        : throw new ArgumentException("PersonaPlex frames require exactly 1920 PCM16 samples.", nameof(pcm16));
```

- [ ] **Step 4: Run the focused test and verify it passes.**

- [ ] **Step 5: Commit the contracts and their test.**

### Task 2: GPU runtime lifecycle and deterministic model validation

**Files:**
- Create: `src/Modules/AI/PersonaPlex/DigitalBrain.Modules.AI.PersonaPlex.csproj`
- Create: `src/Modules/AI/PersonaPlex/PersonaPlexOptions.cs`
- Create: `src/Modules/AI/PersonaPlex/PersonaPlexModelSet.cs`
- Create: `src/Modules/AI/PersonaPlex/PersonaPlexSessionFactory.cs`
- Create: `src/Modules/AI/PersonaPlex/PersonaPlexSession.cs`
- Create: `src/Modules/AI/PersonaPlex/PersonaPlexHosting.cs`
- Modify: `Directory.Packages.props`
- Modify: `DigitalBrain.slnx`
- Test: `tests/DigitalBrain.AI.PersonaPlex.Tests/PersonaPlexOptionsTests.cs`
- Test: `tests/DigitalBrain.AI.PersonaPlex.Tests/PersonaPlexSessionFactoryTests.cs`

**Interfaces:**
- Consumes Task 1 contracts.
- Produces DI registrations and an unavailable/ready factory for Kernel.

- [ ] **Step 1: Write a failing test that an enabled runtime rejects a missing four-graph model directory.**

```csharp
[Fact]
public void ValidateRejectsMissingTemporalAndDepformerGraphs()
{
    var options = new PersonaPlexOptions { Enabled = true, ModelDirectory = "missing" };
    Assert.Throws<InvalidOperationException>(() => options.Validate());
}
```

- [ ] **Step 2: Run the test and verify it fails because options do not exist.**

- [ ] **Step 3: Add the project and implement validation for encoder, temporal, depformer, and decoder graph paths.**

- [ ] **Step 4: Write a failing test that disabled configuration returns a failed/unavailable readiness without opening ORT sessions.**

- [ ] **Step 5: Implement model-set ownership, readiness states, singleton warm-up, and per-session reset. Wire CUDA I/O binding for temporal past/present values.**

- [ ] **Step 6: Run all PersonaPlex tests. Perform the gated manual GPU smoke command only when model artifacts and CUDA are configured.**

- [ ] **Step 7: Commit the runtime lifecycle work.**

### Task 3: AI module and Aspire configuration

**Files:**
- Create: `src/Modules/AI/Aspire.Hosting/PersonaPlexHostingExtensions.cs`
- Modify: `src/Modules/AI/AI/AIModule.cs`
- Modify: `src/Modules/AI/AI/DigitalBrain.Modules.AI.csproj`
- Modify: `src/Modules/AI/Aspire.Hosting/DigitalBrain.Modules.AI.Aspire.Hosting.csproj`
- Modify: `src/Aspire/DigitalBrain.AppHost/Program.cs`
- Modify: `tests/DigitalBrain.Aspire.Tests/NamesConformanceTests.cs`
- Test: `tests/DigitalBrain.Aspire.Tests/PersonaPlexHostingTests.cs`

**Interfaces:**
- Consumes `PersonaPlexOptions` and `PersonaPlexHosting.Add` from Task 2.
- Produces `WithPersonaPlex(Action<PersonaPlexHostOptions>)` and `DigitalBrain__AI__PersonaPlex__*` Kernel configuration.

- [ ] **Step 1: Write a failing model-rendering test for `DigitalBrain__AI__PersonaPlex__Enabled`.**

- [ ] **Step 2: Run it and verify AppHost does not yet render that environment key.**

- [ ] **Step 3: Implement a dedicated module projection that writes enabled, model-directory, CUDA-device, and max-session settings.**

- [ ] **Step 4: Register PersonaPlex hosting in `AIModule` without changing `VoiceToTextHosting`.**

- [ ] **Step 5: Run Aspire model tests and verify all existing names/topology tests pass.**

- [ ] **Step 6: Commit the hosting integration.**

### Task 4: Kernel WebSocket native-voice surface

**Files:**
- Create: `src/Kernel/DigitalBrain.Kernel/MapPersonaPlexVoice.cs`
- Create: `src/Kernel/DigitalBrain.Kernel/PersonaPlexVoiceProtocol.cs`
- Modify: `src/Kernel/DigitalBrain.Kernel/Program.cs`
- Modify: `src/Kernel/DigitalBrain.Kernel/HttpSurfacePaths.cs`
- Modify: `src/Kernel/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`
- Test: `tests/DigitalBrain.E2E.Tests/PersonaPlexVoiceTests.cs`

**Interfaces:**
- Consumes `IPersonaPlexSessionFactory` from Task 2.
- Produces `GET /voice/personaplex` WebSocket protocol with binary PCM frames and JSON status.

- [ ] **Step 1: Write a failing protocol test that rejects a binary frame whose payload is not 3,840 bytes.**

```csharp
[Fact]
public void DecodeAudioRejectsWrongPayloadLength()
    => Assert.Throws<InvalidDataException>(() => PersonaPlexVoiceProtocol.DecodeAudio(new byte[12]));
```

- [ ] **Step 2: Run it and verify the protocol type does not exist.**

- [ ] **Step 3: Implement fixed binary header encoding/decoding and JSON `start`, `status`, `error`, and `stop` control messages.**

- [ ] **Step 4: Write a failing WebSocket integration test using a fake session factory that sends one valid input frame and receives one valid output frame.**

- [ ] **Step 5: Implement connection-level session ownership, strict sequencing, bounded queues, cancellation, and non-sensitive errors. Map the route from `Program.cs`.**

- [ ] **Step 6: Run the focused E2E test and existing Kernel boot smoke tests.**

- [ ] **Step 7: Commit the Kernel surface.**

### Task 5: Flutter core voice transport and workspace tab

**Files:**
- Create: `src/Modules/UI/Flutter/core/lib/src/personaplex_voice_client.dart`
- Create: `src/Modules/UI/Flutter/core/lib/src/personaplex_voice_protocol.dart`
- Modify: `src/Modules/UI/Flutter/core/lib/digitalbrain_flutter.dart`
- Create: `src/Modules/UI/Flutter/shell/lib/voice/personaplex_voice_controller.dart`
- Create: `src/Modules/UI/Flutter/shell/lib/voice/personaplex_voice_screen.dart`
- Create: `src/Modules/UI/Flutter/shell/lib/voice/pcm_audio_output.dart`
- Modify: `src/Modules/UI/Flutter/shell/lib/chat/brain_workspace.dart`
- Modify: `src/Modules/UI/Flutter/shell/lib/chat/brain_chat_app.dart`
- Modify: `src/Modules/UI/Flutter/shell/pubspec.yaml`
- Test: `src/Modules/UI/Flutter/core/test/personaplex_voice_protocol_test.dart`
- Test: `src/Modules/UI/Flutter/shell/test/voice/personaplex_voice_controller_test.dart`

**Interfaces:**
- Consumes Task 4 wire protocol.
- Produces a new independent Voice destination using live PCM capture and playback.

- [ ] **Step 1: Write a failing Dart test that a decoded output packet with 1,920 samples preserves sequence and PCM bytes.**

- [ ] **Step 2: Run `flutter test` for that file and verify failure because the protocol does not exist.**

- [ ] **Step 3: Implement WebSocket client/protocol with same-origin URI conversion and lifecycle-safe close.**

- [ ] **Step 4: Write a failing controller test that stopping a session closes capture, playback, and socket exactly once.**

- [ ] **Step 5: Implement `record.startStream` PCM16 capture, explicit 24 kHz/mono config, PCM output abstraction, and unavailable/permission/error states.**

- [ ] **Step 6: Add Voice to `BrainWorkspace`'s persistent destination stack and render model readiness, mic level, speaker level, stop, and latency.**

- [ ] **Step 7: Run Flutter format, analyzer, focused tests, then the shell suite.**

- [ ] **Step 8: Commit the Flutter integration.**

### Task 6: Full verification and hardware acceptance

**Files:**
- Modify: `docs/research/2026-08-21-native-personaplex-v2v-runtime.md`
- Test: `tests/DigitalBrain.E2E.Tests/PersonaPlexVoiceTests.cs`

- [ ] **Step 1: Run .NET build and all non-hardware test projects.**

Run: `dotnet test DigitalBrain.slnx --no-restore`

- [ ] **Step 2: Run Flutter analyzer and tests.**

Run: `flutter analyze` and `flutter test`

- [ ] **Step 3: With configured gated models and CUDA hardware, run a sustained microphone-to-speaker smoke session and record p50/p95 frame latency, GPU memory, and zero transcription-service invocations.**

- [ ] **Step 4: Document actual hardware results or the precise external prerequisite that prevented the acceptance run.**

- [ ] **Step 5: Commit verification documentation and all feature changes.**
