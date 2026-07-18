# ino — IAW Native OS

An AI-native OS built on three primitives: **neurons** (Orleans grains, LLM-optional), **synapses** (durable messages that are signal + memory + thinking at once), and a **self-improving loop** powered by Aspire. ino sits on top of the **IAW substrate** (`iaw/` subtree — Orleans + Aspire + Agent runtime); ino projects ProjectReference iaw assemblies, `AddIno()` delegates to `AddIAW()`, and `LlmNeuron<TEvent>` inherits `IAW.Core.Agent`. The transitional `POC/` wrapper folder was purged in favor of the flat layout under `src/`, `domains/`, `clients/`, `test/`.

Load-bearing docs:
- `docs/product-vision-final.md` — the 14 locked v0.1 decisions
- `docs/plan-poc-phase-3.md` — sliced implementation plan
- `README.md` — one-paragraph pitch

## Primitives — the facts that change daily behavior

### Neurons
Every capability is an Orleans grain. Neurons do NOT require an LLM — pure-code grains participate equally with LLM-powered ones. Two base classes ship in `Ino.Core.Hosting`:

- **`Neuron<TEvent>`** — pure-code, journal-event-driven. The base class is a `DurableGrain` with a keyed `IDurableList<EventEnvelope<TEvent>>` and a `RaiseAsync` helper. No `IChatClient` dependency — this is the LLM-optional path.
- **`LlmNeuron<TEvent>`** — inherits **IAW's `Agent`** (`iaw/src/Core/Agents/Agent.cs`) so it gets `IChatClient` streaming, AI tool registration, the tool-approval middleware, and durable chat history out of the box, then layers Neuron's journal-event API on top. Use this when a neuron needs to reason; otherwise stay on `Neuron<TEvent>`.

Both discover each other through synapses, not static references.

### Synapses — unified signal + memory + thinking
A synapse is a typed durable message. Three roles in one primitive:

- **Signal** — at-least-once message to another neuron via `IFirePort`. Direct request/response is reserved for cases where a receipt is needed; everything else fires.
- **Memory** — every synapse carries `decay ∈ [0,100]` (implementation pending). Nightly consolidation decays untouched synapses; access boosts them back. Memories ARE the messages — no separate store.
- **Thinking** — when branching/loops are needed, a synapse carries executable C# that calls other neurons. Full Turing power, local to the neuron's reasoning, not global orchestration.

### Self-improvement — L1 / L2 / L3
- **L1 — new persisted neuron.** Write a row to a cluster-wide registry with prompt + tool list + Roslyn script. Activates by ID. No silo restart.
- **L2 — reasoning-time C#.** Ephemeral Roslyn-compiled code inside one neuron's reasoning.
- **L3 — new compiled capability.** Rebuild silo + rolling restart. Human-gated.

Full derivation lives in `docs/product-vision-final.md`.

## Project layout

```
D:\ino\
├── CLAUDE.md                     (this file)
├── README.md
├── ino.slnx                      solution (the only one for ino)
├── Directory.{Build,Packages}.props
├── aspire.config.json            points the Aspire CLI at src/Ino.AppHost
├── docs/                         vision + plan + design notes
├── reviews/                      demo hero screenshots
├── iaw/                          IAW substrate — Orleans Agent runtime + Aspire integration
│   ├── src/Core                  IAW.Core — Agent base class, AgentDurableState, IChatClient pipeline
│   ├── src/Aspire.Hosting        Aspire.Hosting.IAW — AddIAW(), WithLLM<T>, IAWService used by AddIno
│   ├── src/Aspire.Client         Aspire.IAW.Client — silo AddIAW + client AddIAWClient (OTel, health)
│   ├── src/Agents, Agents.CSharp out-of-the-box agents (memory, code-orchestration, Roslyn/Git/NuGet)
│   ├── src/Testing               IAW.Testing — AgentTest<TAgent> with TestCluster + MockChatClient
│   └── src/Aspire, Agents.Host, MCP, DevUI, Telegram   legacy iaw entrypoints, kept compiling but unreferenced from Ino.AppHost
├── src/
│   ├── Ino.Core                  Neuron base, Synapse primitives, contracts (Caller, DomainId, ISynapse, …)
│   ├── Ino.Core.Hosting          Cluster runtime — Orleans wiring, LLM provider abstraction, FirePort/AmbientFire/CapabilityEnforcer/Discovery client + AddDomain extension shared by every silo. Hosts both Neuron<TEvent> (pure-code) and LlmNeuron<TEvent> : IAW.Core.Agent (IChatClient-backed)
│   ├── Ino.Llm.Xai               xAI provider (XaiProviderFactory + Grok model declarations) — one Ino.Llm.<Provider> assembly per provider, auto-discovered by AddInoChatClients via assembly scan of declared model types
│   ├── Ino.Kernel                kernel silo — hosts the gateway, Cortex routing, Discovery grain, gRPC + Flutter wwwroot. Marker class `Kernel : IDomain` (DomainId "kernel"); peer to Identity/Travel/Taxi
│   ├── Ino.Kernel.Contracts      Synapse contracts shared with Ino.Gateway (ChatIntent, Echo*)
│   ├── Ino.Identity              identity silo (Worker SDK). Marker class `Identity : IDomain`
│   ├── Ino.Gateway               IInoGateway + default InoGateway
│   ├── Ino.Gateway.Grpc          gRPC + gRPC-Web + static file serving
│   ├── Ino.ServiceDefaults       OTel + health wiring
│   ├── Ino.Aspire.Hosting        AddIno / WithDomain extensions. AddIno() internally calls AddIAW() and exposes the IAWService via InoBuilder.Iaw so silos can chain .WithReference(ino.Iaw)
│   ├── Ino.AppHost               Aspire entrypoint — boots kernel + identity + per-domain silos
│   └── Ino.Testing               TestCluster + MockLlm base
├── domains/
│   ├── testing/                  fixture domains used by tests
│   ├── taxi/                     Ino.Domains.Taxi — Worker SDK silo + IDomain marker `Taxi : IDomain` (Uber MCP integration; scaffold-only for v0.1)
│   └── travel/                   Ino.Domains.Travel — Worker SDK silo + IDomain marker `Travel : IDomain` (TripRadar integration)
├── clients/
│   ├── ino.flutter/              Flutter web/mobile client (CanvasKit, gRPC-Web, BLoC, GoRouter, OTel)
│   │   └── assets/rive/persona_orb.riv   marketplace Rive asset
│   └── Telegram/                 Ino.Telegram.Host — bot serves Flutter mini-app + transcribes voice locally + forwards to kernel silo over gRPC
├── test/                         kernel-level e2e + infrastructure tests (per-domain neuron e2e tests live under domains/<x>/<x>.Tests)
├── domains/travel/tripradar/     external product Travel integrates with (own Aspire, independent build; merged into ino.slnx)
└── .config/dotnet-tools.json     csharp-ls etc.
```

`iaw/` is in the tree as the runtime substrate (added back in commit 582ea3c). `POC/`, `features/`, `tests/`, `website/`, `ino.windows/`, `ino.telegram/` were legacy and purged. There is one solution (`ino.slnx`) — `iaw/IAW.slnx` is dormant; iaw inherits ino's `Directory.Build.props` + `Directory.Packages.props` (iaw's were deleted on integration).

## IAW substrate — what AddIno boots under the hood

`AddIno(builder, name)` is a thin wrapper that calls `builder.AddIAW(name)` and stashes the returned `IAWService` on `InoBuilder.Iaw`. AddIAW provisions:

- **Orleans cluster** (`OrleansService` from `Aspire.Hosting.Orleans`) with dev clustering + memory storage + memory streaming + memory reminders.
- **Azure Blob Storage** emulator (`iaw-storage` resource → `file-storage` blob) for `BlobFileStorage` ingestion.
- **Qdrant** (`qdrant` resource) for vector recall used by `IawMemoryProvider`.
- **API key parameters** — the dashboard prompts for `github-token`, `anthropic-api-key`, `openai-api-key` only when the corresponding `.WithLLM<T>()` is declared.

Downstream silos pick up the substrate via `silo.WithReference(ino.Iaw)` (env block + Orleans membership + WaitFor wiring). Legacy ino silos that already use ino's localhost clustering keep working unchanged — pulling in `WithReference(ino.Iaw)` is opt-in per silo.

## Domains — the v0.1 set

Two e2e domains ship in v0.1:

| Domain | Role | Integration |
|---|---|---|
| `Ino.Domains.Travel` | trip planning, flights, hotels, places | **TripRadar** (external service at `domains/travel/tripradar/`, HTTP/gRPC for v0.1) |
| `Ino.Domains.Taxi` | ride-hailing | **Uber via MCP** with user's Google auth (if a real MCP server exists; scaffold-only otherwise) |

Full domain decomposition in `docs/product-vision-final.md` decision 13.

## Clients — multi-surface, shared Flutter codebase

| Client | Renderer | Notes |
|---|---|---|
| Flutter web (primary) | CanvasKit (Skia) | served from `Ino.Kernel/wwwroot/` |
| Telegram bot + mini-app | reuses Flutter web | `clients/Telegram/` (`Ino.Telegram.Host`); `/start` sends a WebApp button (and the persistent chat-menu button) that loads the same Flutter bundle from this host's HTTPS origin. Voice messages get transcribed locally via Foundry Local Whisper, then forwarded with text messages to the kernel silo over gRPC. Bot token comes from the `telegram-bot-token` Aspire parameter (dashboard prompts on first run, persists to user-secrets); webhook URL is injected from the cloudflared tunnel. The host boots without the token and no-ops the bot loop until the parameter is filled. |
| ino-windows desktop (planned) | Flutter desktop | legacy `ino.windows/` was purged; desktop hookup is a later slice |

The Flutter app builds once, deploys everywhere. Persona rendering uses the Rive asset at `clients/ino.flutter/assets/rive/persona_orb.riv` with CustomPaint fallback.

## Working environment

### Build & test

Run from `D:\ino\`:

```bash
dotnet build ino.slnx
dotnet test ino.slnx
```

**NEVER use `dotnet run --project` to start ino.** Always go through the Aspire CLI. Two valid entrypoints, pick by lifecycle:

```bash
# Foreground — runs in the current terminal, Ctrl+C to stop. Use when you want
# to watch logs interactively or own the process for the duration of a session.
aspire run

# Background service — detaches and runs as a service; terminate with `aspire stop`.
# Use when you want the AppHost up while you do other work in the terminal.
aspire start --isolated
aspire stop
```

The repo has `aspire.config.json` at the root pointing at `src/Ino.AppHost/Ino.AppHost.csproj`, so neither command needs `--apphost` from `D:\ino`. Pass `--apphost src/Ino.AppHost/Ino.AppHost.csproj` only when invoking from a different working directory.

To restart individual resources after code changes, use the Aspire MCP tools — don't stop/start the whole AppHost:

```
mcp__aspire__execute_resource_command(resourceName="kernel", commandName="rebuild")
mcp__aspire__execute_resource_command(resourceName="identity", commandName="rebuild")
mcp__aspire__execute_resource_command(resourceName="travel", commandName="rebuild")
mcp__aspire__execute_resource_command(resourceName="taxi", commandName="rebuild")
```

Available commands per resource: `rebuild` (stop + build + restart), `restart` (restart only), `stop`, `start`.

### Verification loop — per slice, never skip

Type checks and test suites verify code correctness, not feature correctness. If the UI can't be driven in a browser, say so explicitly — never claim success on build + test alone.

1. `dotnet build ino.slnx`
2. `dotnet test ino.slnx`
3. `aspire run` (foreground) or `aspire start --isolated` (background) — confirm every resource Healthy in the dashboard
4. Open the kernel-silo HTTPS URL in Chrome (via Chrome DevTools MCP). Drive the scenario. Check Aspire **Structured Logs** (filter `ino-flutter` → BLoC transitions) and **Traces** (`grpc Chat`, `fire` / `handle` spans linked by `traceparent`). Cross-domain trace filtering: each domain silo emits its own `service.name` (`Ino.Domains.Travel`, `Ino.Domains.Taxi`, `Ino.Kernel`, `Ino.Identity`), so filter by service in the Traces tab to isolate one domain.
5. E2E with browser-rendered Flutter UI — `dotnet test test/Ino.E2E.Tests` (defaults to headed Chromium; CI=true flips to headless). Per-domain neuron tests live alongside their domain (`domains/<x>/Ino.Domains.<x>.Tests`).
6. Iterate via `mcp__aspire__execute_resource_command(resourceName="…", commandName="rebuild")` — resource names are `kernel`, `identity`, `travel`, `taxi`, `telegram`.

### Non-negotiable first-run story

These two commands must yield a working demo from a completely clean repo:

```bash
git clean -fdx
aspire run            # or `aspire start --isolated` for background
```

The kernel-silo HTTPS URL must serve the Flutter app with zero manual build steps. The `Ino.Kernel` build target auto-builds Flutter web and copies `build/web/*` into `wwwroot/` when `flutter` is on PATH.

### Flutter OpenTelemetry — verify UI changes end-to-end

The Flutter app has full OTel instrumentation exporting traces, logs, metrics to the Aspire dashboard. **After any Flutter change, use this telemetry to verify the feature works end-to-end** — don't just check that it compiles.

- `lib/telemetry/grpc_interceptor.dart` — every gRPC call gets a span with W3C `traceparent` propagation
- `lib/telemetry/bloc_observer.dart` — BLoC events/transitions/errors via OTel
- Pre-registered metrics: `ino.grpc.requests`, `ino.grpc.duration`, `ino.chat.messages`, `ino.errors`

After Flutter changes:
1. `cd clients/ino.flutter && flutter build web --no-tree-shake-icons` (or let the MSBuild target do it on `dotnet build`)
2. `mcp__aspire__execute_resource_command(resourceName="kernel", commandName="rebuild")`
3. Open the kernel-silo URL, interact
4. Check Aspire Structured Logs for `ino-flutter` category + Traces for the four-span chain

Flutter 3.41 removed `--web-renderer html` — only CanvasKit (default) and Wasm are available. CanvasKit renders to `<canvas>`, so DOM text queries don't work for testing. Use gRPC-Web response interception + screenshots.

## Context7 — mandatory before writing library-touching code

Resolve and query Context7 before writing code that touches a library / framework / SDK / CLI. No exceptions. Especially critical for:

- **Orleans 10** — journaling, reminders, placement filters, cluster membership. Plain grain calls are NOT durable without journaling.
- **Aspire 13** — AppHost topology is frozen after `Build()`. Orleans grain manifests are cluster-wide at silo startup.
- **Flutter web + CanvasKit** — bundle setup, renderer selection, Rive state-machine introspection.
- **RFW (Remote Flutter Widgets)** — parser rejects Windows CRLF; always strip `\r` before sending.
- **ML.NET / LightGBM** — POC-level ML wiring (see slices 2 + 3).
- **Reqnroll** — BDD framework; .feature files double as dev mocks for `BddMockChatClient`.

## Known traps — don't re-hit

- **`<>z__ReadOnlyArray<T>`** triggers `CodecNotFoundException` on cross-silo deep-copy. Use concrete `T[]` for any `IReadOnlyList<T>` field in a `[GenerateSerializer]` record.
- **`cluster.GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix: Type.FullName)`** doesn't work — prefix is matched against Orleans' source-generated `GrainType.Name`, not `Type.FullName`. Use interface-only resolution, or `[Alias("…")]` the neuron and pass the alias.
- **`AddPlacementFilter<TFilter, TDirector>`** has a `new()` constraint on `TFilter` — strategy is reconstructed on each silo. Custom state must flow through grain properties via `AdditionalInitialize(GrainProperties)`, NOT via `[GenerateSerializer]` fields (see `PinToSiloPlacementFilter.cs`).
- **Aspire rebuild changes `ContentRootPath`** vs initial launch. `InoGrpcHostingExtensions.ResolveWwwroot` already probes three roots — don't regress that.
- **gRPC-dart `ResponseStream<T>` is single-subscription.** Telemetry interceptors can't `response.listen()` AND hand the stream back — use `response.trailers` or a `StreamTransformer` for side effects.
- **`Microsoft.NET.Sdk.Web`** serves `wwwroot/` from project dir in dev but NOT from bin. After `git clean -fdx` the source wwwroot is empty; the MSBuild target must populate it before Aspire launches the silo.
- **Aspire dashboard OTLP endpoint** uses HTTPS with a self-signed cert + API-key header; the gateway's `/otlp/v1/*` proxy must forward both correctly or it 502s.
- **Orleans 10.1 DurableJobs API renames.** When bumping Microsoft.Orleans.DurableJobs to 10.1.0-alpha.1, `IDurableJobContext` → `IJobRunContext` and `ScheduleJobAsync(grainId, name, dueTime, metadata, ct)` → `ScheduleJobAsync(new ScheduleJobRequest { Target=…, JobName=…, DueTime=…, Metadata=… }, ct)`. Already migrated in `iaw/src/Core/Agents/Agent.Scheduling.cs`.
- **iaw/Telegram Foundry pin.** `iaw/src/Telegram/Telegram.csproj` carries `VersionOverride="0.9.0"` for `Microsoft.AI.Foundry.Local` because its transcription service uses the 0.9 `ModelVariant` API (internalized in 1.0). ino's `clients/Telegram` is on 1.0.0 — leave it alone.
- **iaw/Aspire RID leak.** `iaw/src/Telegram/Telegram.csproj` pins `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` for Concentus/NAudio native deps; that propagates into `iaw/src/Aspire/Aspire.csproj`, which therefore declares `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>` so its assets file resolves correctly.

## Commit discipline

- Granular commits per slice: `feat(poc):` / `test(poc):` / `fix(poc):` / `docs:` / `chore:` / `refactor(poc):`.
- One logical change per commit.
- Never skip hooks (`--no-verify`) or bypass signing unless the user asks.
- Don't use destructive git commands (`reset --hard`, `push --force`, `checkout --`) without confirming first.

## Out of scope for Phase 3

Don't scope-creep into:
- Cortex self-creation (L1/L2/L3)
- Cross-user missed-intent aggregation
- Full multifractal spectrum research
- Revenue model decisions
- Domains beyond Travel + Taxi
- Telegram / ino-windows full migration

If a Phase 3 review comment pushes toward any of these, defer with a link to the post-v0.1 epic list in `docs/product-vision-final.md`.
