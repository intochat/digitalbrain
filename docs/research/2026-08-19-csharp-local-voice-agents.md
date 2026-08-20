# C# local voice-to-voice AI-agent examples

Research date: 2026-08-19. Claims below link to the owning repository, author post, or Microsoft documentation.

## Best direct match: ElBruno.Realtime (Ollama)

[ElBruno.Realtime](https://github.com/elbruno/ElBruno.Realtime) is the strongest starting point: a .NET real-time audio-conversation framework with local VAD, Whisper STT, Ollama support, and local TTS adapters.

Its runnable [Scenario 04 console sample](https://github.com/elbruno/ElBruno.Realtime/tree/main/src/samples/scenario-04-realtime-console) implements exactly:

`Microphone -> Whisper STT -> Ollama (phi4-mini by default) -> QwenTTS -> speakers`

It automatically downloads the Whisper and QwenTTS models on first use; Ollama needs `phi4-mini` pulled locally. The sample has continuous microphone capture, silence detection, and looping turns. The project also offers local [Kokoro](https://github.com/elbruno/ElBruno.Realtime/tree/main/src) and VibeVoice TTS adapters. For an accompanying explanation and configuration examples, see the author's [blog post](https://elbruno.com/2026/03/02/%F0%9F%8E%99%EF%B8%8F%F0%9F%A4%96-real-time-ai-conversations-in-net-local-stt-tts-vad-and-llm-no-cloud-required/).

**Why choose it:** it is the only verified example here that is already a complete C# microphone-to-speaker loop with Ollama, not merely a collection of building blocks.

## Other useful Ollama/C# codebases

### AyazDuru.Samples.AI.Chat

[Repository](https://github.com/ayzdru/AyazDuru.Samples.AI.Chat) · [application setup](https://github.com/ayzdru/AyazDuru.Samples.AI.Chat/blob/main/source/AyazDuru.Samples.AI.Chat.Console/Program.cs) · [author blog post (Turkish)](https://www.ayazduru.com.tr/blog/post/2025/08/27/net-ile-yerel-yapay-zeka-ai-destekli-sesli-ve-yazili-sohbet-uygulamasi-ollama-speech-to-text-stt-text-to-speech-tts)

This is a smaller .NET/Aspire voice-and-text chat sample. Its console startup downloads Whisper, Silero VAD, and Kokoro, then registers an `OllamaApiClient` with local `llama3.2`. It is a good code-level reference for NAudio plus local models, though the repo is less polished than ElBruno.Realtime.

### Persona Engine

[Persona Engine](https://github.com/Arcevalis/persona-engine) is a more ambitious C# avatar/VTuber-style voice agent. It combines microphone capture, Silero VAD, Whisper.NET ASR, an OpenAI-compatible LLM endpoint (including Ollama), local Kokoro TTS, and speaker output. It is a better reference for an animated-character experience than a minimal agent, but its preferred setup requires an NVIDIA GPU. See its [README](https://github.com/Arcevalis/persona-engine#readme) and [installation guide](https://github.com/Arcevalis/persona-engine/blob/main/INSTALLATION.md).

## Foundry Local: verified C# building blocks, not yet an all-in-one voice agent

[Foundry Local](https://github.com/microsoft/foundry-local) provides local C# chat and speech-to-text models, including Whisper, and exposes in-process SDK and OpenAI-compatible local-server paths. Its public examples cover chat and local audio transcription, but not a full bundled TTS stage; pair it with a local TTS engine such as QwenTTS, Kokoro, or Piper for voice-to-voice output.

The best authoritative C# references are:

- [Official .NET Foundry Local samples](https://github.com/microsoft/Generative-AI-for-beginners-dotnet/blob/main/samples/CoreSamples/FOUNDRY-LOCAL-SAMPLES-README.md): local chat, a local agent-style chat with tools, audio transcription, and Windows-only live microphone transcription.
- [Official .NET blog: live speech-to-text with Foundry Local and C#](https://devblogs.microsoft.com/dotnet/foundry-local-live-speech-to-text-csharp/): explains the Windows sample using `Microsoft.AI.Foundry.Local.WinML`, NAudio, and `nemotron-speech-streaming-en-0.6b`; links the full source.
- [Video: .NET & AI Community Standup — Foundry Local live STT in C#](https://www.youtube.com/watch?v=3RTipcC1sl8): companion demo for that Microsoft blog post. It demonstrates local live transcription, not TTS.
- [Foundry Local Lab, Part 9](https://github.com/microsoft-foundry/Foundry-Local-Lab/blob/main/labs/part9-whisper-voice-transcription.md): a step-by-step C# `AudioClient`/Whisper example that runs fully on-device. It includes both file and streaming transcription APIs.

### Recommended Foundry composition

`microphone -> Foundry Local streaming ASR -> Foundry Local chat client or local /v1 endpoint -> local QwenTTS/Kokoro/Piper -> speaker`

Start from Microsoft's live-transcription sample, add the chat/agent sample, then use the TTS implementation from ElBruno.Realtime. This is an engineering recommendation based on the verified components above—not a claim that Microsoft ships this combined project.

## Local/offline caveat

All examples require an initial download of their runtime and model weights. Once the models are installed and the services bind to localhost, the inference path is local. External tools (web search, weather, and similar integrations) remain network-dependent if enabled.
