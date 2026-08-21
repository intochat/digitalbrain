# Native PersonaPlex voice-to-voice design

## Goal

Add a Windows-first, CUDA-backed PersonaPlex voice tab that exchanges live 24 kHz PCM audio with the local Kernel without using Whisper, external STT, `IChatClient`, or external TTS in its conversation path.

## Architecture

The new `DigitalBrain.Modules.AI.PersonaPlex` runtime owns ONNX Runtime GPU sessions and one resettable stateful stream per connected client. It runs the four Mobius PersonaPlex graphs: Mimi encoder, temporal transformer, depformer, and Mimi decoder. The temporal cache remains device-resident through `OrtIoBinding`.

Kernel exposes a separate WebSocket route, `/voice/personaplex`, for binary PCM frames and JSON control messages. It owns connection lifetime and readiness checks, but does not place frames on Orleans or in a journal. Flutter's new Voice destination captures PCM16 input, communicates over that WebSocket, and plays returned PCM through a dedicated output adapter.

The existing `/chats/{chatName}/voice` multipart route remains a Whisper-to-chat feature and is not modified.

## Constraints

- Input and output are 24,000 Hz, mono, signed 16-bit PCM in 1,920-sample (80 ms) frames.
- Native V2V has no automatic fallback to Whisper, `IChatClient`, or external TTS.
- Windows with an NVIDIA CUDA-capable GPU is required for the enabled feature.
- The PersonaPlex four-graph model artifacts live outside the repository and must be explicitly configured.
- The temporal KV cache is GPU-resident; ordinary CPU-copying `InferenceSession.Run` loops are prohibited for the temporal graph.
- Raw audio is neither logged nor journaled. Only operational metrics and readiness/error status are emitted.
- One WebSocket owns one PersonaPlex stream. A closed connection resets and disposes its stream state.

## Public contracts

`IPersonaPlexSessionFactory.CreateAsync(PersonaPlexSessionRequest, CancellationToken)` creates `IPersonaPlexSession`.

`IPersonaPlexSession.ProcessAsync(PersonaPlexAudioFrame, CancellationToken)` consumes exactly one input PCM frame and returns one output PCM frame. `ResetAsync` clears temporal and depformer state.

`PersonaPlexReadiness` reports `Disabled`, `Loading`, `Ready`, or `Failed`, with a non-sensitive message and current model configuration status.

## Wire protocol

The endpoint accepts an initial JSON `start` message, then binary PCM frame messages. Each binary frame starts with a fixed version, sequence, and sample count header followed by exactly 3,840 bytes of PCM16 payload. It emits the same binary shape for assistant audio and JSON `status` / `error` messages. Unsupported versions, invalid sequence order, sample count, or payload size close the session with a protocol error.

## Delivery gates

1. Target hardware runs the upstream Mobius reference with sustained real-time frame processing.
2. The C# runtime opens the complete four-graph model set and retains temporal state on the GPU through `OrtIoBinding`.
3. Flutter's Windows and web targets can capture and play continuous 24 kHz PCM without gaps.
4. Automated tests prove no code path uses `IAudioTranscriptionService` or `IChatClient` for a PersonaPlex session.
