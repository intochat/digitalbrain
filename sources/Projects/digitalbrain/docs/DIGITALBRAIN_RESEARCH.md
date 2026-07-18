# DigitalBrain — Architecture Research & Final Vision

> **Status:** RFC / proposal · **Date:** 2026-05-10 · **Supersedes:** `docs/reqnroll.md`,
> `docs/superpowers/specs/2026-05-10-digitalbrain-restructure-and-flutter-desktop-design.md`
>
> **TL;DR.** DigitalBrain is a specification-driven, AI-native runtime for behaviors. Users
> describe what they want in plain English (often by voice). The system writes a
> Reqnroll `.feature` file that captures the intent as red-green BDD scenarios,
> declares its dependencies on existing neurons (Gmail, SQLite, an LLM), runs the
> tests, and only then compiles and activates the new neuron. We are connectors
> and architects; the user gets to program by talking.
>
> This document settles the open architecture questions, names the libraries we
> standardise on, lays out the final project tree, and walks through the
> end-to-end voice-driven scenario the PoC must pass.

---

## Table of contents

1. [Vision and scope](#1-vision-and-scope)
2. [Core principles](#2-core-principles)
3. [The neuron paradigm — three options, one winner](#3-the-neuron-paradigm--three-options-one-winner)
4. [AI as a domain — LLM, embedding, voice as neurons](#4-ai-as-a-domain--llm-embedding-voice-as-neurons)
5. [Kernel vs Core — the boundary](#5-kernel-vs-core--the-boundary)
6. [Domains, contracts, and cross-domain reuse](#6-domains-contracts-and-cross-domain-reuse)
7. [Aspire wiring — `builder.AddDigitalBrain()`](#7-aspire-wiring--builderadddigitalbrain)
8. [Reqnroll at runtime — Creator follows red-green](#8-reqnroll-at-runtime--creator-follows-red-green)
9. [Telemetry and validation declared in `.feature` files](#9-telemetry-and-validation-declared-in-feature-files)
10. [Google domain — `GoogleGmailNeuron` and friends](#10-google-domain--googlegmailneuron-and-friends)
11. [SQLite neuron — per-instance DbContext](#11-sqlite-neuron--per-instance-dbcontext)
12. [Dynamic domain — where runtime-born neurons live](#12-dynamic-domain--where-runtime-born-neurons-live)
13. [Synapse payload — JSON + bytes](#13-synapse-payload--json--bytes)
14. [Brain visualization — neurons, synapses, `.feature` files, icons](#14-brain-visualization--neurons-synapses-feature-files-icons)
15. [Flutter client — three tabs, voice in, RFW cards out](#15-flutter-client--three-tabs-voice-in-rfw-cards-out)
16. [Testing — only `dotnet test`, no `flutter test`](#16-testing--only-dotnet-test-no-flutter-test)
17. [The end-to-end PoC scenario](#17-the-end-to-end-poc-scenario)
18. [Example `.feature` files](#18-example-feature-files)
19. [Final project tree](#19-final-project-tree)
20. [Migration plan from current state](#20-migration-plan-from-current-state)
21. [Risks, trade-offs, open questions](#21-risks-trade-offs-open-questions)

---

## 1. Vision and scope

DigitalBrain is **not** a new chat product. It is a substrate that turns natural-language
intent into running, testable software. The unit of work is a **neuron**: a small
addressable behavior that consumes synapses and emits synapses. The unit of
specification is a **`.feature` file**: a Gherkin document that any sufficiently
intelligent process — LLM or human — can read, validate, and grow.

What changes vs. today:

- **Anyone programs.** A user says "I want my last 5 email senders in a database."
  DigitalBrain turns that into a feature file, generates a neuron under it, runs the
  tests red, makes them green, and only then lets the agent run live.
- **Behaviors compose.** Generated neurons reuse existing ones — `GoogleGmailNeuron`,
  `SqliteNeuron`, `LlmNeuron("gpt5-reasoning")` — without the user knowing they
  exist. The Creator picks them by reading the brain catalog.
- **The brain is observable.** Every synapse is recorded. The 3D viz shows
  neurons with their icons, synapses flowing between them, the `.feature` file
  that defines each neuron, and the JSON payload of any synapse you click.
- **We are the connectors.** We ship the kernel, the AI domain, the Google
  domain, the data domain, and the Flutter client. Users grow the rest.

What this document **does not** cover: deployment to a public cloud, multi-user
SaaS, billing, marketplace of neurons. Those come later. The PoC runs on one
desktop, with one user, and proves the loop.

## 2. Core principles

These are the non-negotiables every architecture decision below answers to:

1. **Spec first.** No neuron exists without a `.feature` file. No behavior runs
   without a green test.
2. **AI-native, not AI-bolted.** LLMs, embedding generators, and speech-to-text
   are first-class neurons in the AI domain; nothing about them is special at
   the kernel layer.
3. **Aspire as the chassis.** `DistributedApplication.CreateBuilder` is the only
   composition root. ServiceDefaults handles every cross-cutting concern. Each
   domain `Program.cs` is one line: `builder.AddDigitalBrainDomain<TThis>()`.
4. **Orleans for state.** Neurons are virtual actors. The runtime decides where
   they live; we never know.
5. **Microsoft.Extensions.AI for portability.** `IChatClient`,
   `IEmbeddingGenerator`, and `ISpeechToTextClient` are the only LLM/voice
   contracts we depend on. Provider swap = one DI line.
6. **Reqnroll for executable specifications.** Not a custom DSL. Not a fork.
   Plain Gherkin, parsed by the upstream `Gherkin` 39.x library and run by
   Reqnroll 3.3+.
7. **One test command.** `dotnet test` runs every test in the system —
   including the ones that drive the Flutter UI through gRPC.

## 3. The neuron paradigm — three options, one winner

The key design question: **how does an LLM, or a speech-to-text engine, fit
into DigitalBrain?** Microsoft.Extensions.AI gives us `IChatClient`. The current
`DigitalBrain.Core.Hosting.Llm` wraps that as an `LlmNeuron`. Three viable shapes:

### Option A — keep the wrapper (today)

```csharp
public sealed class LlmNeuron : Neuron {
    private readonly IChatClient _client;
    public LlmNeuron(IChatClient client) => _client = client;
    public override async Task ProcessAsync(SynapseRecord syn, ...) { ... }
}
```

One neuron type per *capability* (chat, embed, transcribe). The chosen model is
configuration. Aspire wires `IChatClient` keyed by model name; the neuron picks
the right one at runtime.

✅ Cheap. ✅ Familiar. ❌ The brain catalog only sees one entry called
"LlmNeuron"; it can't tell `gpt-5-nano` apart from `gpt-5-reasoning`. ❌ Voice
goes through a parallel hierarchy.

### Option B — a neuron per model (IAW-style, but pure neurons)

`Gpt5Reasoning` is a neuron. `Gpt5Nano` is a neuron. `WhisperLargeV3Turbo` is a
neuron. Each registers itself in the brain catalog with a unique id, an icon,
and a capability tag (`fast`, `balanced`, `reasoning`). The neuron *uses* an
`IChatClient` (or `ISpeechToTextClient`) internally.

```csharp
[Neuron(Id = "ai/llm/openai/gpt-5-reasoning", Icon = "openai", Capability = NeuronCapability.Reasoning)]
public sealed class Gpt5ReasoningNeuron : LlmNeuronBase {
    public Gpt5ReasoningNeuron([FromKeyedServices("gpt-5-reasoning")] IChatClient client)
        : base(client) { }
}
```

✅ One node per model in the brain viz; users see exactly what's available.
✅ Routing is explicit ("send this to the reasoning neuron"). ✅ Each model can
have its own `.feature` file documenting strengths/limits. ❌ More classes
(but they're trivial — a one-line subclass).

### Option C — registry of capabilities, no neuron-per-model

`LlmNeuron` becomes a *family*: at registration we tell the kernel "for capability
`reasoning` on provider `openai`, the model is `gpt-5`". The Creator picks by
capability, not by model name. The brain viz shows `LlmNeuron[reasoning]` etc.

✅ Smallest surface. ✅ Closest to IAW's `.AsReasoning()`. ❌ Loses the visceral
sense of "this is gpt-5 talking" in the viz. ❌ Capability is a leaky abstraction:
some models reason cheaper, some translate better, etc.

### **Recommendation — Option B**

A neuron per model, generated from one base class via a marker type, registered
fluently in AppHost:

```csharp
.WithLlm<OpenAI.Gpt5Nano>().AsFast()
.WithLlm<OpenAI.Gpt5Mini>().AsBalanced()
.WithLlm<OpenAI.Gpt5>().AsReasoning()
```

The marker type carries metadata (provider, model id, default scopes, icon).
`AsFast/Balanced/Reasoning` adds capability tags so the Creator can ask the
catalog for "give me a reasoning-class neuron" without naming a model. The
result: clean catalog, fluent config, one base class to maintain.

Same shape for voice (`WithVoice2Text<Whisper.LargeV3Turbo>()`) and embedding
(`WithEmbedding<OpenAI.TextEmbedding3Small>()`).

## 4. AI as a domain — LLM, embedding, voice as neurons

### Library choices (verified May 2026)

| Concern | Library | Interface | Notes |
|---|---|---|---|
| Chat / completion | `Microsoft.Extensions.AI` 1.0.3+ | `IChatClient` | Provider-agnostic; OpenAI, Anthropic via SK, Ollama via OllamaSharp, GitHub Models, Azure OpenAI all implement it |
| Embedding | `Microsoft.Extensions.AI.Abstractions` | `IEmbeddingGenerator<string, Embedding<float>>` | Same provider list |
| Speech-to-text | `Whisper.net` 1.9.0+ | `ISpeechToTextClient` (`WhisperSpeechToTextClient`) | New in 1.9; CUDA 13/12 with auto-fallback to CPU; Vulkan; Metal; CoreML |
| Function calling | `Microsoft.Extensions.AI` middleware | `.UseFunctionInvocation()` | Wires automatically when chained on `AddChatClient` |
| Caching / OTel / logging | `Microsoft.Extensions.AI` middleware | `.UseDistributedCache()`, `.UseOpenTelemetry()`, `.UseLogging()` | All composable on `ChatClientBuilder` |

### What lives in `DigitalBrain.Domains.Ai`

- **`LlmNeuronBase`** — abstract; concrete subclasses are tiny. Handles a
  `LlmRequest` synapse (system prompt, messages, tools), emits `LlmResponse`.
- **`Voice2TextNeuron`** — handles `Voice2TextRequest` (raw audio bytes +
  format hint), emits `Voice2TextResponse` (transcript + segments).
- **`EmbeddingNeuron`** — handles `EmbeddingRequest`, emits `EmbeddingResponse`.
- **Marker types** in `Models/`:

```csharp
namespace DigitalBrain.Domains.Ai.Models;

public static class OpenAI {
    public sealed record Gpt5Nano    : ILlmModel { public static string Id => "gpt-5-nano"; public static string Icon => "openai"; }
    public sealed record Gpt5Mini    : ILlmModel { public static string Id => "gpt-5-mini"; public static string Icon => "openai"; }
    public sealed record Gpt5        : ILlmModel { public static string Id => "gpt-5"; public static string Icon => "openai"; }
    public sealed record TextEmbedding3Small : IEmbeddingModel { ... }
}

public static class Anthropic {
    public sealed record Claude5Haiku  : ILlmModel { public static string Id => "claude-5-haiku"; public static string Icon => "anthropic"; }
    public sealed record Sonnet47      : ILlmModel { public static string Id => "claude-sonnet-4-7"; public static string Icon => "anthropic"; }
    public sealed record Opus47        : ILlmModel { public static string Id => "claude-opus-4-7"; public static string Icon => "anthropic"; }
}

public static class Whisper {
    public sealed record LargeV3Turbo : IVoiceModel { public static string Id => "whisper-large-v3-turbo"; public static string Icon => "whisper"; }
}
```

A registration like `.WithLlm<OpenAI.Gpt5>().AsReasoning()` does three things:

1. Calls `builder.AddOpenAIClient("openai")` (or reads existing) and chains
   `.AddKeyedChatClient(modelId)` with all the standard middleware
   (`UseFunctionInvocation`, `UseOpenTelemetry`, `UseLogging`).
2. Registers a `Gpt5Neuron` (auto-generated subclass of `LlmNeuronBase`) in the
   AI silo, keyed to that `IChatClient`.
3. Adds the catalog entry: `{ id: "ai/llm/openai/gpt-5", capability: "reasoning",
   icon: "openai", feature: "Llm.feature" }`.

### Why BDD tests can interact with LLMs

A step like `When the reasoning neuron is asked "Summarise this email"` needs
to actually call gpt-5 only when we're in an integration test stage. For the
unit/fast lane, `BddMockChatClient` (already in the codebase) returns canned
responses keyed by prompt fingerprint. We extend the existing pattern:

- `@stage:fast` tag → mocked LLM, mocked voice, no network
- `@stage:integration` tag → real `IChatClient`, real Whisper, real Google
- `@stage:e2e` tag → full Aspire orchestration, real Flutter via gRPC

This is the same `dotnet test --filter` flow the current Travel tests use.

## 5. Kernel vs Core — the boundary

Today the line is fuzzy: `DigitalBrain.Core` defines `Neuron` and `SynapseRecord`,
`DigitalBrain.Core.Hosting` adds Orleans grains and the Roslyn compiler,
`DigitalBrain.Kernel` hosts the Creator and the gateway. Let's make it explicit.

| Layer | Reusable across domains? | Examples | Has `.feature` files? |
|---|---|---|---|
| **Core** (primitives) | Yes — every project references it | `Neuron`, `SynapseRecord`, `NeuronId`, `INeuronRegistry`, `IRoslynCompiler` | No — pure runtime |
| **Core.Hosting** (Orleans glue) | Yes — every silo references it | `DynamicNeuronGrain`, `BrainCatalogGrain`, `NeuronRegistryGrain`, `RoslynCompiler` | No |
| **Kernel** (the silo) | No — only one in the system | `CreatorNeuron`, `NavigatorRouter`, `DigitalBrainGatewayService`, `BrainWatchService` | Yes — `Creator.feature`, `Cortex.feature` |
| **Kernel.Contracts** | Yes — referenced by domains that talk to kernel | `CreateNeuronRequest`, `NeuronCreated`, synapse type names, proto definitions | No |
| **Domain** (e.g. Ai, Google, Travel, Data) | Itself: no. Its **contracts**: yes | Neurons + features + step defs | Yes |
| **Domain.Contracts** | Yes — any other domain can reference | Synapse records, type names | No |

**Rule:** a domain may reference any other domain's `*.Contracts`, but **never**
the implementation project. Cross-domain communication goes through synapses
routed by the cortex; the sender doesn't import the receiver's neuron class.

This is the same pattern Orleans uses (interface project + grain project), and
it's what enables the cortex to be a generic router instead of a switch
statement.

## 6. Domains, contracts, and cross-domain reuse

A domain consists of **three projects**:

```
domains/<name>/
├── DigitalBrain.Domains.<Name>/                # silo: neurons, features, step defs
├── DigitalBrain.Domains.<Name>.Contracts/      # synapse records, type names
└── DigitalBrain.Domains.<Name>.Tests/          # Reqnroll runner only
```

The silo project **co-locates** each neuron with its feature file and step
definitions:

```
DigitalBrain.Domains.Ai/Llm/
├── LlmNeuronBase.cs
├── LlmNeuron.feature
├── LlmNeuron.Steps.cs
└── Models/
    └── OpenAIModels.cs
```

The `*.Tests` project is intentionally thin — it references the silo project,
references `DigitalBrain.NeuronTesting`, and contains nothing but `_GlobalUsings.cs`
and `reqnroll.json`. The Reqnroll source generator picks up the `.feature` files
from the silo project (via `<ProjectReference>` and `<RootNamespace>` tricks)
and produces the test classes in `*.Tests`.

> **Why not put tests in the silo project itself?** Because the silo deploys to
> production — we don't want Reqnroll runtime, xUnit, and FluentAssertions in
> the production binary. The split keeps the silo lean.

### How cross-domain reuse works

Concrete example: a runtime-generated neuron in `Dynamic` that fetches Gmail
senders and writes them to SQLite.

1. `DigitalBrain.Domains.Dynamic` references
   `DigitalBrain.Domains.Google.Contracts` and `DigitalBrain.Domains.Data.Contracts`.
2. The Roslyn-compiled neuron sends a `GmailListMessagesRequest` synapse with
   `correlationId = X` and listens for `GmailListMessagesResponse` with the
   same correlation id.
3. It also sends a `SqliteUpsertRequest` to the SQLite neuron with a
   `database = "email-senders"` slug. The kernel routes by **synapse type
   name**, not by neuron name.

The cortex doesn't know about Gmail or SQLite specifically. It looks up "who is
subscribed to `GmailListMessagesRequest`?" in the brain catalog and forwards.
Adding a new domain doesn't require any kernel changes.

## 7. Aspire wiring — `builder.AddDigitalBrain()`

The current AppHost mixes per-project setup with kernel concerns. Refactor so
that **every domain registers the same way**, the AppHost reads as a manifest
of capabilities, and individual `Program.cs` files become single-line.

### `DigitalBrain.AppHost/Program.cs`

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var digitalbrain = builder.AddDigitalBrain("digitalbrain")
    .WithRedis()                                    // Orleans clustering + grain storage
    .WithKernel()                                   // CreatorNeuron, gateway, brainwatch
    .WithDomain<Projects.DigitalBrain_Domains_Ai>(ai => ai
        .WithLlm<OpenAI.Gpt5Nano>().AsFast()
        .WithLlm<OpenAI.Gpt5Mini>().AsBalanced()
        .WithLlm<OpenAI.Gpt5>().AsReasoning()
        .WithEmbedding<OpenAI.TextEmbedding3Small>()
        .WithVoice2Text<Whisper.LargeV3Turbo>())
    .WithDomain<Projects.DigitalBrain_Domains_Google>(g => g
        .WithGmail()                                // GoogleGmailNeuron + auth flow
        .WithCalendar())                            // GoogleCalendarNeuron + auth flow
    .WithDomain<Projects.DigitalBrain_Domains_Data>(d => d
        .WithSqlite())
    .WithDomain<Projects.DigitalBrain_Domains_Travel>()  // legacy / example
    .WithDomain<Projects.DigitalBrain_Domains_Dynamic>(); // empty silo for runtime-generated

builder.AddProject<Projects.DigitalBrain_Client_Flutter>("flutter")
    .WithReference(digitalbrain);

builder.Build().Run();
```

### Each domain `Program.cs`

```csharp
// DigitalBrain.Domains.Ai/Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainDomain();        // ServiceDefaults + Orleans silo + neuron registry + Reqnroll runtime
builder.Build().Run();
```

That's it. No manual OpenTelemetry config, no manual Orleans config, no manual
DI for `IChatClient` — all handled by the extension methods that
`builder.AddDigitalBrain(...).WithDomain<T>()` injected as environment variables.

### What `AddDigitalBrain` actually does

- Adds Redis (or in-memory in dev) for Orleans clustering and grain storage.
- Adds Aspire's `AddOrleans("digitalbrain")` and configures clustering once.
- Adds an OTLP collector and the dashboard endpoints used by `BrainWatchService`.
- For each `WithLlm<T>()` call, configures the right `AddKeyedChatClient` on
  the AI silo project's environment.
- Provides shared parameters (`OPENAI_API_KEY` etc.) via `builder.AddParameter`
  so secrets stay out of source.
- Wires up the Flutter client's gRPC reference.

### `DigitalBrain.ServiceDefaults`

Stays small. Provides:

- `AddServiceDefaults()` — OTel, health, service discovery, HTTP resilience.
- `AddDigitalBrainDomain()` — calls `AddServiceDefaults` plus: Orleans silo
  (parameterless `UseOrleans()` with Aspire injecting config), neuron registry,
  Reqnroll runtime hooks, autodiscover neurons via assembly scanning.
- `AddDigitalBrainClient()` — calls `AddServiceDefaults` plus: gRPC channel to the
  gateway, sets up `DigitalBrainGrpcInterceptor`.

## 8. Reqnroll at runtime — Creator follows red-green

The Creator is a kernel neuron. Its job: given a `CreateNeuronRequest`
(natural-language description + optional voice transcript + correlation id),
produce a working neuron and tests in the Dynamic domain.

### Steps it takes

1. **Plan with an LLM (reasoning class).** Ask for a Gherkin `.feature` and a
   list of dependencies (existing neurons + synapse types). The reasoning
   neuron's prompt includes the **brain catalog**: every neuron, its synapses,
   its icon. (Catalog is exposed via a function-call tool, so the LLM can
   browse rather than carry the whole list in context.)
2. **Validate the `.feature`.** Parse with the upstream `Gherkin` 39.0.0 NuGet.
   If `ParserException` or `CompositeParserException`, return errors to the
   LLM and re-prompt. (Verified working in current Reqnroll source via
   `CheckSemanticErrors`.)
3. **Compile and validate step definitions.** Generate the C# step file via
   the LLM. Parse with `CSharpSyntaxTree.ParseText` and inspect
   `Compilation.GetDiagnostics()` for errors before persisting. Reject and
   re-prompt on compile errors.
4. **Persist red.** Write `.feature` and `.cs` to the Dynamic domain's
   per-neuron folder under grain storage. Run the test (via
   `INeuronTestRunner`, which spins up an in-process Reqnroll runtime). It
   fails — as it should, the neuron doesn't exist yet.
5. **Generate the neuron.** Same LLM, now asked: "Implement a `Neuron`
   subclass that satisfies these step definitions and uses these dependencies."
   Compile (`IRoslynCompiler`), load into a collectible `AssemblyLoadContext`.
6. **Persist green.** Re-run the test. Green: register the neuron in
   `INeuronRegistry`, expose it via the brain catalog, emit a `NeuronCreated`
   synapse. Red: feed errors back, retry up to N attempts, then give up and
   emit a failure event with the last LLM transcript.

### Why per-neuron folders

Each runtime-generated neuron lives at:

```
DigitalBrain.Domains.Dynamic/
└── Generated/
    └── <neuron-id>/
        ├── manifest.json       # name, deps, capabilities, icon
        ├── <name>.feature
        ├── <name>.cs
        └── <name>.Steps.cs
```

When the Dynamic silo starts, it walks `Generated/`, compiles every neuron in
parallel, and registers them. Promotion to a real domain is a `git mv` plus a
catalog flag flip.

### Why no full source generators at runtime

Reqnroll's Roslyn source generators run at build time. At runtime we don't
generate code with attributes — we run an in-process Reqnroll runtime that
reads the `.feature` file and matches step text against the compiled
step-definition assembly. This is well-trodden in Reqnroll for "external data"
scenarios; we follow the same pattern.

## 9. Telemetry and validation declared in `.feature` files

Two things need to be declarable from the spec, not from code:

- **Telemetry.** A counter for "emails fetched", a histogram for "transcription
  latency", etc. The spec is the source of truth.
- **Validation.** "This step's argument must be a valid email address",
  "this synapse's payload must validate against this JSON Schema", etc.

We use **tags**, which Reqnroll already passes through to step hooks:

```gherkin
@neuron:dynamic/email-senders
@telemetry:counter:emails_fetched
@telemetry:histogram:fetch_latency_ms
@requires:google.gmail
@requires:data.sqlite
@stage:integration
Feature: Cache last 5 email senders into a local SQLite database

  Background:
    Given a fresh SqliteNeuron with database id "email-senders" and schema:
      """
      CREATE TABLE IF NOT EXISTS senders (
        email TEXT PRIMARY KEY,
        first_seen_at TEXT NOT NULL
      );
      """

  Scenario: Fetch and cache the most recent senders
    Given the user has authorised Google with scope "gmail.readonly"
    When I ask the dynamic neuron to refresh the cache
    Then the SQLite database "email-senders" contains exactly 5 rows
    And the counter "emails_fetched" was incremented by 5
    And the histogram "fetch_latency_ms" recorded 1 sample
```

A custom `[BeforeScenario]` hook reads the `Tags` collection, looks for the
`telemetry:` prefix, and registers `Meter`-backed counters/histograms ready
for the assertion in the `Then` step. The `requires:` tag is read by the
Creator before generation to know which contracts to reference.

## 10. Google domain — `GoogleGmailNeuron` and friends

### Library choice

`Google.Apis.Auth.OAuth2` 1.73+ for the auth flow plus `Google.Apis.Gmail.v1`
and `Google.Apis.Calendar.v3` for the APIs. This is the official client. We
checked Context7 / NuGet — there is no better-maintained C# alternative that
covers both. Don't roll our own OAuth.

### Per-neuron scopes (your decision in turn 2)

`GoogleGmailNeuron` requests **only** Gmail-relevant scopes
(`gmail.readonly`, `gmail.send`, etc., depending on operation), not the entire
Google scope set. Same for `GoogleCalendarNeuron`. This is a hard rule: each
Google neuron has minimum scopes baked in. If a future neuron needs Drive,
that's a new `GoogleDriveNeuron` with its own scopes.

### The auth flow

`GoogleWebAuthorizationBroker.AuthorizeAsync` exists and works for installed
apps — it spins up a local HTTP receiver on a free port, opens the system
browser, captures the auth code, exchanges for tokens. The default `IDataStore`
is `FileDataStore`, which writes to
`%AppData%\Google.Apis.Auth\Google.Apis.Auth.OAuth2.Responses.TokenResponse-user`.

We **replace** the default with `DigitalBrainGrainDataStore`, which:

- Stores tokens in an Orleans grain keyed by `(domain, neuron-id, user)`.
- Encrypts at rest using DPAPI on Windows / `libsecret` on Linux / Keychain
  on macOS. Falls back to a master-key-encrypted blob otherwise.
- Refreshes transparently on `IConfigurableHttpClientInitializer` callbacks.

This puts auth state inside Orleans, which means it survives restarts, scales
with the cluster, and shows up in the brain visualization (you can see "Gmail
neuron — authenticated as user@x.com — token expires in 12m").

### Auth as internal concern, not a neuron

`GoogleAuthBroker` is a class inside `DigitalBrain.Domains.Google`, **not a neuron**.
The reasoning: auth is plumbing, not behavior. Neurons are addressable units
that emit synapses; an auth broker doesn't naturally do that. Instead, every
Google neuron's constructor takes `GoogleAuthBroker` and asks it for a
`UserCredential` with the scopes that neuron needs.

If a synapse arrives and the user hasn't consented yet, the neuron emits a
`GoogleAuthRequired` synapse with the consent URL. The kernel forwards it to
the Flutter UI, which shows a card and opens the browser. After the local
receiver captures the code, the broker stores the token, and the neuron retries
the original synapse from the kernel's correlation log.

### How the Gmail "last 5 senders" call looks

```csharp
public sealed class GoogleGmailNeuron(GoogleAuthBroker auth) : Neuron
{
    public override async Task ProcessAsync(SynapseRecord syn, CancellationToken ct)
    {
        if (syn.Type != GoogleSynapseTypes.GmailListMessagesRequest) return;
        var req = syn.As<GmailListMessagesRequest>();

        var cred = await auth.GetCredentialAsync(
            scopes: new[] { GmailService.Scope.GmailReadonly },
            user: syn.Tenant,
            ct);

        if (cred is null) {
            await Emit(syn.Reply(GoogleSynapseTypes.AuthRequired,
                new GoogleAuthRequired(scopes: ..., consentUrl: ...)));
            return;
        }

        var gmail = new GmailService(new BaseClientService.Initializer {
            HttpClientInitializer = cred,
            ApplicationName = "DigitalBrain"
        });
        var list = await gmail.Users.Messages.List("me").ExecuteAsync(ct);
        // ... extract senders ...
        await Emit(syn.Reply(GoogleSynapseTypes.GmailListMessagesResponse,
            new GmailListMessagesResponse(senders)));
    }
}
```

## 11. SQLite neuron — per-instance DbContext

`SqliteNeuron` is parameterised by a **database id** (a slug). On grain
activation, it asks the kernel for `IDatabaseContextFactory.GetContext(id)`
which:

- Resolves a path: `%LocalAppData%\DigitalBrain\databases\{tenant}\{id}.db`.
- Creates the file if missing; runs migrations declared by the `.feature`
  file's `Background` (the schema doc-string from the example above).
- Returns a `SqliteConnection` (or `DbContext` if EF Core is requested).
- Caches per-grain — one `SqliteConnection` per active neuron.

Because each neuron has its own database id, the runtime-generated email-senders
neuron gets its own `email-senders.db`. No conflicts, no shared state. When the
neuron is deactivated, the connection closes; when reactivated, it reopens.
File survives.

The `SqliteNeuron`'s contracts cover the basic ops the Creator might generate:

- `SqliteExecuteRequest(sql, params)`
- `SqliteQueryRequest(sql, params)`
- `SqliteUpsertRequest(table, columns, values)`
- `SqliteCountRequest(table, where)`

The Creator generates SQL inline in the dynamic neuron; SQLite's strictness
catches bad schemas at red-test time.

## 12. Dynamic domain — where runtime-born neurons live

A clean separation:

- **Travel**, **Ai**, **Google**, **Data** — built by us, ship in source control.
- **Dynamic** — empty at startup. Holds neurons born at runtime via the
  Creator. Persists to grain storage so they survive restarts.

This solves a real architectural smell from your current design: today there's
no clean home for runtime-generated neurons. Putting them in `Kernel` would
pollute the kernel; putting them in `Travel` is wrong by definition. `Dynamic`
is the answer.

**Promotion** from Dynamic to a "real" domain is a manual step and should be:
this is how a one-shot user behavior becomes a first-class feature. The
manifest.json carries enough to make the move trivial: it lists the synapse
types referenced, the icon, the capabilities, the test stage. Promotion = move
the folder, update `using` directives, delete the runtime entry from grain
storage.

## 13. Synapse payload — JSON + bytes

Today `SynapseRecord` has a `Payload` of an unspecified shape. Make it explicit:

```csharp
public sealed record SynapsePayload(
    JsonElement? Json,
    ReadOnlyMemory<byte> Bytes,
    string? BytesMimeType
) {
    public static SynapsePayload FromJson<T>(T value) =>
        new(JsonSerializer.SerializeToElement(value, JsonOptions), default, null);
    public static SynapsePayload FromBytes(ReadOnlyMemory<byte> bytes, string mime) =>
        new(null, bytes, mime);
    public static SynapsePayload Both<T>(T value, ReadOnlyMemory<byte> bytes, string mime) =>
        new(JsonSerializer.SerializeToElement(value, JsonOptions), bytes, mime);
}
```

The brain visualization shows the JSON element in a syntax-highlighted card
plus a "raw bytes (4.2 MB, audio/wav)" label when bytes are present. Voice2Text
synapses carry both: JSON metadata (sample rate, language hint) + bytes (the
audio).

This is critical for debuggability. Today the only way to know what a synapse
contains is to attach a debugger. With JSON-on-the-wire, the Flutter UI
displays the payload directly; the user sees "ah, the LLM got *this* prompt".

## 14. Brain visualization — neurons, synapses, `.feature` files, icons

Three additions to the existing 3D viz:

### Icons per neuron

Every neuron declares an icon name. The catalog stores it. The Flutter client
maintains an `IconRegistry` mapping name → asset. We ship icons for: openai,
anthropic, google, gmail, googlecalendar, whisper, sqlite, ollama, github,
generic-llm, generic-data, creator. Adding a new domain = drop one PNG.

```dart
// lib/icons/icon_registry.dart
class IconRegistry {
  static const Map<String, String> _icons = {
    'openai':   'assets/icons/openai.svg',
    'gmail':    'assets/icons/gmail.svg',
    'whisper':  'assets/icons/whisper.svg',
    // ...
  };
  static Widget? iconFor(String name) => _icons[name] == null
      ? null
      : SvgPicture.asset(_icons[name]!, width: 24, height: 24);
}
```

### `.feature` file viewer on click

The brain catalog already exposes neuron metadata. Add a `featureContent`
field — the raw Gherkin source. Flutter shows it in a syntax-highlighted
modal sheet when you tap a neuron. For runtime-generated neurons in Dynamic,
this is the file the Creator produced; for compiled-in neurons, it's
`Resources.GetString` from the assembly.

### Synapse payload viewer on click

Tap a synapse (the line/particle between two neurons) → modal with:

- `Type` (e.g. `ai.llm.LlmRequest`)
- `Correlation id`
- `Source neuron` → `Target neuron`
- `JSON` block (collapsible, syntax-highlighted)
- `Bytes` summary (size + MIME) if present, with a "save raw" button

This adds enormous debuggability. With JSON on the wire we don't have to
decode anything custom.

## 15. Flutter client — three tabs, voice in, RFW cards out

### Tab 1 — **Home** (chat + voice + generative cards)

- A chat surface at the top.
- A microphone button at the bottom; tap-and-hold or tap-to-toggle.
- When recording stops, the audio is sent over gRPC to the gateway, which
  forwards it as a `Voice2TextRequest` synapse to the AI domain. The
  transcript comes back over a server-streaming gRPC call.
- LLM responses can include **RFW card payloads**. When a synapse arrives
  with type `ui.RfwCard`, Flutter renders it via `rfw` package using the
  `core` and `material` local widget libraries plus a small DigitalBrain-specific
  one (DigitalBrain buttons, DigitalBrain chips, DigitalBrain data tables).
- Cards spawn **at the bottom**, scrolling up over time, like a feed —
  not pinned to the top. (Your spec.)

### Tab 2 — **Brain** (live 3D viz)

- Same gRPC stream the current `BrainWatchService` exposes; no change.
- Neurons rendered as nodes with their icons.
- Synapses as animated particles.
- Tap-to-inspect (see §14).

### Tab 3 — **Domains** (settings + connectors)

- One row per domain with: status, last error, connect/disconnect button.
- Google domain shows: signed-in account, scopes granted, "revoke" button.
- AI domain shows: configured providers, model list with capability tags.
- Data domain shows: list of SQLite databases on disk with size + last access.
- This tab is the canonical place to start the Google OAuth flow before
  speaking your first command.

### What we drop

- `creator_screen.dart` and `trip_planner_screen.dart` — replaced by the
  generic Home tab. Trip planning becomes a card type the AI emits.
- `flutter test` — see §16.

### Routing

`go_router` with three tabs, deep links for synapse inspection
(`/brain/synapse/:correlationId`), and OAuth callback for the rare case the
desktop receiver doesn't catch the redirect.

## 16. Testing — only `dotnet test`, no `flutter test`

> **You said:** "we should never use flutter test — only dotnet test which
> covers e2e neurons tests with up to flutter rfw visualization."

Three tiers of tests are defined, but all 440 tests must be executed **sequentially** and without stage-filtering flags (such as `@stage:fast`, `@stage:integration`, `@stage:e2e`).

Run the full test suite sequentially from the repository root:

```pwsh
dotnet test --max-parallel-test-modules 1
```

### Orleans Port Contention Fix & Global Test Parallelization Rules

- **Orleans Port Contention Fix**: Orleans uses loopback clustering ports (e.g., 11111, 30000) for local silo communications. When tests run in parallel, multiple test host processes spin up Orleans silos simultaneously and compete for the same loopback ports, leading to port collisions, connection failures, and flaky test runs. Running tests sequentially avoids this contention.
- **Global Test Parallelization Constraint**: xUnit test parallelization has been explicitly disabled globally at the assembly level to enforce single-silo execution isolation and prevent resource race conditions. This is defined in the assembly configurations using the following attribute:
  ```csharp
  [assembly: CollectionBehavior(DisableTestParallelization = true)]
  ```
  This attribute is non-negotiable and must exist in both:
  - `DigitalBrain.Test/AssemblyInfo.cs`
  - `kernel/DigitalBrain.Kernel.Tests/AssemblyInfo.cs`

Three tiers of tests were originally cataloged:
1. **Unit tests** (`@stage:fast`, no tag) — `DigitalBrain.Core.Tests`, mocked LLM, mocked Whisper. Run on every save. No external services. No Aspire.
2. **Domain BDD** (`@stage:integration`) — BDD tests in `DigitalBrain.Test`. Real IChatClient, real Whisper for AI domain; real Google API only behind environment credentials.
3. **End-to-end** (`@stage:e2e`) — `DigitalBrain.E2E.Tests` in `UI/`. Spins up the full Aspire `DistributedApplicationTestingBuilder`, launches the Flutter client in headless desktop mode, drives it via gRPC. Asserts on the RFW payload structure.

For the Flutter UI specifically, end-to-end tests assert on the contract,
not the rendered pixels:

```csharp
[Fact, Stage("e2e")]
public async Task Voice_request_for_email_senders_renders_a_data_table_card()
{
    await using var brain = await TestDigitalBrain.StartAsync();
    await brain.SimulateVoice("show me the last 5 email senders");
    var cards = await brain.AwaitRfwCards(count: 1, timeout: 30s);

    cards.Single().LibraryName.Should().Be("digitalbrain");
    cards.Single().RootWidget.Should().Be("DataTable");
    cards.Single().Data["rows"].AsArray().Should().HaveCount(5);
}
```

If we ever need to verify pixel rendering, that's a screenshot test driven by
the same dotnet runner using `puppeteer-sharp` or similar — still
`dotnet test`, never `flutter test`.

## 17. The end-to-end PoC scenario

This is the scenario that proves DigitalBrain works:

> **User (voice):** "I want you to be able to get my last 5 email senders and
> put them into a database."

The system must, with no further intervention:

1. **Transcribe** the voice via `Voice2TextNeuron` → `WhisperLargeV3Turbo`.
2. **Route** the transcript to the Creator neuron in the kernel.
3. **Plan**: Creator asks `Gpt5ReasoningNeuron` for a Gherkin spec. The LLM
   has the brain catalog as a tool; it picks `GoogleGmailNeuron` and
   `SqliteNeuron`. It writes the `.feature` file shown in §18.1 below.
4. **Validate**: Creator parses the `.feature` with `Gherkin` 39.0; valid.
5. **Generate** step definitions and the neuron implementation; compile both
   with Roslyn; both clean.
6. **Persist red**: write to `DigitalBrain.Domains.Dynamic/Generated/email-senders/`.
   Run the test in-process. Fails — neuron exists but Google not authorised
   yet.
7. **Surface auth**: Creator emits `GoogleAuthRequired`; Flutter shows a card
   "Connect Gmail to continue". User clicks, browser opens, consents.
   `DigitalBrainGrainDataStore` persists the token.
8. **Persist green**: re-run the test. Passes.
9. **Activate**: register the neuron. Brain viz adds a node with a `gmail`
   icon and a `sqlite` neighbor. Synapses flow.
10. **Run live**: kernel forwards the original transcript intent to the new
    neuron. It fetches messages, dedupes senders, upserts into `email-senders.db`,
    emits an RFW card with a `DataTable` showing the 5 rows.
11. **Render**: Flutter Home tab shows the card under the user's message.

Steps 1–10 must complete in well under a minute on the dev machine. Step 7 is
the only human-in-the-loop moment, and only the first time.

## 18. Example `.feature` files

Four flavors, illustrating different parts of the system.

### 18.1 Runtime-generated, the PoC scenario

```gherkin
@neuron:dynamic/email-senders
@telemetry:counter:emails_fetched
@telemetry:histogram:fetch_latency_ms
@requires:google.gmail
@requires:data.sqlite
@stage:integration
Feature: Cache the last 5 distinct email senders into a local SQLite database

  As a DigitalBrain user, I want my recent inbox senders cached locally so I can
  query them offline.

  Background:
    Given a fresh SqliteNeuron with database id "email-senders" and schema:
      """
      CREATE TABLE IF NOT EXISTS senders (
        email      TEXT PRIMARY KEY,
        first_seen TEXT NOT NULL,
        last_seen  TEXT NOT NULL,
        message_id TEXT NOT NULL
      );
      """

  Scenario: User has consented and inbox has at least 5 messages
    Given the user has authorised Google with scope "gmail.readonly"
    And  the inbox contains 7 messages from 5 distinct senders
    When I dispatch RefreshSenders to the dynamic neuron
    Then the SQLite database "email-senders" contains exactly 5 rows
    And  the counter "emails_fetched" was incremented by 5
    And  the histogram "fetch_latency_ms" recorded 1 sample

  Scenario: User has not yet consented
    Given the user has not authorised Google
    When I dispatch RefreshSenders to the dynamic neuron
    Then a "GoogleAuthRequired" synapse is emitted with scope "gmail.readonly"
    And  the SQLite database "email-senders" remains empty

  Scenario: Re-running keeps the cache idempotent
    Given the cache already contains 5 rows
    When I dispatch RefreshSenders again
    Then the SQLite database "email-senders" still contains exactly 5 rows
    And  no new "first_seen" timestamp was written
```

### 18.2 A core LLM-using neuron — first-class, in `DigitalBrain.Domains.Ai`

```gherkin
@neuron:ai/llm/openai/gpt-5
@capability:reasoning
@telemetry:histogram:llm_latency_ms
@telemetry:counter:llm_tokens_in
@telemetry:counter:llm_tokens_out
@stage:fast
Feature: gpt-5 reasoning neuron answers a chat request

  Scenario: Single-turn completion
    Given the LlmRequest synapse:
      """
      {
        "system": "You are concise.",
        "messages": [{"role":"user","content":"What is 2+2?"}],
        "model": "gpt-5"
      }
      """
    When the gpt-5 reasoning neuron processes the synapse
    Then a "LlmResponse" synapse is emitted with text matching "(?i)^4\\b"
    And  the histogram "llm_latency_ms" recorded 1 sample
```

### 18.3 Adding a new behavior to an empty domain (user-typed, not voiced)

```gherkin
@neuron:dynamic/morning-digest
@requires:google.calendar
@requires:google.gmail
@requires:ai.llm.reasoning
@stage:integration
Feature: Generate a one-paragraph morning digest at 7am local time

  Scenario: Compose digest from today's calendar and unread mail
    Given the time is 07:00 local
    And the user has authorised Google for calendar and gmail
    When I dispatch GenerateMorningDigest
    Then a "RfwCard" synapse is emitted with library "digitalbrain"
    And  the card root widget is "DigestCard"
    And  the card data has a "summary" string of at most 600 characters
```

### 18.4 Cross-domain — Travel calls AI calls Google

```gherkin
@neuron:travel/trip-planner
@requires:ai.llm.reasoning
@requires:google.calendar
@stage:integration
Feature: Plan a trip and add it to the user's calendar

  Scenario: Three-day Prague itinerary
    Given the TripPlanRequested synapse for "Prague" from "2026-06-01" to "2026-06-03"
    And the user has authorised Google for calendar
    When the trip planner neuron processes the synapse
    Then a "TripPlanReady" synapse is emitted with at least 3 day plans
    And  3 calendar events were created in the user's primary calendar
    And  an "RfwCard" synapse was emitted with library "digitalbrain" and root "TripCard"
```

## 19. Final project tree

```
E:\DigitalBrain
├── DigitalBrain.slnx
├── README.md
├── CLAUDE.md
├── Directory.Build.props
├── Directory.Build.targets
├── Directory.Packages.props
├── global.json
├── .lsp.json
├── .mcp.json
├── aspire.config.json
├── .gitattributes
├── .gitignore
├── .vscode/extensions.json
├── .config/dotnet-tools.json
├── .agents/skills/         (unchanged)
├── .claude/skills/         (unchanged)
├── .github/skills/         (unchanged)
│
├── docs/
│   ├── DIGITALBRAIN_RESEARCH.md         ← this file
│   ├── reqnroll.md                  (kept as historical reference)
│   ├── flutter-desktop-aspire-history.md
│   ├── reqnroll.md                  (kept as historical reference)
│   └── flutter-desktop-aspire-history.md
│
├── inolang/
│   ├── DigitalBrain.InoLang/              # core compiler / lexer / parser
│   ├── DigitalBrain.InoLang.TestRunner/
│   └── DigitalBrain.InoLang.Tests/
├── kernel/
│   ├── DigitalBrain.Core/                      # virtual actor core primitives
│   ├── DigitalBrain.Core.Hosting/              # silo registry and host implementations
│   ├── DigitalBrain.Boot/                      # genesis boot floor
│   ├── DigitalBrain.Kernel/                    # dynamic interpreted execution runtime
│   └── DigitalBrain.Kernel.Tests/              # sequential core test suite
├── sdk/
│   ├── DigitalBrain.SDK/                  # unified monolithic SDK assembly
│   │   ├── Ai/                            # consolidated AI connectors
│   │   ├── Aspire/                        # Aspire integrations
│   │   ├── Sqlite/                        # SQLite storage engine
│   │   ├── Stripe/                        # Stripe payment integrations
│   │   ├── Telegram/                      # Telegram webhook/alert integrations
│   │   └── XAI/Grok/                      # Grok connector
│   └── DigitalBrain.SDK.Contracts/        # consolidated contracts assembly
├── samples/
│   ├── DigitalBrain.Domains.Onboarding/        # onboarding sample domain
│   └── DigitalBrain.Domains.Travel/            # travel sample domain
├── UI/                                    # frontend client codebase
└── DigitalBrain.Test/                     # sequential SDK/connector test suite
```

**Diff summary** vs the historical tree:

- Consolidated multiple separate domain assemblies under `src/domains/` into a single monolithic `sdk/DigitalBrain.SDK` assembly (connectors) and `sdk/DigitalBrain.SDK.Contracts` assembly (contracts).
- Unified language compiler services under `inolang/` directory.
- Placed core virtual actor substrates, hosts, boot floors, and core runtimes under `kernel/` directory.
- Co-located all connector and SDK end-to-end integration tests under the unified sequential `DigitalBrain.Test` suite.
- Reorganized samples and client directories (`samples/`, `UI/`).

**Diff summary** vs the current tree:

- New folders: `src/core/`, `src/kernel/`, `src/domains/ai`, `src/domains/google`,
  `src/domains/data`, `src/domains/dynamic`, `src/testing/`.
- New files: `DIGITALBRAIN_RESEARCH.md` (this), `CLAUDE.md`, `SynapsePayload.cs`,
  `NeuronAttribute.cs`, `NeuronCapability.cs`, `GherkinValidator.cs`,
  `DigitalBrainGrainDataStore.cs`, all the AI / Google / Data domains.
- Deleted: `clients/flutter/test/`, `DigitalBrain.Kernel/CreatorNeuron` lives in
  `Creator/` subfolder (rename), no top-level `DigitalBrain.NeuronTesting/Internals`
  reorganisation needed.
- Moved: `DigitalBrain.Core` → `src/core/DigitalBrain.Core` (etc.) under a `core/` group;
  `DigitalBrain.Kernel` → `src/kernel/DigitalBrain.Kernel`.

## 20. Migration plan from current state

Six PRs, each merge-able and runnable on its own.

### PR 1 — `SynapsePayload`, neuron metadata, ServiceDefaults split

- Add `SynapsePayload` (JSON + bytes); migrate `SynapseRecord.Payload`.
- Add `[Neuron(Id, Icon, Capability)]` attribute and registry scanning.
- Split `ServiceDefaults` into `AddServiceDefaults` / `AddDigitalBrainDomain` /
  `AddDigitalBrainClient`.
- No domain changes yet. Travel keeps working.

### PR 2 — Folder reshuffle (no code changes)

- Move projects under `src/core`, `src/kernel`, `src/domains/travel`,
  `src/testing`. Update `DigitalBrain.slnx`. CI green.

### PR 3 — AI domain

- Create `DigitalBrain.Domains.Ai` + `.Contracts` + `.Tests`.
- Port `LlmNeuron` from `DigitalBrain.Core.Hosting.Llm` to the AI domain as
  `LlmNeuronBase` + concrete subclasses generated from marker types.
- Move `BddMockChatClient` to AI domain as a stage helper.
- Wire `Whisper.net` `WhisperSpeechToTextClient` for `Voice2TextNeuron`.
- AppHost: add `WithLlm<>().AsFast()` etc. fluent builder.

### PR 4 — Google domain (Gmail-only first)

- Create `DigitalBrain.Domains.Google` with `GoogleAuthBroker`,
  `DigitalBrainGrainDataStore`, `GoogleGmailNeuron`.
- Add Flutter `domains` tab with the Google connector card.
- Add E2E test that exercises the auth flow end-to-end with a fixture account.

### PR 5 — Data domain (SQLite) and Dynamic domain skeleton

- `SqliteNeuron` with per-instance `DbContext` factory.
- Empty `DigitalBrain.Domains.Dynamic` silo with grain-backed persistence loader.

### PR 6 — Creator runtime + the PoC scenario

- Promote the existing `Creator` from a stub to a working agent with the
  red-green loop.
- `GherkinValidator` calls `Gherkin` 39.0; Roslyn validation already in core.
- Add the `dynamic/email-senders` E2E test from §18.1 to
  `DigitalBrain.E2E.Tests`.
- Flutter Home tab gains voice input and RFW card feed; Brain tab gains
  feature/payload viewers; old screens removed.

After PR 6 the system is in its final form.

## 21. Risks, trade-offs, open questions

- **LLM reliability for code generation.** The Creator might produce
  syntactically valid C# that semantically misuses dependencies. Mitigation:
  the red-green loop, strict step contracts, automatic retry with feedback,
  and an N-attempt cap. Long-term: fine-tune on previously-generated neurons.
- **Token storage on Linux without keyring.** `libsecret` may not be
  available in all desktop environments. Fallback: master-password-encrypted
  blob, prompted on first launch. Document this clearly.
- **Reqnroll runtime in production assemblies.** Even with the
  silo/`*.Tests` split, the Dynamic silo runs Reqnroll at runtime to validate
  generated specs. This adds ~3 MB per silo and brings xUnit transitively.
  Acceptable for the PoC; revisit if size matters.
- **OAuth scope creep.** Each new Google neuron means another consent click.
  Consider a `WithGoogle()` umbrella that asks for the union upfront for the
  user's whitelisted neurons. Out of scope for v1 (your call: per-neuron
  scopes).
- **Dynamic neuron security.** Generated code runs in-process. A malicious
  prompt could try to exfiltrate. Mitigation: the only `IChatClient` and
  `GoogleAuthBroker` instances visible to a generated neuron are the ones
  the Creator explicitly grants via DI; we don't expose the full DI scope.
  Long-term: AssemblyLoadContext sandboxing with restricted permissions.
- **Brain viz performance.** Hundreds of synapses per second can swamp the
  3D scene. The current `BrainWatchService` already throttles; keep it.
- **Mobile.** Whisper.NET on iOS works (Metal runtime, static libs added in
  1.9), but model weights are large. For mobile Voice2Text we may want a
  cloud fallback. Out of scope for v1 (desktop only).
- **Cost.** Reasoning-class LLM calls during Creator runs are not cheap.
  Add a per-tenant budget; the brain catalog already tracks token counters
  per neuron via the telemetry hook.

---

**End of research document. See [`README.md`](../README.md) for build
instructions and [`CLAUDE.md`](../CLAUDE.md) for agent-facing guidance.**
