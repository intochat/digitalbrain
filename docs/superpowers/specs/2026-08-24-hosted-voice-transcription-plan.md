# Hosted voice transcription: OpenAI in prod, Foundry Local in dev

Date: 2026-08-24. Status: proposed. Depends on
[the AI model catalog redesign](2026-08-24-ai-model-catalog-redesign.md), which
replaces the catalog design sketched here — this is phase 5 of that plan.

## Problem

Voice is dead on the dev stand. Probed live:

```
POST /chats/main/voice -> 503
{"error":"Voice-to-text is not configured. Call AIModule.WithVoiceToText<T>() in AppHost."}
```

Setting `DigitalBrain__AI__Whisper__ModelId` would NOT fix it.
`FoundryLocalTranscriptionService` downloads and loads the model in `StartAsync`
(5-min download timeout, 2-min load, CUDA then CPU fallback). Whisper
large-v3-turbo is ~1.5 GB against a 0.5 vCPU / 1 GiB container, and
`Microsoft.AI.Foundry.Local` is a native Windows-oriented runtime — the kernel
csproj pins `RuntimeIdentifier win-x64` locally because of it. It would never
reach `IsReady` on Linux.

## What already works — do not rebuild it

The seam is in the right place. `IAudioTranscriptionService` exposes `IsReady`,
`InitializationFailed`, `ErrorMessage`, `ModelId`, and two `TranscribeAsync`
overloads; `MapChatVoice` already returns 503 when not ready, 415 for
non-multipart, 413 over 25 MB, and 422 on failure or empty text, then feeds the
text into the same durable `chat.send` path as typed input.

`VoiceToTextHosting.Add` already selects the implementation from configuration.
This plan adds a third branch to that switch. No endpoint, contract, or Flutter
change.

`VoiceUploadLimits.MaxBytes` is `25 * 1024 * 1024` — exactly OpenAI's own
request limit, ~13 min of 16 kHz mono WAV. Nothing to change.

## Decisions

| Decision | Choice |
| --- | --- |
| Model | `gpt-4o-mini-transcribe` (~$0.003/min, about half of `whisper-1`) |
| Config shape | Marker catalog, mirroring `Default:Model` / `Default:Embedding` |
| Local Whisper | Kept; OpenAI takes precedence when configured |
| Credential | Reuses `DigitalBrain:AI:OpenAI:ApiKey`. No new secret |

## Design

### Catalog — see the redesign

The first draft of this plan invented a `TranscriptionModel` catalog alongside
the existing `WhisperModel` one. That was the wrong shape: it would have made
five model kinds with five conventions.

The catalog now comes from
[the AI model catalog redesign](2026-08-24-ai-model-catalog-redesign.md), where
local Foundry and hosted OpenAI transcription are entries in ONE
`TranscriptionModel` catalog separated by `Provider`, `WhisperModel` is retired,
and markers are constrained to `ITranscription`. Phases 1-4 there land before
this work; what remains here is the service itself.
### New: `OpenAITranscriptionService`

`src/Modules/AI/AI/Voice/OpenAITranscriptionService.cs`, implementing
`IAudioTranscriptionService`. NOT an `IHostedService` — there is nothing to warm
up, which is the whole point.

- Resolves the marker name from `DigitalBrain:AI:Default:Transcription` and the
  key from `DigitalBrain:AI:OpenAI:ApiKey`.
- `IsReady` is true only when both resolve. Otherwise `InitializationFailed` is
  true and `ErrorMessage` names the missing key — so the endpoint answers 503
  with a useful message instead of the kernel refusing to boot over a voice
  misconfiguration. This mirrors how the Foundry service already swallows its
  own init failure.
- `AudioClient` built lazily behind a `Lazy<T>`, with the same `NetworkTimeout`
  the existing `OpenAICompatibleProviderFactory` uses; a 13-minute upload needs
  more than a default timeout.
- `TranscribeAsync(stream, fileName, ct)` delegates to
  `TranscribeAudioAsync(stream, fileName, options, ct)`. The SDK infers the
  format from the filename extension, and the shell always sends `voice.wav`;
  a filename arriving without a recognized extension is defaulted to `.wav`
  rather than passed through to a 400 from OpenAI.
- `ResponseFormat = Text`. Note `gpt-4o-*-transcribe` supports only `json` and
  `text` — the verbose/timestamp formats are `whisper-1`-only. Pinning Text
  keeps every catalogued model working through one code path.

### Changed: `VoiceToTextHosting.Add`

One new branch, ahead of the existing two:

```
Default:Transcription set  -> OpenAITranscriptionService
Whisper:ModelId set        -> FoundryLocalTranscriptionService   (unchanged)
neither                    -> UnavailableTranscriptionService    (unchanged)
```

Local dev is untouched: AppHost still calls
`ai.WithVoiceToText<IWhisperLargeV3Turbo>()`, which sets `Whisper:ModelId` and
never sets `Default:Transcription`. No AppHost change is needed, because prod
sets its env var directly — the same way `Default__Model` and
`Default__Embedding` are already set there.

### Tests

- Selection precedence: OpenAI when pinned; Foundry when only `Whisper:ModelId`;
  Unavailable when neither; OpenAI wins when both are set.
- `OpenAITranscriptionService`: unknown marker and missing API key each yield
  `IsReady == false` with an `ErrorMessage` naming the offending key; `ModelId`
  maps the marker to its wire id.
- No network test. The transcription call itself is covered by the manual
  rollout check below.

Open item: there is no AI-module unit test project today (`tests/` holds
Aspire, E2E, and Simulation). These are plain unit tests over config
resolution — recommend a small `tests/DigitalBrain.AI.Tests` rather than
bending an existing suite to fit.

## Rollout

1. Land the code; CI green.
2. Cut a release. The pipeline publishes the image and rolls the container app.
3. Turn it on — no new secret, one variable:

```bash
az containerapp update -n ca-digitalbrain-kernel -g intochat-rg \
  --set-env-vars DigitalBrain__AI__Default__Transcription=IGpt4oMiniTranscribe
```

4. Verify with a real recording, not a stub — a valid WAV is required, since a
   dummy body reaches OpenAI and returns 422:

```bash
curl -u "$USER:$PASS" -F "audio=@sample.wav" \
  https://<kernel-fqdn>/chats/main/voice
```

Expect `200` and a `chat-delta` SSE frame carrying the model's reply to the
transcribed text.

## Cost

~$0.003/min of audio. A 30-second voice note is about $0.0015. Negligible beside
the nano-tier chat spend.

## Unrelated risk to settle first

The shell hard-requires WAV and aborts with "WAV recording is not supported on
this device" when the encoder is missing. Browsers natively record
`audio/webm;codecs=opus`; `record_web` synthesizes WAV via PCM capture. If that
check fails in the deployed shell, voice never records and no transcription
backend matters.

This needs a browser test. If WAV turns out to be unavailable, the fix is to
stop insisting on it: OpenAI accepts `webm` and `ogg` directly, so the encoder
check could fall back to Opus and forward the container as-is. The local Foundry
path is the only reason WAV is mandatory, and `OggOpusToWavConverter` already
exists to bridge it.