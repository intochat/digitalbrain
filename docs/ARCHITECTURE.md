# DigitalBrain — Ratified Architecture (2026-08-22)

Decisions ratified by the owner on 2026-08-22. Code is the source of truth; this
records intent the code cannot show yet. Supersedes all PersonaPlex-era plans.

## Product

Multiuser chat product. One kernel image, one Flutter codebase, entities all the
way down. Core differentiator: **Smart Prompts** — user-authored plain-English
automations with `@Module` bindings.

## Core loop (chat + dynamic UI)

1. Flutter → `IChat.Send` → durable turn → `ChatTurnWorker` (already built).
2. Worker → `IAssistant` → keyed Microsoft.Extensions.AI `IChatClient`
   (tier or explicit model) with an AI toolset.
3. Every UI-kit component registers an AI tool (`render_chart`,
   `generate_image`, …) via `TurnBoundFunction`. A tool call creates/updates an
   `Entity<TState>` and posts a **reference card** `{componentKind, entityId,
   caption}` into the transcript — never a snapshot.
4. SSE pushes the card; Flutter mounts the matching kit widget, which reads the
   entity via its `[ClientEntryPoint]` contract. The same entity renders
   full-size on a Surface — one live state, two mounts.
5. Interactive components (Button, Form, Todo) fire their command synapse back
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
- **Tiers**: `fast` / `balanced` / `reasoning` aliases so neurons ask for a
  tier, not a vendor.
- **Local dev**: Ollama `IGemma4` + `IEmbeddingGemma` stay so dev and CI run
  offline. Production embeddings come from a cloud provider; embedding
  dimensions are config-driven because Qdrant index dims lock to them.
- **Agent layer**: Microsoft Agent Framework. Assistant and SmartPromptRunner
  are MAF agents; master's orchestration layer (Team/GroupChat/
  MafParticipantAdapter) is restored from git history and modernized.
- **Voice**: Whisper STT (Foundry Local) stays dev-only. PersonaPlex is deleted
  (see Trash record); future voice = provider realtime APIs.

## Smart Prompts

`SmartPromptEntity`: name, plain-English prompt, resolved bindings, trigger,
enabled, run refs.

- `@Gmail` in a prompt = hard binding to the Google module's `IGmail` neuron
  toolset — deterministic, no retrieval.
- Unpinned capability comes from vector retrieval: every module publishes tool
  descriptions into a Qdrant capability-catalog collection; the runner
  retrieves top-k tools for the prompt text.
- `SmartPromptRunner` neuron (ChatTurnWorker pattern) executes with exactly the
  bound + retrieved toolset; progress via ProgressCard; output cards land in
  the owning chat.
- Triggers v1: manual, scheduled (Time module), chat-invoked. Event-driven
  (Gmail push etc.) is phase 2.
- Creation: by hand (Form component) or by asking the chat
  (`create_smart_prompt` tool). Same entity either way.

## Integration modules

`Modules/Google` and `Modules/Salesforce`, each the standard triple
(Contracts / implementation / Aspire.Hosting).

- Per-user OAuth: `AccountEntity` per user per provider holds the refresh
  token; kernel HTTP serves the callback; every neuron call resolves the
  caller's token. No shared credentials.
- v1 surface: Gmail search/read/draft/send*, Calendar list/create*, Salesforce
  SOQL/read/create*. `*` = a confirmation Button card in chat must be activated
  before the mutation executes.
- Both modules publish tool descriptions into the Smart Prompts capability
  catalog.

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
  `src/Kernel/DigitalBrain.Kernel/Dockerfile`) published to Docker Hub. The
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

1. AI providers + tiers (IAW port) — chat becomes real against cloud models.
2. Auth (UserAccountEntity, cookie + token) — multiuser boundary.
3. UI kit, all 13 components on the template.
4. Smart Prompts (entity, catalog, runner, triggers).
5. Google + Salesforce modules.
6. MAF orchestration restore.
7. Image → Docker Hub, ACA + Key Vault deploy.
