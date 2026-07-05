# DigitalBrain — Architecture Overview & Production Plan

Date: 2026-07-05
Scope: `brain/` repo (prototype → production), referencing production patterns proven in `TripRadar/`.
Azure target: existing `digitalbrain-rg` (westeurope).

---

## 1. Current State — Architecture Overview

### 1.1 Solution shape

`Brain.slnx` is the single canonical solution (~33 projects). Logical layers:

| Layer | Projects | Notes |
|---|---|---|
| Orchestration (Aspire) | `NeuroOSPrototype.AppHost`, `NeuroOSPrototype.ServiceDefaults`, `DigitalBrain.Aspire` | AppHost is thin; all wiring lives in the `DigitalBrain.Aspire` DSL ("plugin" layer) |
| Runtime host | `DigitalBrain.Kernel` | Orleans silo + gRPC/gRPC-Web gateway + web bundle host — the one deployable backend image |
| Stable contracts | `DigitalBrain.Core`, `DigitalBrain.Pack.Contracts`, `DigitalBrain.Marketplace.Contracts`, `DigitalBrain.Ui.Contracts`, `DigitalBrain.Demo.Contracts` | Phase-2 Core split in progress (see ARCHITECTURE_CLEANUP_PROPOSAL.md) |
| Runtime libs | `DigitalBrain.Context` (RAG/hybrid scorer), `DigitalBrain.Ui.Runtime`, `DigitalBrain.Demo.Runtime`, `DigitalBrain.UiKit`, `DigitalBrain.SeedPacks` | |
| Integrations (packs) | `DigitalBrain.Telegram[.Channel/.Transport]`, `DigitalBrain.Google`, `DigitalBrain.Salesforce`, `DigitalBrain.Windows`, `DigitalBrain.Developer`, `DigitalBrain.Mcp`, `DigitalBrain.Experience.PersonalAssistant` | Telegram.Transport is a second deployable |
| Clients | `app/` (Flutter: windows, web, mobile shells) | `Flutter.proj` referenced by slnx; `SkipFlutterBuild=true` in headless CI |
| Deploy | `deploy/` (Pulumi.AzureNative, ~330 lines) | Replaced the 33k-LOC vendored DeploymentKit |
| Tests | 12 `*.Tests` projects + `DigitalBrain.TestKit` | Maintained, not stubs |

### 1.2 Aspire hosting integration (the "plugin" model)

`AppHost.cs` is intentionally ~80 lines. Everything is composed via `DigitalBrain.Aspire`:

- `builder.AddDigitalBrain("digitalbrain", options => ...)` provisions: Azure Storage (`RunAsEmulator()` → Azurite locally), Orleans (`WithClustering(table)` + `WithGrainStorage("Default", blobs)` + journal blobs), and an **always-on Ollama container** (`WithGPUSupport().WithDataVolume().WithOpenWebUI()`) pulling the fallback model `qwen2.5-coder:1.5b`.
- Typed model registry DSL: `options.WithLLM<Qwen25Coder1_5B>()`, `WithVoice2Text<Whisper1Local>()`, `WithEmbedding<T>()`, `WithVectorDatabase(...)`, with routing roles `AsFast()/AsBalanced()/AsReasoning()`. Models are typed markers in `LlmModels.cs`, not raw strings. Azure OpenAI switch = `WithLLM<Gpt4oMini>()` + two secret parameters.
- `ctx.WireKernelSilo(kernel)` — injects Orleans refs, LLM/voice/model-registry env vars, grpc + web endpoints, `WithReplicas(3)` for local HA.
- `ctx.AddDefaultDevFlutterClient(kernel)` / `AddFlutterClient(...)` — runs `flutter run -d windows` as an `ExecutableResource` on `aspire run`.
- `ctx.WireTelegramTransport(...)` — boots no-op without a token; token is an optional secret parameter or supplied later via in-app config, no AppHost restart.
- `builder.AddSalesforceAppConfig()` — separate extension file.

This is a good pattern and the right seam for "packs provide their own Aspire bits". Cleanup items for this layer are in §2.3.

### 1.3 Orleans runtime — dual-mode kernel

`DigitalBrain.Kernel/Program.cs` detects mode via `ConnectionStrings__clustering|grainstate|journal` env vars (`isAspireHosted`):

| | Fast path (`dotnet run`) | Aspire/cloud path |
|---|---|---|
| Clustering | `UseLocalhostClustering()` | `UseAzureStorageClustering` (Table `OrleansSiloInstances`) |
| Grain storage | Memory | `AddAzureBlobGrainStorage("Default")` |
| Journal | In-memory prototype journals | `AddAzureBlobJournalStorage`, `orleans-binary` format (JSON journal format proven broken by spike — see `DigitalBrain.Tests/Spikes/`) |
| Cluster identity | — | `Orleans:ClusterId/ServiceId` = `digitalbrain` (stable rejoin) |

Both paths: memory streams (`HomeFeed`, `DigitalBrainTimeline`), memory `PubSubStore`, Foundry, gRPC gateway (`DigitalBrainGateway`/`UiGateway`) with gRPC-Web + CORS for browsers, and optional static web bundle serving via `DIGITALBRAIN_WEBROOT`.

Important: locally under `aspire run` the same Azure providers are used against the **Azurite emulator**, so local and cloud exercise identical Orleans persistence code. This is a real strength — keep it.

### 1.4 Local ML (offline-functional guarantee)

- **LLM**: provider abstraction `ollama | azureopenai` (`DigitalBrain__Llm__Provider/Model/OllamaEndpoint/AzureOpenAI*`). Ollama always runs as fallback regardless of primary provider.
- **Voice-to-text**: `Whisper1Local` — any OpenAI-compatible `/audio/transcriptions` endpoint, activated only when `DigitalBrain:Voice:Endpoint` / `DIGITALBRAIN_VOICE_ENDPOINT` is set. Optional by design.
- **Embeddings**: **not real yet.** `NoOpEmbeddingGenerator` emits 384-dim zero vectors; `HybridScorer` detects zero vectors and falls back to keyword recall. The DSL (`WithEmbedding<T>`) and the swap point (`IEmbeddingGenerator`) already exist — wiring a real local model activates vector RAG with no code change. This is the single biggest functional gap in the "local ML" story.

### 1.5 Flutter client

`app/` full Flutter workspace. Local: launched by Aspire as windows desktop client. Web: `deploy-flutter-web.yml` builds `flutter build web --release` and publishes to **GitHub Pages** at `digitalbrain.tech` (CNAME in `app/web/`). Client talks to kernel via gRPC-Web to the kernel's external ingress.

### 1.6 Current deployment (digitalbrain-rg)

Pulumi.AzureNative program (`deploy/Program.cs`), stack `dev`, state in `azblob://pulumi-state` (in `digitalbrainstprod`). Live footprint (10 Pulumi / 8 Azure resources):

- `digitalbrain-rg` (westeurope)
- `digitalbrainstprod` StorageV2 — Orleans Table clustering + Blob grainstate/journal + Pulumi state
- `digitalbrainopenaiprod` (S0) + deployment `chat` = gpt-4o-mini GlobalStandard cap 10
- `digitalbrain-log-prod` + `digitalbrain-ai-prod` (Log Analytics + App Insights)
- `digitalbrain-cae-prod` ACA managed environment
- `digitalbrain-jobs` container app — **the kernel silo**, external ingress `Auto` (HTTP/1.1 + h2 → gRPC-Web + native gRPC), port 8080, 1 CPU / 2Gi, scale 1–5, secrets: storage conn string, OpenAI key, checkpoint AES key
- `digitalbrain-telegram` container app — external `/webhook` ingress, 0.25 CPU / 0.5Gi, scale 1–3

CI/CD: `deploy.yml` on push to master → `dotnet test Brain.slnx` (skip Flutter, skip E2E) → `dotnet publish -t:PublishContainer` → **public Docker Hub** `vhorbachov/digitalbrain-kernel` → Azure OIDC login → `pulumi up`. All prod deploys go through GitHub Actions only.

**Known incomplete (from DEPLOY-STATUS.md):**

1. Azure OIDC app registration not created (`AZURE_CLIENT_ID` is a placeholder) — deploy workflow cannot actually run.
2. `DOCKERHUB_TOKEN` is a placeholder — image push will fail.
3. Dangling `api` / `asuid.api` DNS records at registrar.
4. MCP service not deployed; Telegram image `docker.io/vhorbachov/digitalbrain-telegram` build step not in `deploy.yml` (only kernel is published).
5. Key Vault and ACR were deleted in Pass 1 — secrets are ACA secrets, images are public Docker Hub.

---

## 2. Cleanup Plan (pre-prod)

The repo is cleaner than a typical prototype (no checked-in bin/obj, secrets parametrized, only ~24 TODO/HACK across 12 files, tests maintained). Cleanup is polishing, ordered by value:

### 2.1 P0 — Naming: kill "NeuroOSPrototype"

You cannot go to production with `NeuroOSPrototype.*` in image metadata and namespaces. Blast radius is small and known:

- Rename projects/folders: `NeuroOSPrototype.AppHost` → `DigitalBrain.AppHost`, `NeuroOSPrototype.ServiceDefaults` → `DigitalBrain.ServiceDefaults`.
- Update the 4 reference points: `aspire.config.json` (appHost path), `Brain.slnx`, `DigitalBrain.Kernel.csproj`, `DigitalBrain.Tests.csproj`, plus `using NeuroOSPrototype.ServiceDefaults;` in Kernel/Program.cs and namespaces inside the two projects.
- Grep-verify: `grep -rn NeuroOSPrototype --include="*.cs*" --include="*.json" --include="*.slnx" --include="*.yml"` returns zero afterward.

Also finish the Silo→Kernel product-language rename in deploy scripts/workflow comments (flagged "Still left" in ARCHITECTURE_CLEANUP_PROPOSAL.md), and consider renaming the container app `digitalbrain-jobs` → `digitalbrain-kernel` at the next safe window (it's a create-new/delete-old in ACA; do it before the public FQDN gets embedded anywhere).

### 2.2 P0 — Docs: archive AI-session artifacts

Move to `docs/archive/` (or delete; git history keeps them): root `CONTINUATION_PROMPT.md`, `CONTINUITY.md`, and `docs/CONTINUATION-*.md` (5 files). Keep as living docs: `README.md`, `AGENTS.md`, `docs/PRODUCT_VISION.md`, `docs/SYSTEM_DESIGN.md`, `deploy/README.md` + `DEPLOY-STATUS.md` (fold the latter's stale sections into one current-state doc), and `ARCHITECTURE_CLEANUP_PROPOSAL.md` (trim "Done" log, keep "Still left" as the tracker). This file supersedes scattered deployment notes.

### 2.3 P1 — Aspire hosting integration hygiene (the plugin layer)

`DigitalBrain.Aspire` is the public face of the pack/plugin model, so its API should be tight before packs depend on it:

- **`DigitalBrainContext.Llm` is typed `object`** and consumers cast `(IResourceBuilder<IResourceWithConnectionString>)ctx.Llm` in two places (AppHost MCP wiring, `AddFlutterClient`). Type it properly (`IResourceBuilder<OllamaModelResource>` or the connection-string interface) and delete the casts.
- **Mutable context**: `EnableOrleansDashboard/OrleansDashboardPort/EnableMcp` are settable post-construction and `WithOrleansDashboard`/`WithMcp` mutate the context after `AddDigitalBrain` already ran. Either make these options-only (they already exist on `DigitalBrainOptions` with defaults `true`!) or make the fluent methods do the actual wiring. Right now `WithMcp(port)` ignores its `port` parameter entirely — dead knob, remove it.
- **Duplicate defaults**: dashboard/MCP enabled both in `DigitalBrainOptions` defaults and re-enabled in AppHost fluent calls. One source of truth.
- **`ResolveDevFlutterAppPath`**: 3 candidate paths + a 6-level parent-directory walk. Now that the app canonically lives at `brain/app`, cut this to (env override → `AppHostDirectory/../app`) and fail with the existing clear message.
- **Publish-mode awareness**: `storage.RunAsEmulator()` and the Ollama container are unconditional. Harmless today (prod bypasses Aspire publish via Pulumi), but wrap in `builder.ExecutionContext.IsRunMode` guards so `aspire publish` doesn't emit an Azurite emulator — this is the TripRadar pattern (conditional resource wiring per execution context) and it future-proofs an eventual `azd`/Aspire-publish path.
- **Split per-pack extensions into files** consistently (Salesforce already is; move Flutter + Telegram extensions out of `DigitalBrainBuilderExtensions.cs` into `FlutterAspireExtensions.cs` / `TelegramAspireExtensions.cs`). Long-term (post-prod): each integration pack ships its own `*.Aspire` bits, `DigitalBrain.Aspire` keeps only core (`AddDigitalBrain`, `WireKernelSilo`, model DSL).
- Remove the commented-out `// options.WithLLM<Gpt4oMini>()` line in AppHost; the switch is documented here and in README instead.

### 2.4 P1 — Kernel decomposition (carry-over, timeboxed)

`DigitalBrain.Kernel` is host + gateway + marketplace + LLM adapter + economics + foundry + ~20 neurons. Full split is post-GA work; before prod do only: keep `Program.cs` a composition root (it mostly is), and continue the `DigitalBrain.Core` split already in flight. Don't block deployment on this.

### 2.5 P2 — Small items

- The one actionable TODO (Task 10 Azure controller) — resolve or ticket it.
- Clean remaining nullable/obsolete warnings (`ARCHITECTURE_CLEANUP_PROPOSAL.md` "Still left").
- `scripts/` has only two verify scripts — fine; add `scripts/dev-setup.md` covering Ollama/Flutter/Azurite prerequisites for new machines.
- Split `DigitalBrain.Tests` into fast/slow lanes so CI gates stay quick as coverage grows.

---

## 3. Production Deployment Plan

Principle: **the cloud is a deployment profile, not a fork.** Same kernel image, same Orleans providers (already true), local `aspire run` remains fully functional offline with Ollama + Whisper + local embeddings.

### 3.1 Topology (target)

```
                    ┌─────────────────────────────────────────────┐
                    │ digitalbrain-rg (westeurope)                │
 Flutter Web        │                                             │
 (App Service or ───┼─► gRPC-Web ──► digitalbrain-kernel (ACA)    │
  Static Web Apps)  │               Orleans silo, 2–5 replicas    │
                    │               ├─ Table clustering ──┐       │
 Telegram ──webhook─┼─► digitalbrain-telegram (ACA) ─gRPC─┤       │
                    │                                     ▼       │
 Flutter desktop/   │               digitalbrainstprod (Storage)  │
 mobile ──gRPC──────┼─►             grainstate/journal/clustering │
                    │               digitalbrainopenaiprod (LLM)  │
                    │               Log Analytics + App Insights  │
                    └─────────────────────────────────────────────┘
```

Keep Pulumi (matches TripRadar; state already migrated to azblob). Do not switch to azd/Bicep mid-flight.

### 3.2 Phase 1 — Unblock the pipeline (the two one-time steps)

1. Create the Azure OIDC app registration + federated credential for `repo:digitalbraintech/brain:ref:refs/heads/master`, grant Contributor on `digitalbrain-rg` + Storage Blob Data Contributor on `digitalbrainstprod`, set `AZURE_CLIENT_ID` (commands already scripted in `deploy/DEPLOY-STATUS.md`).
2. Rotate `DOCKERHUB_TOKEN` to a real push-capable token — or better, **switch images to GHCR** (`ghcr.io/digitalbraintech/*`): same OIDC-free `GITHUB_TOKEN` auth in Actions, private repos free, removes the Docker Hub secret entirely. One-line change in `deploy.yml` + image constants in `deploy/Program.cs`.
3. Add the missing **Telegram image publish step** to `deploy.yml` (currently only the kernel image is built, but Pulumi deploys `digitalbrain-telegram` from a repo that CI never pushes).
4. Verify first green run: container boots without checkpoint crash, gRPC-Web CORS preflight OK, `/health` over FQDN.

### 3.3 Phase 2 — Make the Orleans silo production-grade on ACA

- **Replicas ≥ 2**: set `MinReplicas = 2` for the kernel. Azure Table clustering already supports multi-silo; the local AppHost already runs 3 replicas, so parity is proven. Verify silo-to-silo ports: ACA apps in the same environment can talk over internal FQDN, but Orleans silo/gateway ports (11111/30000) must be reachable pod-to-pod — confirm with `tcp` additional ingress or use the ACA internal networking; test scale-out before raising min replicas.
- **Health probes**: expose `/alive` (liveness) and `/health` (readiness incl. Orleans `IHealthCheck` participation) from ServiceDefaults, wire ACA `Probes` in Pulumi. TripRadar's dual `/live` + `/ready` pattern is the model.
- **Graceful shutdown**: ACA sends SIGTERM on revision replace; Orleans handles this via host lifetime, but set `terminationGracePeriodSeconds` (ACA default 30s) high enough for grain deactivation + journal flush — 60–90s.
- **Streams caveat**: `AddMemoryStreams` + memory `PubSubStore` are **per-cluster in-memory** — events survive within the cluster but not restarts, and PubSubStore in memory means subscriptions are lost on full restart. Acceptable for feed/timeline UX now; document it. When durability matters: Azure Queue streams + table PubSubStore (config-only change, mirrors the existing storage account).
- **Reminders**: if/when reminders are used, add `UseAzureTableReminderService` on the cloud path (same storage account).
- **Cluster identity**: pin `Orleans:ServiceId` = `digitalbrain` (stable forever) and set `ClusterId` per deployment generation if you ever need a blue/green cluster cut.

### 3.4 Phase 3 — Flutter web to Azure

Today: GitHub Pages at `digitalbrain.tech`. Options, in order of recommendation:

1. **Azure Static Web Apps (recommended)** — Flutter web is a static bundle; SWA gives CDN, free SSL, custom domain, and stays in `digitalbrain-rg`/Pulumi. Add a `staticwebapp` resource + swap the Pages workflow's upload step for the SWA deploy action.
2. **App Service** (your suggestion) — works (serve `build/web` via a tiny static server or the App Service static site config), but you pay for an always-on plan to serve static files and still want a CDN in front. Choose this only if you need server-side middleware on the web origin.
3. **Serve from the kernel** — `DIGITALBRAIN_WEBROOT` support already exists; bake `build/web` into the kernel image. Zero extra infra, same-origin gRPC-Web (no CORS). Great as a fallback/simplest option; couples web releases to kernel releases.

Either way: point `digitalbrain.tech` at the new host, attach `api.digitalbrain.tech` custom domain to the kernel ingress (SP2 item), and remove the dangling DNS records.

Flutter desktop/mobile: distribute separately (MS Store / sideload / stores later); they already speak native gRPC to the kernel FQDN — add a build-time config for the prod endpoint.

### 3.5 Phase 4 — Security hardening

- **Managed identity over keys** (revisit the Pass-1 deletion of Key Vault pragmatically): give the kernel container app a system-assigned identity, grant `Storage Table Data Contributor` + `Storage Blob Data Contributor`, switch Orleans providers to `TableServiceClient(uri, credential)` / `BlobServiceClient(uri, credential)`; then set `AllowSharedKeyAccess = false` on the storage account. Same for Azure OpenAI (`Cognitive Services OpenAI User` role, disable key auth). This removes the two biggest secrets entirely; ACA secrets then hold only checkpoint key + Telegram token, which is fine without Key Vault.
- Keep the `internal-service-key` contract (never injected into Flutter config) — already correct.
- Storage network rules: once stable, `DefaultAction = Deny` + ACA environment outbound IPs / private endpoint (later; needs VNet-injected ACA env — defer).

### 3.6 Phase 5 — Observability

- ServiceDefaults already wires OTEL; export to App Insights by injecting `APPLICATIONINSIGHTS_CONNECTION_STRING` into both container apps in Pulumi (the App Insights component exists but nothing sends to it today).
- Add the Orleans meters (`Microsoft.Orleans` instrumentation) to the OTEL metrics pipeline; keep the Orleans dashboard dev-only.
- Post-deploy smoke step in `deploy.yml`: hit `/health` on the new revision FQDN, fail the run otherwise.

---

## 4. Local-first & Cloud Sync

### 4.1 Keep `aspire run` fully offline-functional (P0, cheap)

The dual-mode design already guarantees local function. Close the remaining gap:

- **Real local embeddings**: register an Ollama embedding model in the DSL — e.g. `WithEmbedding<NomicEmbedText>()` (`nomic-embed-text`, 768-dim) or `all-minilm` (384-dim, drop-in for the current NoOp dimensionality) — add the model to the Ollama resource (`ollama.AddModel("embed", ...)`), inject `DigitalBrain__Embedding__*` env in `WireKernelSilo`, and replace `NoOpEmbeddingGenerator` registration with an Ollama-backed `IEmbeddingGenerator` when configured (fail-soft to NoOp when not). The HybridScorer comment says vector scoring activates with no further code change.
- **Local whisper by default in dev**: ship a compose/Aspire resource for a local OpenAI-compatible Whisper server (e.g. `speaches`/`faster-whisper-server` container) so `WithVoice2Text<Whisper1Local>()` activates out of the box instead of requiring a manually-set endpoint. Keep the env-gate for machines that can't run it.
- Document the three run profiles in README: **fast** (`dotnet run` kernel only, localhost clustering, in-memory), **full local** (`aspire run`: Azurite + Ollama + Whisper + Flutter windows client — zero cloud dependencies), **cloud** (ACA).

### 4.2 Cloud sync — recommended approach

Goal: use DigitalBrain locally (private, offline, free) and optionally have state meet the cloud brain. Three options considered:

1. **Hybrid cluster (local silo joins cloud cluster)** — rejected: latency, NAT, and clustering-table reachability make this fragile; Orleans clusters want a flat network.
2. **Profile switch (local kernel, cloud storage)** — trivial to support today: point local `aspire run` at the prod connection strings (`ConnectionStrings__*` overrides) and the local silo becomes a cloud-state client. Cheap, but it's *shared state*, not sync, and it puts prod credentials on the laptop. Support it as a power-user mode only; **never against the prod cluster's clustering table** (a local silo would join the prod cluster — use a separate ClusterId or separate storage account for this mode).
3. **Checkpoint-based sync (recommended)** — build on the existing checkpoint machinery (`DigitalBrain__Checkpoint__Key`, AES-encrypted): a `SyncNeuron`/host service that exports encrypted checkpoints (grain state + journals for the user's scope) to a per-user Azure Blob container and imports on the other side, last-writer-wins per journal stream at first. This matches the pack/journal architecture, works over plain HTTPS, keeps local fully sovereign, and cloud/local can both be "behind" without breaking. Phase it: (a) one-way local→cloud backup, (b) restore/bootstrap cloud→local, (c) two-way merge later if genuinely needed.

Decision needed from you: is sync "backup my local brain to my cloud brain" (option 3a/3b covers it, ~small effort) or "same brain, live on both" (that's option 2's shared-state semantics or a much bigger merge design)?

---

## 5. Sequenced Checklist

| # | Item | Phase | Effort |
|---|---|---|---|
| 1 | OIDC app reg + AZURE_CLIENT_ID + token rotation (or GHCR switch) | Pipeline | hours |
| 2 | Telegram image publish step in deploy.yml | Pipeline | minutes |
| 3 | Rename NeuroOSPrototype.* → DigitalBrain.* | Cleanup | hours |
| 4 | Archive CONTINUATION/CONTINUITY docs | Cleanup | minutes |
| 5 | DigitalBrain.Aspire API hygiene (typed Llm, dead knobs, path resolver, run-mode guards) | Cleanup | ~1 day |
| 6 | Health probes + MinReplicas=2 + graceful shutdown on ACA | Orleans prod | ~1 day |
| 7 | App Insights OTEL export + deploy smoke test | Observability | hours |
| 8 | Local embeddings via Ollama (replace NoOp) | Local-first | ~1 day |
| 9 | Local Whisper container in AppHost | Local-first | hours |
| 10 | Flutter web → Static Web Apps (or App Service) + api.digitalbrain.tech + DNS cleanup | Flutter prod | ~1 day |
| 11 | Managed identity for Storage + OpenAI, disable shared keys | Hardening | 1–2 days |
| 12 | Checkpoint-based local↔cloud sync (one-way first) | Sync | ~1 week |
| 13 | Streams durability (Azure Queue) — only when product needs it | Later | — |
| 14 | Kernel decomposition / Core split continuation | Later | ongoing |

Items 1–2 unblock everything; 3–5 before the first "real" prod tag so names/images are final; 6–7 before inviting users; 8–9 keep the local promise; 10–12 follow.
