# Native PersonaPlex voice-to-voice: current local-runtime research

Research date: 2026-08-21. This note investigates a strict native voice-to-voice path: live user audio must reach the model as audio tokens and the response must leave as generated audio, without an ASR/Whisper-to-text-to-LLM-to-TTS turn pipeline.

## Bottom line

**The goal is now achievable locally.** The previous conclusion that only Mimi codec ONNX graphs existed is out of date. There are two complete, transcription-free PersonaPlex runtimes:

1. NVIDIA's official Python/Moshi server runs the complete 7B model locally today. Its live path is `Opus audio -> Mimi encode -> 7B Moshi LM -> Mimi decode -> Opus audio`, not a Whisper pipeline. [NVIDIA server source](https://github.com/NVIDIA/personaplex/blob/main/moshi/moshi/server.py#L136-L309) shows the WebSocket receive, codec encode, `lm_gen.step`, codec decode, and concurrent send loop; [NVIDIA's README](https://github.com/NVIDIA/personaplex#usage) calls it real-time full-duplex speech-to-speech.
2. The ONNX Runtime project's current **Mobius** repository contains a full four-graph PersonaPlex ONNX implementation: Mimi encoder, 7B temporal transformer, depformer, and Mimi decoder. Its [example README](https://github.com/onnxruntime/mobius/tree/main/examples/personaplex) defines that exact graph split and a live microphone-to-speaker mode. The associated [merged work and implementation notes](https://github.com/onnxruntime/mobius/issues/369) explain the real-time design.

This means native V2V remains the right architecture. Do not put Whisper in the PersonaPlex conversation path.

## What “native” means here

PersonaPlex is a duplex speech model based on Moshi: it receives live user audio and autoregressively produces agent audio while the user continues speaking. The paper explicitly describes user audio arriving alongside the agent's audio and text streams, with text and audio generated together. [PersonaPlex paper](https://arxiv.org/abs/2602.06053)

That does **not** mean that no text tokens exist inside the model. The model has an optional/generated text side channel and accepts an optional text role prompt. It does mean there is no external ASR boundary between microphone input and the 7B model, and no external TTS boundary between the model and speaker output. NVIDIA's protocol accepts incoming binary audio (`kind == 1`); generated text (`kind == 2`) is a separate optional output. [NVIDIA server protocol](https://github.com/NVIDIA/personaplex/blob/main/moshi/moshi/server.py#L157-L244)

```text
24 kHz microphone PCM / Opus
  -> Mimi encoder
  -> PersonaPlex 7B temporal transformer + depformer
  -> Mimi decoder
  -> 24 kHz speaker PCM / Opus
```

## Verified runtimes

| Route | Full 7B backbone? | Native live V2V? | Windows/.NET relevance | Status |
| --- | --- | --- | --- | --- |
| NVIDIA PersonaPlex / Moshi | Yes: server loads `model.safetensors` through `get_moshi_lm`, rather than just Mimi. [source](https://github.com/NVIDIA/personaplex/blob/main/moshi/moshi/server.py#L1886-L1938) | Yes: encoder, LM step, decoder, and send/receive loops run concurrently. [source](https://github.com/NVIDIA/personaplex/blob/main/moshi/moshi/server.py#L204-L309) | A local Python sidecar can be consumed by a C# WebSocket/audio client; no C# runtime is supplied. | Best correctness baseline and shortest proof-of-functionality. |
| ONNX Runtime Mobius PersonaPlex example | Yes: four ONNX graphs include the 7B temporal graph and depformer. [README](https://github.com/onnxruntime/mobius/tree/main/examples/personaplex#personaplex--moshi--full-duplex-speech-to-speech-with-onnx-runtime) | Yes: it supplies `--stream`, `--mic`, and browser WebSocket modes. [README](https://github.com/onnxruntime/mobius/tree/main/examples/personaplex#3-offline--terminal-modes-no-browser) | Strongest path to an all-.NET local runtime because the serving graphs are ONNX. The supplied application is Python, so the streaming loop must be ported, not merely referenced. | Recommended product path. |
| `Codes4Fun/moshi.cpp` | Claims PersonaPlex support and provides a `personaplex` executable. [README](https://github.com/Codes4Fun/moshi.cpp#status) | Intended native microphone V2V; its Windows guide downloads quantized PersonaPlex weights and runs `personaplex`. [Windows instructions](https://github.com/Codes4Fun/moshi.cpp#personaplex-windows) | Usable Windows executable, but it is community C++/GGML with SDL/FFmpeg dependencies and no managed API. | Valuable Windows spike/fallback; not the cleanest DigitalBrain integration. |

## The ONNX discovery changes the earlier blocker

The official NVIDIA release itself is Python/PyTorch and packages safetensor weights, not a ready-to-consume ONNX package. Its declared dependencies include PyTorch and omit ONNX Runtime, while the model repository lists `model.safetensors` and tokenizer/voice artifacts. [NVIDIA project dependencies](https://raw.githubusercontent.com/NVIDIA/personaplex/main/moshi/pyproject.toml) and [official model files](https://huggingface.co/nvidia/personaplex-7b-v1/tree/main) support that distinction.

However, Mobius now exports and runs the missing middle, rather than passing Mimi codes through. Its four graphs are:

- Mimi encoder: PCM waveform to eight audio-codebook streams.
- Temporal transformer: the 7B Moshi core, returning hidden states, text logits, and KV cache.
- Depformer: 16 autoregressive substeps per frame for the codebook outputs.
- Mimi decoder: agent audio-codebook streams back to 24 kHz PCM.

This is documented by the [Mobius PersonaPlex README](https://github.com/onnxruntime/mobius/tree/main/examples/personaplex#personaplex--moshi--full-duplex-speech-to-speech-with-onnx-runtime). The 80 ms frame budget (12.5 Hz / 1,920 samples at 24 kHz) and reported CUDA performance are also documented there. CPU inference is explicitly reported as far too slow for real-time streaming; a CUDA GPU is required for the target experience. [same source](https://github.com/onnxruntime/mobius/tree/main/examples/personaplex#personaplex--moshi--full-duplex-speech-to-speech-with-onnx-runtime)

### Current ElBruno package audit

This distinction is particularly important for `ElBruno.PersonaPlex`. Its
Hugging Face repository now contains an `lm_backbone.onnx` external-data file,
but the library currently neither downloads it nor opens an inference session
for it. [`ModelManager.RequiredModelFiles`](https://github.com/elbruno/ElBruno.PersonaPlex/blob/main/src/ElBruno.PersonaPlex/ModelManager.cs#L10-L18)
names only the Mimi encoder and decoder. [`PersonaPlexPipeline.ProcessAsync`](https://github.com/elbruno/ElBruno.PersonaPlex/blob/main/src/ElBruno.PersonaPlex/Pipeline/PersonaPlexPipeline.cs#L139-L177)
explicitly labels the LM stage a placeholder and passes codes through unchanged.
It is consequently **not** a native conversational PersonaPlex runtime today,
regardless of its NuGet description or published backbone artifact.

### ONNX Runtime preview feeds do not fill the model gap

Microsoft's preview/release-candidate feeds can provide pre-release ONNX
Runtime binaries (including the `Microsoft.ML.OnnxRuntime.Gpu.Windows` NuGet
package), as shown in the official [ORT 1.26 release-candidate announcement](https://github.com/microsoft/onnxruntime/issues/28343).
They do not ship a PersonaPlex temporal/depformer export or a stateful C#
orchestrator. The relevant release choice is therefore a CUDA-capable ORT .NET
runtime—whose current installation guidance is documented by the [CUDA EP
page](https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html)—not
an alternative feed. DigitalBrain's checked-in [NuGet configuration](../../nuget.config)
currently maps all packages to NuGet.org; changing feeds is neither required
nor sufficient for native V2V.

## Why a direct C# port must retain IO binding

Do not implement the 7B temporal graph as ordinary per-frame `Run()` calls that copy its KV cache to CPU and back. The Mobius maintainers measured that approach growing from approximately 60 ms to more than 200 ms per frame as context lengthened—past the 80 ms deadline. Their solution retains the temporal KV cache on the GPU with ONNX Runtime I/O binding and reuses it as the next frame's `past.*` input; this holds the reported end-to-end path near 49 ms/frame. [Mobius implementation notes](https://github.com/onnxruntime/mobius/issues/369#3a-the-temporal-kv-cache-must-be-device-resident-ort-io-binding)

This is feasible from managed code: ONNX Runtime's C# API supplies `InferenceSession.CreateIoBinding()` and `RunWithBinding`, and its official API documentation describes binding preallocated GPU memory to avoid repeat copies. [C# `RunWithBinding` source](https://github.com/microsoft/onnxruntime/blob/main/csharp/src/Microsoft.ML.OnnxRuntime/InferenceSession.shared.cs) and [C# `OrtIoBinding` API](https://onnxruntime.ai/docs/api/csharp/api/Microsoft.ML.OnnxRuntime.OrtIoBinding.html)

Therefore the production .NET port should preserve Mobius's state model:

```text
capture 1,920 PCM samples (80 ms)
  -> ONNX Mimi encoder
  -> ONNX temporal (GPU-resident KV cache via OrtIoBinding)
  -> 16 × ONNX depformer steps
  -> ONNX Mimi decoder
  -> queue PCM for playback
```

Only the small per-frame audio/code/logit values should cross the host/device boundary as necessary for sampling. The temporal KV cache must remain device-resident.

## Recommended course for DigitalBrain

1. **Use the NVIDIA Python server as the truth baseline.** Run it locally against the publicly downloadable `nvidia/personaplex-7b-v1` weights, connect a temporary client, and verify real microphone interruption/overlap. The code is MIT; the weights use the NVIDIA Open Model License and require a Hugging Face account to accept those terms before download. This is an account/terms gate, not a paid or proprietary runtime licence. [NVIDIA repository](https://github.com/NVIDIA/personaplex) and [model card](https://huggingface.co/nvidia/personaplex-7b-v1)
2. **Treat Mobius as the native .NET implementation source.** Export the four graphs once using the documented Mobius command, then port `MoshiORT.reset_stream`, priming, `process_frame`, greedy sampling, and GPU I/O binding to C#. The Mobius server is already a useful protocol/behavior reference, but is Python—not a drop-in NuGet component. [example file roles](https://github.com/onnxruntime/mobius/tree/main/examples/personaplex#files)
3. **Keep native audio framing end-to-end.** Capture/play 24 kHz PCM locally; do not send the user audio to Whisper. A role text prompt is permitted for persona control, but it is not a user-transcription pipeline.
4. **Run only after a CUDA readiness check.** Mobius requires CUDA for real-time, and its published fastest configuration uses fp16 LM graphs with the codec in fp32. [build and server guidance](https://github.com/onnxruntime/mobius/tree/main/examples/personaplex#1-build-the-onnx-models-needs-mobius)

## Explicit non-recommendations

- Do not use a Mimi-only encoder/decoder package as if it were PersonaPlex. It cannot generate a conversational response because it lacks the temporal 7B model and depformer.
- Do not use CPU-only ONNX Runtime for the live route. Mobius documents about 1.8 seconds per frame on CPU versus an 80 ms real-time budget. [performance note](https://github.com/onnxruntime/mobius/tree/main/examples/personaplex#personaplex--moshi--full-duplex-speech-to-speech-with-onnx-runtime)
- Do not describe the product as “no text anywhere.” The model can internally generate text tokens and accepts a role prompt; the architectural requirement is no external transcription-driven turn pipeline.

## Evidence boundaries

This report verifies source availability and documented runtime behavior; it does not claim a successful run on the project's particular GPU or an existing maintained C# PersonaPlex package. The substantive .NET work is porting the stateful four-graph generation loop, especially the temporal KV-cache I/O binding and the depformer sampling loop.

## 2026-08-21 implementation verification

The following automated checks were run from this checkout on 2026-08-21:

| Check | Result |
| --- | --- |
| `dotnet test DigitalBrain.slnx --no-restore` and the minimal-console-logger variant | These invocations can produce Microsoft Testing Platform handshake failures (zero tests, exit code 5); that is a runner issue and is not the authoritative suite result. |
| `dotnet test DigitalBrain.slnx --no-restore -- --no-ansi --progress off` | Ran 74 real tests: 71 passed, 2 failed, 1 skipped. The failures were the pre-existing `DigitalBrain.E2E.Tests.McpSurfaceTests.TheFrozenMcpToolInvokesChatOverTheRealProtocol` (attributed by `git blame` to `daea795b`) and `DigitalBrain.E2E.Tests.PersonaPlexVoiceTests.RuntimeInvalidDataFailureIsUnavailableNotProtocolError`. The PersonaPlex close test passes in isolation, so its solution-wide failure is consistent with cross-assembly parallel resource/timing interference rather than a reproducible PersonaPlex failure. |
| `dotnet test tests/DigitalBrain.E2E.Tests/DigitalBrain.E2E.Tests.csproj --no-restore -- --no-ansi --progress off --parallel none` | Ran 23 tests: 21 passed, 1 failed, 1 skipped. The sole failure was the same pre-existing MCP surface test; the PersonaPlex close test passed with E2E parallelism disabled. |
| `dotnet test tests/DigitalBrain.AI.PersonaPlex.Tests/DigitalBrain.AI.PersonaPlex.Tests.csproj --no-restore --no-build` | Passed: 20 total, 20 succeeded, 0 failed, 0 skipped (957 ms). |
| `flutter analyze` in `src/Modules/UI/Flutter/shell` | Completed with one warning: unused import `chat_contracts.dart` in `lib/chat/workspace_session.dart:6`. |
| `flutter test` in `src/Modules/UI/Flutter/shell` | Passed: 28 tests. |

### Hardware acceptance status: blocked by model configuration

`nvidia-smi` identifies an NVIDIA GeForce RTX 5080 with driver 596.36 and 16,303 MiB of memory. That establishes that an NVIDIA GPU is visible, but it does not prove the enabled ONNX Runtime CUDA execution provider or the four-graph PersonaPlex runtime.

No `AppHost:PersonaPlex:ModelDirectory` / `DigitalBrain__AI__PersonaPlex__ModelDirectory` value was configured in this environment, and no exported four-graph model set was available. The required artifacts are `mimi_encoder/model.onnx`, `temporal/model.onnx`, `depformer/model.onnx`, and `mimi_decoder/model.onnx` (including every external-data sidecar required by those graphs). Consequently, no microphone-to-speaker session was started and no p50/p95 frame latency, GPU-memory usage during inference, or transcription-service invocation count was measured.

To unblock acceptance, download the public weights after accepting the NVIDIA Open Model License terms on Hugging Face, export or obtain the compatible four-graph artifact directory, configure it in AppHost together with `Enabled=true` and a valid CUDA device ID, and run the application with an ONNX Runtime CUDA provider compatible with the installed NVIDIA driver. Only then can a sustained 24 kHz microphone-to-speaker run produce the required latency, memory, and zero-transcription-service evidence.
