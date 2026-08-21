# Official PersonaPlex Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable a locally hosted official PersonaPlex runtime from Aspire and connect the existing Flutter native-audio tab to it.

**Architecture:** A pinned NVIDIA/Moshi runtime runs in a GPU-enabled container with a secret HF token and a persistent cache. A local adapter accepts DigitalBrain PCM frames, speaks the NVIDIA stream protocol, and exposes safe health/readiness. Kernel preserves its public voice WebSocket and delegates inference to that adapter.

**Tech Stack:** .NET Aspire 13.5, ASP.NET Core WebSockets/OpenTelemetry, Docker NVIDIA GPU runtime, Python 3.12/Moshi, Flutter PCM16.

**Spec:** `docs/superpowers/specs/2026-08-21-personaplex-official-runtime-design.md`

## Global Constraints

- Do not create a git worktree; execute on the user-authorized `finalv2` branch.
- Never read, print, or log the Hugging Face token.
- The voice route remains direct local PCM and must not call Whisper/chat.
- Pin upstream runtime source and container dependencies.

---

### Task 1: Create the local runtime image and safe readiness API

**Files:**
- Create: `src/Runtime/PersonaPlex/Dockerfile`
- Create: `src/Runtime/PersonaPlex/entrypoint.py`
- Create: `src/Runtime/PersonaPlex/requirements.lock`
- Test: `tests/DigitalBrain.PersonaPlex.Runtime.Tests/*`

**Produces:** a local container listening on a health endpoint and a private
streaming endpoint, using `HF_TOKEN` only within the container.

- [ ] Write failing health/readiness and no-secret-leak tests.
- [ ] Implement the pinned NVIDIA/Moshi image, cache location, model warm-up,
  readiness states, CPU-offload fallback, and protocol adapter.
- [ ] Run the runtime tests and build the image.
- [ ] Commit the runtime image and tests.

### Task 2: Wire the runtime into Aspire and Kernel configuration

**Files:**
- Modify: `src/Aspire/DigitalBrain.AppHost/AppHost.cs`
- Modify: `src/Modules/AI/Aspire.Hosting/PersonaPlexHostingExtensions.cs`
- Modify: `src/Modules/AI/PersonaPlex/PersonaPlexOptions.cs`
- Test: `tests/DigitalBrain.Aspire.Tests/PersonaPlexHostingTests.cs`

**Produces:** a named Aspire runtime resource with a secret reference, cache,
GPU allocation, health check, and Kernel dependency.

- [ ] Write failing resource-model tests for token-only runtime injection and
  Kernel runtime endpoint configuration.
- [ ] Implement the resource graph and replace the default false ONNX setting
  with runtime-based readiness configuration.
- [ ] Run Aspire tests and inspect the generated resource model.
- [ ] Commit the Aspire wiring and tests.

### Task 3: Relay native sessions from Kernel to the local adapter

**Files:**
- Create: `src/Modules/AI/PersonaPlex/RemotePersonaPlexSessionFactory.cs`
- Create: `src/Modules/AI/PersonaPlex/RemotePersonaPlexSession.cs`
- Modify: `src/Modules/AI/PersonaPlex/PersonaPlexHosting.cs`
- Modify: `src/Kernel/DigitalBrain.Kernel/MapPersonaPlexVoice.cs`
- Test: `tests/DigitalBrain.AI.PersonaPlex.Tests/*`
- Test: `tests/DigitalBrain.E2E.Tests/PersonaPlexVoiceTests.cs`

**Produces:** the unchanged public WebSocket endpoint delegates PCM frames to
the local runtime and returns its PCM output with bounded backpressure.

- [ ] Write failing tests for ready relay, unavailable readiness, reset, and
  no-transcription path.
- [ ] Implement the remote session factory/session and safe status mapping.
- [ ] Run module and E2E protocol tests.
- [ ] Commit the Kernel relay and tests.

### Task 4: Present real readiness in Flutter and validate live telemetry

**Files:**
- Modify: `src/Modules/UI/Flutter/shell/lib/voice/personaplex_voice_controller.dart`
- Modify: `src/Modules/UI/Flutter/shell/lib/voice/personaplex_voice_screen.dart`
- Test: `src/Modules/UI/Flutter/shell/test/voice/*`
- Modify: `docs/research/2026-08-21-native-personaplex-v2v-runtime.md`

**Produces:** a talk-ready Voice tab only when live runtime readiness succeeds,
plus live Aspire MCP telemetry evidence.

- [ ] Write failing UI tests for ready/degraded/unavailable states.
- [ ] Implement the readiness presentation without changing PCM capture/playback.
- [ ] Run Flutter tests; rebuild changed Aspire resources; use Aspire MCP to
  verify health, logs, and traces; perform a microphone-to-speaker session.
- [ ] Commit UI/docs and report observed latency/throughput.
