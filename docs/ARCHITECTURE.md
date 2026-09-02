# DigitalBrain — Ratified Architecture (2026-08-22)

Decisions ratified by the owner on 2026-08-22. Code is the source of truth; this
records intent the code cannot show yet. Supersedes all PersonaPlex-era plans.

## Product

Multiuser chat product. One kernel image, one Flutter codebase, entities all the
way down. Core differentiator: **the neuron substrate itself** — a durable, weighted
graph of neurons connected by synapses, routing signals by learned strength rather
than by name. User-authored automations are generated C# neurons (see
[Automations](#automations) below), not a second, English-language runtime.

## Core loop (chat + dynamic UI)

1. Flutter → `IChat.Send` → durable turn → `ChatTurnWorker` (already built).
2. Worker → `IAssistant` → a Microsoft.Extensions.AI `IChatClient` (the
   configured default model or an explicit marker) with an AI toolset.
3. Every UI-kit component registers an AI tool (`render_chart`,
   `generate_image`, …) via `TurnBoundFunction`. A tool call creates/updates an
   `Entity<TState>` and posts a **reference card** `{componentKind, entityId,
   caption}` into the transcript — never a snapshot.
4. SSE pushes the card; Flutter mounts the matching kit widget, which reads the
   entity via its `[ClientEntryPoint]` contract. The same entity renders
   full-size on a Surface — one live state, two mounts.
5. Interactive components (Button, Form, Todo) fire their command signal back
   through the same path a user message takes.
6. Every component state record lands in `flutter-wire-contracts.golden.json`;
   a conformance test fails on C#↔Dart drift.

## UI kit — 13 components, one wave

All follow the identical template: `Entity<TState>` + `[ClientEntryPoint]`
contract + Flutter widget + golden wire contract. Binary payloads (Image, File)
live in Azure Blob Storage; the entity holds the reference.

MarkdownCard, Chart, Image (multi-provider generation), Button, Table, Form,
TodoList, CodeCard, ProgressCard, TimerCard, Browser (embedded webview),
FileCard, Diagram.

The existing `digitalbrain_ui_kit` Dart package (KitChartPart, KitButtonPart,
KitGalleryScreen) is the starting point for the widget side.

## AI module

- **Providers**: port IAW's provider-factory layer (E:\intochat\Projects\IAW)
  behind DigitalBrain's marker DSL — AppHost declares `WithLlm<IModel>()`, each
  marker maps to a keyed MEAI `IChatClient`. Providers: OpenAI (top-3 current
  flagship line), Anthropic (Opus/Sonnet/Haiku current), Google (Gemini
  Pro/Flash current), xAI (Grok). Exact model ids pinned against provider docs
  at implementation time.
- **Segregation** (no tiers — vetoed): model markers are pure types
  (`IOpus5 : ILLM`) that select keyed `IChatClient`s; agents (`Agent` base:
  instructions + tools, model chosen via `[Llm<TModel>]`) are the only
  conversational citizens. The unkeyed default client follows
  `DigitalBrain:AI:Default:Model`, else the first configured provider (cloud
  before local).
- **Local dev**: Ollama `IGemma4` + `IEmbeddingGemma` stay so dev and CI run
  offline. Production embeddings come from a cloud provider; embedding
  dimensions are config-driven because Qdrant index dims lock to them.
- **Agent layer**: `Agent` neurons over MEAI clients today; MAF orchestration
  (Team/GroupChat/MafParticipantAdapter) restores from master's git history as
  a later build-order step.
- **Voice**: Whisper STT (Foundry Local) stays dev-only. PersonaPlex is deleted
  (see Trash record); future voice = provider realtime APIs.

## Automations

Retired in favour of generated C#. See
[2026-09-02-digitalbrain-v2-neuron-substrate-design.md](superpowers/specs/2026-09-02-digitalbrain-v2-neuron-substrate-design.md)
§9.3 — an automation is a neuron, authored by the system and compiled against module contracts.

## Integration modules

`Modules/Google` and `Modules/Salesforce`, each the standard triple
(Contracts / implementation / Aspire.Hosting). Both sit on `Kernel/DigitalBrain.Sdk`:
`Sdk/Mcp` owns the hosted MCP tool client (per-owner sessions, bearer auth, catalog
check, result normalization, the single read-only retry), `Sdk/OAuth` the browser
login rail (`BrowserLogins` one-use request registry, `BrowserLoginSurface` for the
login/callback paths, correlation claim, completion worker), `Sdk/Http` the
`IHttpSurface` seam through which a module maps its callbacks without the kernel
naming it. A module keeps only its provider policy: OAuth scheme and events, the
credential store behind `IMcpCredentials`, tool definitions and confirmations.
Each module's `Aspire.Hosting` project declares its own operator parameters
(`WithGmail()`, `WithHostedMcp()`) and projects them onto the kernel; fakes mode
declares none.

- Per-user OAuth: `AccountEntity` per user per provider holds the refresh
  token; kernel HTTP serves the callback; every neuron call resolves the
  caller's token. No shared credentials.
- v1 surface: Gmail search/read/draft/send*, Calendar list/create*, Salesforce
  SOQL/read/create*. `*` = a confirmation Button card in chat must be activated
  before the mutation executes.
- Both modules publish tool descriptions as module contracts that generated
  automation neurons compile against — no separate capability catalog.

## Multiuser

- Login + password only (registration included). `UserAccountEntity` per user:
  username key, password hash in state. No external IdP, no EF, no relational
  DB.
- Sessions: ASP.NET Core cookie (web, same-origin) **and** bearer token issued
  at login — both from day one so native clients never depend on cookies.
- `OwnerId` keying and `VerifiedActor` (already in the kernel) carry identity
  through the grain graph.

## Deployment

- **Product = one Docker image** (kernel, built from
  `src/Kernel/DigitalBrain.Silo/Dockerfile`) published to Docker Hub. The
  built Flutter web app is baked into this image and served by the kernel —
  same-origin cookies and SSE by construction.
- **Runtime**: Azure Container Apps pulls the image. Scale = silo replicas.
- **Secrets**: Azure Key Vault → injected env (`DigitalBrain__*` keys already
  stubbed in docker-compose.yml).
- **State**: Azure Storage (Orleans clustering / grain state / journaling /
  reminders + blobs for Image/File components). Qdrant runs as an external
  container with a persistent volume.
- **No GPU in production**: Ollama / Whisper / Foundry Local are dev-only;
  cloud providers serve all production inference.
- **Clients**: web ships in the image; Windows/mobile are Flutter build targets
  against the same API using the existing `cookie_http_client` or the bearer
  token.

## Trash record (deleted 2026-08-22, recoverable from git history)

- PersonaPlex, entirely: `src/Modules/AI/PersonaPlex`, contracts, hosting
  extensions, kernel WebSocket endpoint + protocol, `src/Runtime/PersonaPlex`
  python runtime, Flutter voice client/protocol/screens/controller, the shell's
  Voice destination, `flutter_soloud` dependency and web bootstrap scripts, all
  PersonaPlex tests, and the PersonaPlex-era plans/specs/research docs.
- Rationale: the runtime never worked (branch history ends "Not working 2" /
  "Disable personaplex"), needed a GPU the deployment target doesn't have, and
  8 of its E2E tests were failing at HEAD.

## Build order

1. AI providers (IAW port, no tiers) — shipped 2026-08-22.
2. Auth (UserAccountEntity, cookie + token) — multiuser boundary.
3. UI kit, all 13 components on the template. (template + Chart + Image shipped 2026-08-23)
4. Automations (generated C# neurons: compile chain, sensors/effectors, discovery).
5. Google + Salesforce modules.
6. MAF orchestration restore.
7. Image → Docker Hub, ACA + Key Vault deploy.
