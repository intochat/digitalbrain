# Media neurons

`IImageGenerator`, `IEmbeddingModel`, `ISpeechRecognizer`, and `ISpeechSynthesizer`
are separate callable neurons. They use the configured default image, embedding,
and transcription services; they do not execute tools. Requests accept Orleans
cancellation tokens and publish typed completion or failure signals. These live
signals are not a durable event log.

Stage one uses serialized inline bytes, capped at 8 MiB per result or audio input.
There is no shared artifact storage service in this layer, so returning an opaque
artifact identifier would produce a reference that callers could not resolve.
No remote URLs or local filesystem paths are accepted. Large media should move to
artifact storage when that service exists. Legacy providers can buffer output
before this boundary rejects an oversized response; this is a wire payload limit,
not a provider memory allocation limit. Text inputs are capped at 32768 characters;
embedding batches contain at most 128 inputs.

`AddMediaNeurons()` registers the default unavailable speech transport. To enable
the real OpenAI adapter, register `AddOpenAISpeechSynthesis("gpt-4o-mini-tts")` and
configure `AIOptions.OpenAI.ApiKey` (and optionally `Endpoint`). The adapter returns
MP3 bytes. A host can instead register `ISpeechSynthesisTransport` for another
provider or deterministic tests. Availability means configured, not a network
health check. No external provider call is made by the default test suite.

SDK mapping follows the installed OpenAI 2.12 audio API and the official
[speech guide](https://developers.openai.com/api/docs/guides/text-to-speech).