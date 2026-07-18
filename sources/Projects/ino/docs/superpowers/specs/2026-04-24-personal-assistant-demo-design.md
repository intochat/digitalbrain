# Personal assistant demo — design spec (Spec A)

**Date.** 2026-04-24
**Track.** Opensource-readiness roadmap, revised ordering (2 → 3 → 4 → 7). This spec ships the Track-3 Cortex catalog work bundled with the smallest set of additions that makes ino usable as a real personal assistant on master — so the user can `git clone && aspire start` and talk to it.
**Status.** Design pending user approval.
**Follow-up.** Spec B — per-synapse-scoped memory via telemetry (separate doc, shipped after Spec A is green).
**Related.**
- `docs/superpowers/specs/2026-04-23-domain-experience-vocabulary-design.md` — Track 1; locked the `IExperience` contract this spec routes to.
- `docs/product-vision-final.md` — Decision 6 (Cortex), Decision 8 (two vocabularies).
- `POC/src/Ino.Core.Hosting/Llm/BddMockChatClient.cs` — the deterministic test-side `IChatClient` that stays as-is.

---

## Mission

One week of focused work ending with this demo:

> `git clone https://github.com/…/ino && export XAI_API_KEY=… && aspire start` →
> Flutter web opens → user presses the mic button, says "find flights to Bali" → Web Speech API transcribes in-browser → `IInoGateway.ChatAsync` fires → Cortex classifies the prompt against the live experience catalog (no hardcoded keyword branches) → fires `FindFlightsRequest` → `FlightSearchNeuron` replies with a real Grok-generated copy + seeded flight fixtures → a flight card renders.

The same loop works for hotels, places, trip planning, flight monitoring, and the one Taxi scenario we add. All powered by **xAI Grok** through a single tier-aware `IChatClient` factory. No memory, no ino:cloud, no IAW dependency. One-user, local, but real.

---

## Out of scope

- **Memory primitive.** Per-synapse-scoped memory via telemetry is Spec B. Cortex reasons over the prompt + catalog only; it does not recall prior turns.
- **ino:cloud marketplace + cross-device sync.** Track 4. Marketplace stays file-backed local.
- **IAW integration.** Dropped after evaluating `IAW.Core.Agent` — it's a full agent runtime (`DurableGrain` + Qdrant + chat history + its own cluster), not a base class, and inheriting it would violate CLAUDE.md's "neurons do not require an LLM" invariant. ino may talk to IAW as a peer service later if its specialist agents earn their keep.
- **Track 2 (opensource clone polish).** Per-clone `clusterId`, first-run onboarding, secrets helpers — still on the list but not required for the single-developer demo this spec targets. Called out in the verification checklist.
- **Server-side Whisper / speech backend.** Web Speech API carries the demo. A Whisper.cpp sidecar is a later slice.
- **Text-to-speech.** Listening only; responses stay visual.
- **Reasoning-tier neurons beyond `ItineraryComposer`.** More reasoning users earn the tier when they appear.
- **Embedding models.** No memory → no embeddings.
- **Taxi beyond one scenario.** Real Uber-MCP integration is not in v1; Taxi remains scaffold with one BDD-mock-friendly `.feature`.

---

## Demo script (the thing this spec has to deliver)

1. Fresh clone. Developer sets `XAI_API_KEY`. `aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated`.
2. Aspire dashboard shows `system`, `identity`, `domains` Healthy.
3. Developer opens the `system-http` endpoint → Flutter loads.
4. Developer clicks the mic button in the chat input. Browser prompts for mic permission once, remembers.
5. Developer says "find flights to Bali". Web Speech API streams interim transcripts into the input. Silence for 1.2s auto-submits.
6. Gateway `ChatAsync` fires. Cortex (Fast tier) classifies against the catalog → matches `travel.find-flights` → fires `FindFlightsRequest`.
7. `FlightSearchNeuron` (Balanced tier) generates a one-line user-facing copy ("Found 3 morning flights to Denpasar, cheapest $412 …") from Grok + the seeded fixture list.
8. `SynapseFired` envelope hits the Flutter client via gRPC-Web. A flight card renders with the copy + fixture data.
9. Developer types "plan 5 days in Tokyo". Same path; Cortex matches `travel.plan-trip`; `ItineraryComposer` runs (Reasoning tier) and fans out to FlightSearch + HotelSearch + PlaceSearch.

The "is ino working as my personal assistant" success criterion = **this flow runs on one dev machine with zero manual wiring beyond `XAI_API_KEY`**.

---

## Architecture overview

```
┌─────────────────────────────────────────────────────────────┐
│ Flutter web (CanvasKit)                                     │
│  ├─ Mic button → Web Speech API (window.SpeechRecognition)  │
│  └─ gRPC-Web client → IInoGateway.ChatAsync                 │
└──────────────────────────────┬──────────────────────────────┘
                               │ (system-http)
┌──────────────────────────────▼──────────────────────────────┐
│ Ino.System.Host (system silo)                               │
│  ├─ Ino.Gateway.Grpc — ChatAsync entry                      │
│  ├─ CortexNeuron ───→ IChatClientFactory.ForTier(Fast)      │
│  │                     │                                    │
│  │                     ▼                                    │
│  │   ┌───────── Ino.Llm.Xai ──────────┐                     │
│  │   │ TieredChatClientFactory         │                    │
│  │   │  ├─ Fast     → Grok4FastNoRsn   │──→ api.x.ai/v1     │
│  │   │  ├─ Balanced → Grok4FastRsn     │     (OpenAI-       │
│  │   │  └─ Reasoning → Grok420         │      compatible)   │
│  │   └──────────────────────────────── ┘                    │
│  └─ Discovery / Firing / SynapseFired broadcast             │
└──────────────────────────────┬──────────────────────────────┘
                               │ (Orleans cluster, silo-silo)
┌──────────────────────────────▼──────────────────────────────┐
│ Ino.Domains.Host (domains silo)                             │
│  Travel neurons ─── IChatClientFactory.ForTier(Balanced)    │
│  Taxi scaffold   ── same                                    │
│  ItineraryCompsr ── IChatClientFactory.ForTier(Reasoning)   │
└─────────────────────────────────────────────────────────────┘
```

Two things are new; everything else is either unchanged or a small refactor.

**New:**
- `Ino.Llm.Xai` — provider adapter project. Hosts the xAI-flavoured `IChatClient`, the slim model catalog, and the tier-aware factory.
- Web Speech API hook in Flutter.

**Refactored, not rewritten:**
- `CortexNeuron` — swaps hardcoded keyword branches for catalog-driven routing with a fast-path regex and an LLM classifier fallback.
- Host DI in each silo — adds `AddInoChatClients(config)` that wires `IChatClientFactory`; tests override it with a BDD-mock-backed factory.
- `LlmTier` enum — values change from `{None, Default, Reasoning, Multimodal}` to `{None, Fast, Balanced, Reasoning}`. `Default` callsites map to `Balanced`.

---

## AppHost surface — the thing the user sees

Target shape of `POC/src/Ino.AppHost/Program.cs` after this spec:

```csharp
using Ino.Core;
using Ino.Aspire.Hosting;
using Ino.Llm.Xai.Models;

var builder = DistributedApplication.CreateBuilder(args);

// Pick a provider by uncommenting. Default: xAI (all three tiers).
// API key via XAI_API_KEY env var. Fails loudly if missing.
builder.AddIno("ino")
    .WithLlm<Grok4FastNonReasoning>().AsFast()
    .WithLlm<Grok4FastReasoning>().AsBalanced()
    .WithLlm<Grok420>().AsReasoning()
    .WithVoiceToText<WebSpeechApi>();

builder.AddProject<Projects.Ino_System_Host>(KernelSilo.System.ToResourceName())
    .WithHttpsEndpoint(name: "system-http");
builder.AddProject<Projects.Ino_Identity_Host>(KernelSilo.Identity.ToResourceName());
builder.AddProject<Projects.Ino_Domains_Host>(KernelSilo.Domains.ToResourceName());

builder.Build().Run();
```

The tier fluent calls register declarative model descriptors on the `IInoBuilder`; they do **not** start containers or contact xAI at AppHost-graph-build time. Silos consume the descriptor list at startup via `IConfiguration` (serialized model list) and construct their `IChatClientFactory` locally.

---

## Component specs

### 1. `Ino.Core` — tier enum update

```csharp
public enum LlmTier
{
    None,
    Fast,
    Balanced,
    Reasoning,
}
```

**Breaking change:** `Default` → `Balanced` and `Multimodal` removed. Two call sites: `Travel.cs` and `Taxi.cs` declare `Capability.Llm(LlmTier.Default)` today; both update to `Balanced`. `Multimodal` has no live consumers. Tests that reference `LlmTier.Default` update 1:1.

This happens in one early commit; every subsequent commit is on the new enum.

### 2. `Ino.Llm.Xai` — new project

Single provider package. One csproj, a handful of classes.

**csproj deps:**
- `Microsoft.Extensions.AI` — the `IChatClient` abstraction (already in the packages.props).
- `OpenAI` — the OpenAI-compatible SDK (xAI uses the OpenAI protocol).
- `Microsoft.Extensions.AI.OpenAI` — bridges `OpenAIClient` → `IChatClient`.
- `Microsoft.Extensions.Http.Resilience` — already pinned; used for retry.

**Model descriptor base (in `Ino.Core.Hosting`, not the Xai project, so alternative providers later don't collide):**

```csharp
public abstract class LlmModel
{
    public abstract string Id { get; }          // e.g. "grok-4-1-fast-reasoning"
    public abstract string DisplayName { get; }
    public abstract string Provider { get; }    // "xai"
    public abstract LlmTier DefaultTier { get; }
}
```

**xAI model catalog (`Ino.Llm.Xai.Models`):**

```csharp
public sealed class Grok4FastNonReasoning : LlmModel
{
    public override string Id => "grok-4-1-fast-non-reasoning";
    public override string DisplayName => "Grok 4.1 Fast (no reasoning)";
    public override string Provider => "xai";
    public override LlmTier DefaultTier => LlmTier.Fast;
}

public sealed class Grok4FastReasoning : LlmModel
{
    public override string Id => "grok-4-1-fast-reasoning";
    public override string DisplayName => "Grok 4.1 Fast (reasoning)";
    public override string Provider => "xai";
    public override LlmTier DefaultTier => LlmTier.Balanced;
}

public sealed class Grok420 : LlmModel
{
    public override string Id => "grok-4.20";
    public override string DisplayName => "Grok 4.20 (flagship)";
    public override string Provider => "xai";
    public override LlmTier DefaultTier => LlmTier.Reasoning;
}
```

Exact model-ID strings (`grok-4-1-fast-reasoning` vs `grok-4.1-fast-reasoning`) MUST be confirmed against `https://console.x.ai/team/default/models` or a live 200 from the API during implementation — sources diverge on dash-vs-dot formatting and the spec must not guess in code.

### 3. `IChatClientFactory` — tier-aware resolution

```csharp
public interface IChatClientFactory
{
    IChatClient ForTier(LlmTier tier);
    IReadOnlyList<LlmModel> RegisteredModels { get; }
}
```

**Production implementation (`XaiChatClientFactory`)** — one `OpenAIClient` pointed at `https://api.x.ai/v1`, one `IChatClient` per registered model (lazy), tier → model resolution by the `As{Tier}` binding the AppHost declared, with fallback rule: if tier T is unbound, use the highest-bound tier ≤ T (Reasoning > Balanced > Fast). `None` → throws; callers must not ask for it.

Missing `XAI_API_KEY`: host startup fails with a clear error `"XAI_API_KEY not set. Either set it in the environment or edit POC/src/Ino.AppHost/Program.cs to uncomment a different provider."` No silent fallback.

### 4. Silo DI wiring

New extension in `Ino.Core.Hosting`:

```csharp
public static IHostApplicationBuilder AddInoChatClients(
    this IHostApplicationBuilder builder,
    IConfiguration config);
```

Reads the model list from config (serialized by AppHost via `WithEnvironment` into the silo processes), constructs `XaiChatClientFactory`, registers it as a singleton `IChatClientFactory`, and registers `IChatClient` as keyed singletons per tier (so neurons that just want "the balanced chat client" can `[FromKeyedServices(LlmTier.Balanced)] IChatClient`).

Called from `Ino.System.Host/Program.cs`, `Ino.Identity.Host/Program.cs`, `Ino.Domains.Host/Program.cs`.

**Test mode.** `Ino.Testing` replaces the registration with a `BddMockChatClient`-backed factory — same `IChatClientFactory` interface, same tier keys, deterministic. Spec A adds **no new test infrastructure** — it reuses the existing `BddMockChatClient` + `IReasoningProbe` + `BddScenario` set. Neurons don't know whether they got xAI or the mock.

### 5. AppHost → silo config propagation

`InoBuilder` grows a `DeclaredModels : IReadOnlyList<LlmModel>` surface plus a `DeclaredVoiceProvider` marker. At `builder.Build()` time, the serialized form (provider string + model id + tier binding) is pushed to every ino silo's environment as `Ino__Llm__Models__0__Id` etc. Silos deserialize on startup inside `AddInoChatClients`.

This uses **only** Aspire's `WithEnvironment` — no new secrets mechanism. The `XAI_API_KEY` is read from the silo process environment (developer sets it once at shell level, Aspire propagates when `INO_TEST_MODE` is not set).

### 6. `CortexNeuron` refactor — catalog-driven hybrid routing

Current file: `POC/src/Ino.System/CortexNeuron.cs`, 3 hardcoded keyword branches. Target shape:

```csharp
[PinToSilo("system")]
public sealed class CortexNeuron(
    IDiscoveryClient discovery,
    IFirePort firePort,
    IChatClientFactory llm,
    IReasoningProbe probe,
    ILogger<CortexNeuron> log) : Grain, INeuron<ChatIntent>
{
    public async Task<NeuronResult> HandleAsync(ChatIntent synapse, NeuronContext ctx, CancellationToken ct)
    {
        var liveCtx = ctx with { FirePort = firePort, Logger = log };
        var catalog = await discovery.DumpExperiencesAsync(ct);

        // Step 1 — regex fast-path against PromptExamples. O(n) for n experiences;
        // installed v0.1 catalog is ≤10 so cheap. Confident match → route.
        if (TryRegexMatch(catalog, synapse.Text) is { } confident)
            return await Fire(confident, synapse, liveCtx, ct);

        // Step 2 — LLM classifier. Fast tier, structured-output constrained prompt
        // listing experience ids + descriptions. Returns null when none fits.
        if (await ClassifyWithLlmAsync(catalog, synapse, ct) is { } chosen)
            return await Fire(chosen, synapse, liveCtx, ct);

        return await EmitUnroutedAsync(synapse, liveCtx, ct);
    }
}
```

**Fast-path details.**
- Regex = `PromptExamples` elements joined as an alternation, applied case-insensitively. Each experience contributes one alternation group; Discovery caches the compiled pattern and invalidates on install/uninstall.
- "Confident" = exactly one experience matched. Ambiguous (two experiences match the same utterance) falls through to LLM. Zero matches falls through to LLM.

**LLM classifier details.**
- System prompt is assembled from the catalog: `"You are ino's intent router. Given a user utterance, return exactly one experience_id from this list or 'none'. Experiences: travel.find-flights: Search flights…, taxi.find-ride: Hail a ride…, …"`.
- Uses `ChatOptions.ResponseFormat = ChatResponseFormat.Json` with a schema `{experience_id: enum(ids, 'none')}`. If the model returns an id not in the catalog (hallucination), treat as `none`.
- `IReasoningProbe` records the match: source=`cortex-llm`, scenario=chosen experience id, prompt=user utterance. The inspector's Reasoning panel lights up as today.
- Uses `LlmTier.Fast` — classification is a cheap round-trip.

**Annotation via `IChatClient` moves.** Today `CortexNeuron.AnnotateReasoningAsync` makes a single BDD-mock call after keyword-routing. After this spec the LLM call in the classifier *is* the reasoning annotation — no separate probe write is needed for LLM-routed hits. For regex fast-path hits, a lightweight probe write still records `source=cortex-regex`.

**`NeuronContext.ExperienceId`** is populated by Cortex before firing the canonical synapse — it sets `ctx with { ExperienceId = chosen.Id }`. Downstream neurons read it from context; today they ignore it, which is fine. This closes the "always null" gap Track 1 flagged.

### 7. Domain neurons — minimal LLM wiring

Travel neurons get `IChatClientFactory` (or `[FromKeyedServices(LlmTier.Balanced)] IChatClient`) injected and use it to generate the user-facing sentence that goes into the RFW card.

**Scope of edits per neuron:** ≤20 lines each — pull a `balanced` client from the factory, send a prompt built from the fixture data, append the response text to the synapse result's `Narrative`. Existing fixture-driven card rendering is untouched; only the narrative string is LLM-generated.

**`ItineraryComposer`** additionally uses `LlmTier.Reasoning` for the multi-step composition prompt that fans out to FlightSearch/HotelSearch/PlaceSearch.

### 8. Voice input — Flutter

**New widget:** `PushToTalkButton` in `lib/widgets/`. Wraps `window.SpeechRecognition` via a thin `package:web` interop.

**Behaviour:**
- Tap to start listening. Mic indicator lights up.
- Interim transcripts stream into the chat input's controller (user sees what's being heard).
- Silence ≥1.2s after speech detected → stop, auto-submit the final text via the existing chat send flow. Same gRPC-Web path as typed input.
- Tap again mid-listen = cancel (don't submit).
- Permission denied / browser unsupported → widget renders as disabled with a tooltip "Voice input requires Chrome or Edge (microphone permission)."

**What's not in this spec:** TTS playback, wake-word detection, non-browser platforms, mobile Flutter app. All can plug in later behind the same `IVoiceToText` marker.

**`WebSpeechApi` marker.** `Ino.Aspire.Hosting` defines `public sealed class WebSpeechApi : VoiceToTextProvider { … }`. It has no backend — the marker exists so AppHost can record the choice (useful for Track 7's telemetry tests later) and so future `WithVoiceToText<WhisperSmall>()` et al. have a typed slot.

### 9. Taxi — one minimal BDD scenario

Add `POC/domains/taxi/Ino.Domains.Taxi/Features/taxi-intent.feature`:

```gherkin
Feature: Taxi — intent routing

  Scenario: Hail a ride
    Given the user says "ride|taxi|uber|hail"
    Then the assistant replies "Summoning a ride via the RideSearch neuron."
```

One scenario, three regex alternatives. Enough to keep the BDD-mock path deterministic in tests. Real Uber-MCP integration is still deferred.

---

## Test seam and coverage

`IChatClient` is the only seam. Production wires `XaiChatClientFactory`; tests wire `BddMockChatClientFactory` (a new 30-line class in `Ino.Testing`). Every existing BDD test stays green because its fixture picks up the mock via the same `IChatClientFactory` interface.

**Cortex tests** gain one new case:
- Regex-fast-path hit → route without an LLM call (assert `IChatClient` wasn't invoked via a counting mock).
- Ambiguous fast-path → LLM classifier called, result routed.
- LLM classifier returns `none` → `UnroutedIntent` broadcast.

**Travel-neuron tests** gain one assertion each: the narrative string is non-empty (comes from the BDD mock's canned reply).

**No OTel assertions in this spec.** Structured span-tree assertions are Track 7. Existing `ActivityListener` capture in `Ino.ServiceDefaults` stays dormant for Spec A.

---

## Breaking changes

1. **`LlmTier.Default` → `LlmTier.Balanced`.** Callers: `Travel.cs`, `Taxi.cs`, plus a handful of tests.
2. **`LlmTier.Multimodal` removed.** No live consumer.
3. **`CortexNeuron` constructor** gains `IChatClientFactory`, loses direct `IChatClient`. Tests that construct it in isolation update.
4. **`AddIno(builder, name)`** currently returns `IInoBuilder`; that interface grows `WithLlm<TModel>()` / `AsFast/Balanced/Reasoning` / `WithVoiceToText<T>()`. Existing `.WithDomain<T>()` unaffected.

---

## Verification — per the CLAUDE.md loop

Per CLAUDE.md the verification is build + test + aspire + Flutter + E2E.

1. `dotnet build POC/ino.slnx` — clean.
2. `dotnet test POC/ino.slnx` — BDD tests still green against the mock; Cortex tests exercise the three new branches.
3. `aspire start --apphost POC/src/Ino.AppHost/Ino.AppHost.csproj --isolated` with `XAI_API_KEY` set — all resources Healthy.
4. Open the `system-http` endpoint in Chromium. Confirm:
   - Mic button renders, permission prompt appears on tap.
   - "find flights to Bali" (voice) → flight card with Grok-generated narrative + seeded fixtures.
   - Typed "plan 5 days in Tokyo" → Cortex routes to `travel.plan-trip` via LLM classifier (no regex match) → ItineraryComposer fans out.
   - "call me a ride" → Cortex routes to `taxi.find-ride`; scaffold neuron returns a stub narrative.
   - Aspire Structured Logs filter `ino-flutter` shows BLoC transitions; Traces show `grpc Chat` → `fire ChatIntent` → `handle CortexNeuron.HandleAsync` → downstream.
5. `INO_E2E_NO_BROWSER=true dotnet test POC/test/Ino.E2E.Tests --filter "Category=E2E"` — existing tests green; no new E2E added in this spec.

**Manual verification required because Spec A adds a UI component** (mic button) and a live external dependency (xAI API). The verification loop cannot be skipped; "builds clean + tests pass" is not a completion signal for this spec.

---

## Build sequence (phases — writing-plans will expand)

1. **Tier enum rename.** `LlmTier` update, two domain callsites, tests. One commit.
2. **Model catalog + adapter.** `Ino.Llm.Xai` project, `LlmModel` base, three Grok classes, `XaiChatClientFactory`, unit test that verifies tier fallback without hitting the network.
3. **Fluent AppHost API.** `IInoBuilder.WithLlm<T>()` + `AsFast/Balanced/Reasoning`, `WithVoiceToText<T>()`. No silo wiring yet.
4. **Silo config propagation + `AddInoChatClients`.** AppHost → env vars → silo-side deserialization + DI registration. Silo boots with real xAI in a manual run.
5. **Cortex refactor.** Catalog-driven hybrid routing. Unit tests for the three branches. `CortexNeuron` old keyword branches deleted.
6. **Domain-neuron narrative LLM wiring.** Travel's five neurons + Taxi's scaffold get `IChatClientFactory`. ~20 lines each.
7. **Taxi minimal `.feature`.** One scenario file.
8. **Flutter mic widget.** `PushToTalkButton`, Web Speech API interop, silence-detect submit.
9. **Test fixture wiring.** `BddMockChatClientFactory` in `Ino.Testing`. Every existing test picks it up via the new `IChatClientFactory` interface.
10. **Manual verification + polish.** Per the checklist above.

---

## Risks and how the spec handles them

- **Model-ID format divergence.** Sources disagree on `grok-4-1-fast-reasoning` vs `grok-4.1-fast-reasoning`. Mitigation: first implementation slice hits the API once with both and keeps whichever returns 200; noted above.
- **Web Speech API inconsistency.** Firefox coverage is partial; Safari worse. Spec A targets Chrome + Edge; other browsers render the widget disabled with a tooltip. Not a regression — today the app has no voice at all.
- **LLM classifier latency.** Grok 4.1 Fast is fast but adds a round-trip over the regex fast-path. Mitigation: the fast-path catches the common cases (phrases that match `PromptExamples` verbatim); LLM only runs on ambiguous or novel utterances.
- **Cost on a demo loop.** Fast tier at cache-hit is ~$0.05/M, Balanced similar; Reasoning tier for `ItineraryComposer` is ~$0.20/M cached. A single-developer demo loop stays well under $1/day.
- **xAI outage mid-demo.** Cortex's regex fast-path still works without the LLM; Balanced-tier neurons surface a "LLM unavailable, using fallback copy" narrative. Implemented as a try/catch in each neuron's narrative-build step, not in the factory.
- **Scope creep toward Spec B.** This spec explicitly omits memory. If the MVP reveals that "the assistant feels dumb without memory," that's the right signal to ship Spec B — not to widen Spec A mid-flight.
- **1-week timebox vs. 10 phases.** Phases 1–2 and 9 are mechanical. 3–4 are config plumbing (half-day each). 5–7 are the real work. 8 is the one UI slice that can bite. Realistic total: 5–7 working days for a focused single developer.

---

## Open items explicitly left for implementation

- Exact xAI model-ID string format (verified via live API first-slice).
- Exact silence threshold for mic auto-submit — 1.2s is a guess; tune in step 8.
- Whether to include Ollama as a commented-out secondary option in AppHost — lean yes (matches IAW's AppHost pattern, gives cloners an offline path) but confirm in writing-plans.
- Whether the factory caches `IChatClient` instances per tier or constructs on each call — lean cached; implementation-level, not design.

---

## What Spec B will inherit from Spec A

- The `IChatClient` seam and tier abstraction — memory neurons use the same factory.
- The `IReasoningProbe` pattern — Spec B extends it to memory-record visibility.
- `NeuronContext.ExperienceId` populated by Cortex — Spec B uses it to scope memory recalls.
- The BDD-mock test seam — memory tests use the same `.feature`-driven simulator.

Spec B is additive on top of a working Spec A. Nothing in Spec A locks out anything Spec B needs.
