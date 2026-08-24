# AI model catalog: one shape, marker-typed, conformance-enforced

Date: 2026-08-24. Status: proposed. Supersedes the catalog half of the hosted
voice transcription plan.

## Why now

Adding hosted transcription exposed that there is no single way to declare a
model. Four kinds exist and no two agree:

| | LLMModel | EmbeddingModel | WhisperModel | Image generation |
| --- | --- | --- | --- | --- |
| `Id` | yes | yes | yes | **raw config string** |
| Provider | `LlmProvider` enum | `LlmProvider` enum | **none** | **none** |
| `Marker` type | yes | yes | **none** | **none** |
| Marker base interface | `ILLM` | `IEmbedding` | **none** | **none** |
| `DisplayName` | **none** | **none** | yes | **none** |
| Generic base pinning marker | `LLMModel<TMarker>` | `EmbeddingModel<TMarker>` | **none** | **none** |
| Lookup by marker name | yes | yes | **string munging** | **none** |
| Capabilities | `SupportsTools` bool | **none** | `Priority` int | **none** |

Concrete costs of the drift:

1. **`WhisperModel.FindByMarker` munges strings.** It takes `IWhisperLargeV3Turbo`,
   strips a leading `I`, and string-matches the class name. Nothing checks the
   marker is a marker: `WithVoiceToText<TModel>() where TModel : class` accepts
   `string`.
2. **Image generation has no model concept at all** — `OpenAI:ImageModel`, a raw
   string defaulting to `"gpt-image-1"`, unvalidated.
3. **`EmbeddingModel` has no `Dimensions`**, while `AIClients` comments that
   switching the default embedding "changes vector dimensions and orphans every
   existing Qdrant collection." The invariant the comment protects is not modeled.
4. **`SupportsTools` as a lone bool doesn't scale.** Vision and structured output
   are already real distinctions between catalogued models.
5. **Catalogs are hand-maintained lists.** Adding a model file and forgetting the
   `All` entry compiles clean and fails at runtime.

## What to take from IAW, and what not to

IAW (`D:\IAW`, `src/Core/AI`) gets the *uniformity* right — `LLMModel`,
`EmbeddingModel`, and `WhisperModel` share one shape, every model is one small
file, and `ModelCapabilities` is a record rather than scattered bools. Its
`EmbeddingModel` carries `Dimensions`. Worth adopting.

Three things there are a step DOWN from what this repo already has, and must not
be copied:

- **`Provider` as a string** (`"openai"`). This repo's `LlmProvider` enum is
  strictly better — a typo is a compile error.
- **String `ServiceKey`** for DI (`$"{provider}-{normalizedId}"`). This repo keys
  services by marker `Type` and selects them with the generic attribute
  `[Llm<TModel>] : FromKeyedServicesAttribute(typeof(TModel))`. Compile-time
  beats normalized strings; keep it.
- **Runtime assembly scanning** with mutable static `Register(...)` and locks.
  It gives no stable ordering, and ordering here is semantic:
  `LLMModel.All` is documented as "cloud models precede local ones" because the
  default-model fallback picks the first provider with credentials.

### The assembly-scan objection is narrower than it looks

`WhisperModel` carries the comment: *"Explicit catalog — `Assembly.GetTypes()`
loads `FoundryLocalTranscriptionService` and then `Microsoft.AI.Foundry.Local`,
which AppHost must not pull."*

That is true only because `WhisperModel` lives in the **implementation** assembly
(`src/Modules/AI/AI/Voice/`) next to the Foundry service. `LLMModel` and
`EmbeddingModel` live in **Contracts**, which has no such dependency. Moving the
transcription catalog into Contracts removes the hazard entirely.

Even so, this plan keeps explicit ordered lists — not because scanning is unsafe
once the catalog moves, but because **order is semantic** and scanning discards
it. The duplication risk is closed by a conformance test instead, matching the
existing `NamesConformanceTests` / `TopologyConformanceTests` pattern.

## Target design

### One root, one shape

```csharp
public interface IAiMarker;
public interface ILLM : IAiMarker;
public interface IEmbedding : IAiMarker;
public interface ITranscription : IAiMarker;
public interface IImageModel : IAiMarker;

public enum AiProvider { OpenAI, Anthropic, Google, XAI, Ollama, FoundryLocal }

public abstract class AiModel
{
    public abstract string Id { get; }          // wire id, e.g. "gpt-5.4-nano"
    public abstract AiProvider Provider { get; }
    public abstract Type Marker { get; }

    // Display only. Never used for lookup - that is what Marker is for.
    public virtual string DisplayName => Marker.Name[1..];

    public bool IsLocal => Provider is AiProvider.Ollama or AiProvider.FoundryLocal;
}

public abstract class AiModel<TMarker> : AiModel
    where TMarker : IAiMarker
{
    public sealed override Type Marker => typeof(TMarker);
}
```

`LlmProvider` becomes `AiProvider` and gains `FoundryLocal`, so a locally hosted
model is describable in the same vocabulary as a cloud one.

### Per-kind bases carry only what that kind needs

```csharp
public abstract class LLMModel<TMarker> : AiModel<TMarker> where TMarker : ILLM
{
    public virtual LlmCapabilities Capabilities => LlmCapabilities.ToolCapable;
}

public abstract class EmbeddingModel<TMarker> : AiModel<TMarker> where TMarker : IEmbedding
{
    public abstract int Dimensions { get; }          // was missing entirely
}

public abstract class TranscriptionModel<TMarker> : AiModel<TMarker> where TMarker : ITranscription
{
    public abstract TranscriptionCapabilities Capabilities { get; }
}

public abstract class ImageModel<TMarker> : AiModel<TMarker> where TMarker : IImageModel;
```

The `where TMarker : ILLM` constraint is the type-safety win: a marker cannot be
attached to the wrong kind of model, and `WithVoiceToText<TModel>` becomes
`where TModel : ITranscription` instead of `where TModel : class`.

### Capabilities as records, not scattered bools

```csharp
public sealed record LlmCapabilities(bool Tools, bool Vision, bool StructuredOutput)
{
    public static readonly LlmCapabilities FullyCapable = new(true, true, true);
    public static readonly LlmCapabilities ToolCapable  = new(true, false, true);
    public static readonly LlmCapabilities ChatOnly     = new(false, false, false);
}

[Flags]
public enum TranscriptionFormats { Json = 1, Text = 2, Verbose = 4, Srt = 8, Vtt = 16 }

public sealed record TranscriptionCapabilities(TranscriptionFormats Formats)
{
    public bool SupportsTimestamps => Formats.HasFlag(TranscriptionFormats.Verbose);

    public static readonly TranscriptionCapabilities TextOnly  = new(TranscriptionFormats.Json | TranscriptionFormats.Text);
    public static readonly TranscriptionCapabilities Full      = new((TranscriptionFormats)31);
}
```

This is not decoration. `gpt-4o-mini-transcribe` and `gpt-4o-transcribe` accept
only `json` and `text`; the verbose and timestamp formats are `whisper-1`-only.
Modelling it means the transcription service can assert the format it wants is
supported instead of discovering it as a 400 from OpenAI at runtime.

`SupportsTools` on `LLMModel` folds into `LlmCapabilities.Tools`. The existing
"models that cannot emit tool calls must never be told about tools" pipeline
step in `AIClients.BuildChatPipeline` reads the record instead of the bool.

### Transcription: one catalog, local and hosted together

`WhisperModel` is retired. Local Foundry models and hosted OpenAI models become
entries in the same `TranscriptionModel` catalog, distinguished by `Provider`:

```csharp
// Contracts/OpenAI/Gpt4oMiniTranscribe.cs
public sealed class Gpt4oMiniTranscribe : TranscriptionModel<IGpt4oMiniTranscribe>
{
    public override string Id => "gpt-4o-mini-transcribe";
    public override AiProvider Provider => AiProvider.OpenAI;
    public override TranscriptionCapabilities Capabilities => TranscriptionCapabilities.TextOnly;
}
public interface IGpt4oMiniTranscribe : ITranscription;

// Contracts/FoundryLocal/WhisperLargeV3Turbo.cs
public sealed class WhisperLargeV3Turbo : TranscriptionModel<IWhisperLargeV3Turbo>
{
    public override string Id => "whisper-large-v3-turbo";
    public override AiProvider Provider => AiProvider.FoundryLocal;
    public override TranscriptionCapabilities Capabilities => TranscriptionCapabilities.Full;
}
public interface IWhisperLargeV3Turbo : ITranscription;
```

`VoiceToTextHosting.Add` then selects on `model.Provider` — `FoundryLocal` binds
the Foundry service, anything else binds the hosted OpenAI one. The `name[1..]`
munging and the separate `Whisper:ModelId` key both disappear; one key,
`DigitalBrain:AI:Default:Transcription`, names a marker exactly like
`Default:Model` and `Default:Embedding` already do.

`WhisperModel.Priority` has no successor. It ordered local-model preference; the
explicit catalog order already expresses that, as it does for `LLMModel`.

### Image generation joins the scheme

`IGptImage1 : IImageModel` with a catalogued `GptImage1`, replacing the
unvalidated `OpenAI:ImageModel` string and its hardcoded `"gpt-image-1"` default.

### Population: explicit order, conformance-enforced

Catalogs stay explicit ordered lists in Contracts:

```csharp
public static IReadOnlyList<TranscriptionModel> All { get; } =
[
    new OpenAI.Gpt4oMiniTranscribe(),
    new OpenAI.Gpt4oTranscribe(),
    new OpenAI.Whisper1(),
    new FoundryLocal.WhisperLargeV3Turbo(),
    new FoundryLocal.WhisperSmall(),
    new FoundryLocal.WhisperTiny(),
];
```

and a conformance test closes the "forgot to add it" gap, reflecting over the
**Contracts** assembly only — which has no native dependencies, so the original
scanning hazard does not apply:

- every non-abstract `AiModel` subclass in Contracts appears in its kind's `All`;
- no duplicate `Id` within a kind;
- no duplicate `Marker` across all kinds;
- every marker implements exactly one of the four kind interfaces;
- every `Marker` is an interface, not a class.

This buys IAW's "just add a file" ergonomics with no runtime reflection, stable
ordering, and a failure that lands in CI rather than in prod.

### Config resolution, uniform and fail-loud

One resolver replaces three near-copies of the same lookup:

```csharp
public static TModel RequireByMarkerName<TModel>(
    IReadOnlyList<TModel> catalog, string configKey, string markerName) where TModel : AiModel
```

producing one error shape that lists the valid marker names — the behaviour
`AIClients.UnknownMarker` already has, applied to every kind including the two
that currently have no validation at all.

Keep the existing split on failure handling: chat and embedding fail fast at
resolution, transcription degrades to `IsReady == false` so a voice
misconfiguration answers 503 rather than refusing to boot the silo.

## Phasing

Each phase is independently shippable and CI-green.

1. **Foundation.** `IAiMarker`, `AiProvider` (rename + `FoundryLocal`), `AiModel`,
   `AiModel<TMarker>`. Reparent `LLMModel`/`EmbeddingModel`. Pure refactor, no
   behaviour change.
2. **Conformance test.** Add it against the current catalogs before growing them.
3. **Capabilities + dimensions.** `LlmCapabilities` replaces `SupportsTools`;
   `Dimensions` added to embedding models.
4. **Transcription unification.** Retire `WhisperModel`, move the catalog into
   Contracts, switch `WithVoiceToText` to `where TModel : ITranscription`,
   collapse `Whisper:ModelId` into `Default:Transcription`.
5. **Hosted transcription.** `OpenAITranscriptionService` — now a small addition,
   selected by `Provider`, with the format assertion the capabilities record makes
   possible. This is the original voice plan, shrunk by everything above.
6. **Image model catalog.** Lowest value; fold in when convenient.

Phases 1-3 touch every model file but change no behaviour, so they are reviewable
as mechanical diffs. Phase 4 is the only one with a config migration:
`DigitalBrain__AI__Whisper__ModelId` gives way to
`DigitalBrain__AI__Default__Transcription`. Nothing sets the old key in prod
today, and AppHost sets it locally, so the migration is a one-line AppHost change
plus the new env var on the container app.

## What this does not do

- No source generator. It would remove the conformance test, but adds build
  tooling for a catalog that changes a few times a year.
- No runtime `Register(...)`. Models are compile-time facts here; IAW's mutable
  static registry buys dynamism nothing in this repo asks for.
- No string `ServiceKey`. Marker `Type` keys stay.