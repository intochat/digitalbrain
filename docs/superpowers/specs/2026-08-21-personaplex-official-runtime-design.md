# Official PersonaPlex Runtime Design

## Goal

Make the existing Flutter Voice tab a genuinely local, full-duplex, native-audio
PersonaPlex experience. The user speaks 24 kHz mono PCM and receives generated
24 kHz mono PCM; no user audio is sent to Whisper, chat, or a cloud speech
service.

## Decision

Use NVIDIA's supported `moshi.server` runtime in a local GPU-enabled Linux
container. The direct C# ONNX implementation remains an experimental artifact
loader and must not be selected by the normal AppHost configuration: official
Hugging Face weights are not its required four ONNX graphs.

The Kernel keeps `/voice/personaplex` as the stable DigitalBrain protocol. A
small local adapter exposes a readiness endpoint and translates that protocol's
PCM frames to/from NVIDIA's supported streaming server protocol. This lets the
existing Flutter controller and its capture/playback code remain the product UI.

## Resource graph

```text
Hugging Face secret parameter -> PersonaPlex runtime container (HF_TOKEN)
                                 |  GPU + persistent model cache
                                 v
Flutter <-> Kernel /voice/personaplex <-> local PersonaPlex adapter
```

The HF token is injected only into the runtime container. It is not passed to
the Kernel, Flutter, structured logs, or error messages. The model cache is a
persistent local volume. Kernel waits for the runtime health endpoint, which
reports `downloading`, `loading`, `ready`, or `failed` without secret values.

## Runtime behavior

The container starts the official NVIDIA runtime with CUDA. If the first CUDA
load fails due to capacity, it retries with NVIDIA's `--cpu-offload` option and
reports that degraded mode in health and telemetry. It must never report Ready
until the model has loaded and one streaming session has warmed up.

The adapter owns any chunk-size conversion required by NVIDIA's stream; the
Flutter and Kernel public packet format remains 1,920 PCM16 samples (24 kHz
mono) per packet. Backpressure is bounded. Session stop resets only that
PersonaPlex stream.

## UX and observability

Flutter shows “Ready” only after the Kernel has connected to the ready runtime.
Actionable statuses distinguish missing token/access, model download/load,
CUDA/CPU-offload mode, and runtime unavailable. The upstream web UI is exposed
as an Aspire debugging endpoint, but the DigitalBrain Flutter tab is the
normal product surface.

Each readiness transition is a structured log. A voice-session trace records
only state/latency/capacity metadata, never audio, text content, prompts, or
secrets. Health probes and Aspire MCP can therefore prove `Ready` and the
absence of Whisper from this route.

## Acceptance criteria

1. `aspire start` prompts for the secret when absent and starts the runtime
   once it is present.
2. Aspire shows a healthy PersonaPlex resource before Kernel is considered
   voice-ready.
3. The Flutter tab can start, receive microphone permission, send PCM, and
   play PersonaPlex PCM output without a transcription request.
4. Tests cover secret wiring, readiness/error propagation, PCM-only relay, and
   session reset. A live acceptance run captures health, structured logs,
   traces, and a measured sustained audio session.
5. A 16 GB GPU may use CPU offload, but the final result reports observed
   throughput and latency; it is not labelled real-time if it cannot sustain
   conversational audio.
