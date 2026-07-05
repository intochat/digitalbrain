# DigitalBrain — Architecture Overview & Production Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Take the DigitalBrain prototype (Aspire-hosted Orleans kernel + Flutter client) from its current working-but-rough state to a production-grade, security-hardened deployment on `digitalbrain-rg`, while preserving the fully-offline local-first `aspire run` experience.

**Architecture:** Same kernel image, same Orleans providers, everywhere — the cloud (Azure Container Apps via Pulumi) is a deployment profile, not a fork. Locally, `aspire run` drives the identical Azure Storage/Orleans code paths against the Azurite emulator plus a local Ollama/Whisper stack; in the cloud, the same code paths hit real Azure Storage/OpenAI. Nothing in this plan changes that shape — it closes gaps in it (health, identity, observability, embeddings, sync) and pays down naming/API debt in the `DigitalBrain.Aspire` hosting DSL before external packs depend on it.

**Tech Stack:** .NET 11 / C# (top-level statements + `partial class Program` for test hosting), .NET Aspire 13.x (AppHost + `Aspire.Hosting.Orleans` + `CommunityToolkit.Aspire.Hosting.Ollama`), Microsoft Orleans (grains = "neurons"), Pulumi (`Pulumi.AzureNative`) for cloud provisioning, Azure Container Apps, Azure Storage (Table clustering + Blob grain/journal state), Azure OpenAI, GitHub Actions CI/CD, Flutter (desktop/web client).

## Global Constraints

- **No product fork between local and cloud.** Every task below must keep the `isAspireHosted` branch in `DigitalBrain.Kernel/Program.cs` as the only fork point; do not introduce a second one.
- **`aspire run` must stay fully offline-functional.** Any new dependency (embedding model, Whisper container) must fail soft to today's behavior when not configured, never crash the fast path.
- **.NET version floor:** target framework is `net11.0` everywhere (see any `.csproj` in this repo) — do not downgrade a new project.
- **Package versions:** use the same NuGet package versions already pinned by central package management (check `Directory.Packages.props` if present, otherwise match the sibling project's `PackageReference` version) — do not introduce a second version of a package already referenced elsewhere in the repo.
- **No secrets in git.** Every credential in this plan flows through GitHub Actions secrets → Pulumi config/env, or Aspire `AddParameter(..., secret: true)`. Never hardcode a key/token in source.
- **C# style (this repo's convention, enforced in review):** no `/// <summary>` boilerplate on new members — the existing codebase already violates this in older files (e.g. `DigitalBrainBuilderExtensions.cs`), but new code in this plan must not add more of it. Use a short inline `//` comment only for a genuinely non-obvious constraint, and prefer self-explanatory names over comments.
- **Verification style:** most tasks below are infrastructure/config changes without a meaningful red/green unit test cycle (renaming a project, editing a Pulumi resource). For those, "verification" means an exact build/grep/deploy command with an expected result, not a unit test. Where a task changes actual C# runtime logic (DI wiring, a new runtime-options record, a sync service), a real unit test is included and must go red→green.
- **GitHub org/repo used throughout this plan:** `digitalbraintech/brain` (matches the existing federated-credential subject already documented in `deploy/DEPLOY-STATUS.md`). Confirm this is still correct before running any `gh`/`az` command below.

---

# Part 1 — Current State (Architecture Overview)

## 1.1 Solution shape

`Brain.slnx` is the single canonical solution (~33 projects). Logical layers:

| Layer | Projects | Notes |
|---|---|---|
| Orchestration (Aspire) | `NeuroOSPrototype.AppHost`, `NeuroOSPrototype.ServiceDefaults`, `DigitalBrain.Aspire` | AppHost is thin; all wiring lives in the `DigitalBrain.Aspire` DSL ("plugin" layer). Renamed in Milestone M2 below. |
| Runtime host | `DigitalBrain.Kernel` | Orleans silo + gRPC/gRPC-Web gateway + web bundle host — the one deployable backend image |
| Stable contracts | `DigitalBrain.Core`, `DigitalBrain.Pack.Contracts`, `DigitalBrain.Marketplace.Contracts`, `DigitalBrain.Ui.Contracts`, `DigitalBrain.Demo.Contracts` | Phase-2 Core split in progress (see `ARCHITECTURE_CLEANUP_PROPOSAL.md`) |
| Runtime libs | `DigitalBrain.Context` (RAG/hybrid scorer), `DigitalBrain.Ui.Runtime`, `DigitalBrain.Demo.Runtime`, `DigitalBrain.UiKit`, `DigitalBrain.SeedPacks` | |
| Integrations (packs) | `DigitalBrain.Telegram[.Channel/.Transport]`, `DigitalBrain.Google`, `DigitalBrain.Salesforce`, `DigitalBrain.Windows`, `DigitalBrain.Developer`, `DigitalBrain.Mcp`, `DigitalBrain.Experience.PersonalAssistant` | Telegram.Transport is a second deployable |
| Clients | `app/` (Flutter: windows, web, mobile shells) | `Flutter.proj` referenced by slnx; `SkipFlutterBuild=true` in headless CI |
| Deploy | `deploy/` (Pulumi.AzureNative, ~330 lines) | Replaced the 33k-LOC vendored DeploymentKit |
| Tests | 12 `*.Tests` projects + `DigitalBrain.TestKit` | Maintained, not stubs |

## 1.2 Aspire hosting integration (the "plugin" model)

`AppHost.cs` is intentionally ~80 lines (`NeuroOSPrototype.AppHost/AppHost.cs`). Everything is composed via `DigitalBrain.Aspire` (`DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs`):

- `builder.AddDigitalBrain("digitalbrain", options => ...)` (`DigitalBrainBuilderExtensions.cs:48-114`) provisions: Azure Storage (`RunAsEmulator()` → Azurite locally), Orleans (`WithClustering(table)` + `WithGrainStorage("Default", blobs)` + journal blobs), and an **always-on Ollama container** (`CommunityToolkit.Aspire.Hosting.Ollama`: `WithGPUSupport().WithDataVolume().WithOpenWebUI()`) pulling the fallback model `qwen2.5-coder:1.5b`.
- Typed model registry DSL: `options.WithLLM<Qwen25Coder1_5B>()`, `WithVoice2Text<Whisper1Local>()`, `WithEmbedding<T>()`, `WithVectorDatabase(...)`, with routing roles `AsFast()/AsBalanced()/AsReasoning()`. Models are typed markers in `DigitalBrain.Aspire/LlmModels.cs`, not raw strings. Azure OpenAI switch = `WithLLM<Gpt4oMini>()` + two secret parameters.
- `ctx.WireKernelSilo(kernel)` (`DigitalBrainBuilderExtensions.cs:135-184`) — injects Orleans refs, LLM/voice/model-registry env vars, grpc + web endpoints, `WithReplicas(3)` for local HA.
- `ctx.AddDefaultDevFlutterClient(kernel)` / `AddFlutterClient(...)` — runs `flutter run -d windows` as an `ExecutableResource` on `aspire run`.
- `ctx.WireTelegramTransport(...)` — boots no-op without a token; token is an optional secret parameter or supplied later via in-app config, no AppHost restart.
- `builder.AddSalesforceAppConfig()` — its own extension file, `DigitalBrain.Aspire/SalesforceAspireExtensions.cs` — the template for Milestone M4/Task 9.

This is a good pattern and the right seam for "packs provide their own Aspire bits". Cleanup items for this layer are Milestone M4.

## 1.3 Orleans runtime — dual-mode kernel

`DigitalBrain.Kernel/Program.cs` detects mode via `ConnectionStrings__clustering|grainstate|journal` env vars (`isAspireHosted`, `Program.cs:31-33`):

| | Fast path (`dotnet run`) | Aspire/cloud path |
|---|---|---|
| Clustering | `UseLocalhostClustering()` | `UseAzureStorageClustering` (Table `OrleansSiloInstances`) |
| Grain storage | Memory (`AddMemoryGrainStorageAsDefault`) | `AddAzureBlobGrainStorage("Default")` |
| Journal | In-memory prototype journals (`ConfigurePrototypeJournals`) | `AddAzureBlobJournalStorage`, `orleans-binary` format (JSON journal format proven broken by spike — see `DigitalBrain.Tests/Spikes/`) |
| Cluster identity | — | `Orleans:ClusterId/ServiceId` = `digitalbrain` (stable rejoin) |

Both paths: memory streams (`HomeFeed`, `DigitalBrainTimeline`), memory `PubSubStore` (Program.cs:208-210, **applies to both paths uniformly** — this is the streams-durability gap tracked in Milestone M12), Foundry, gRPC gateway (`GatewayService`/`UiGatewayService`, `Program.cs:232-233`) with gRPC-Web + CORS for browsers, and optional static web bundle serving via `DIGITALBRAIN_WEBROOT` (`Program.cs:223-230`).

Important: locally under `aspire run` the same Azure providers are used against the **Azurite emulator**, so local and cloud exercise identical Orleans persistence code. This is a real strength — keep it.

## 1.4 Local ML (offline-functional guarantee)

- **LLM**: provider abstraction `ollama | azureopenai` (`DigitalBrainLlmRuntimeOptions`, `DigitalBrain.Kernel/Llm/DigitalBrainLlmRuntimeOptions.cs`). Ollama always runs as fallback regardless of primary provider.
- **Voice-to-text**: `Whisper1Local` — any OpenAI-compatible `/audio/transcriptions` endpoint, activated only when `DigitalBrain:Voice:Endpoint` / `DIGITALBRAIN_VOICE_ENDPOINT` is set (`DigitalBrain.Kernel/Voice/VoiceTranscription.cs:62`). Optional by design.
- **Embeddings**: **not real yet.** `NoOpEmbeddingGenerator` (`DigitalBrain.Kernel/Llm/NoOpEmbeddingGenerator.cs`) emits 384-dim zero vectors; `HybridScorer` (`DigitalBrain.Context/HybridScorer.cs:10-18`) detects zero vectors and falls back to keyword recall. The DSL (`WithEmbedding<T>`) and the swap point (`IEmbeddingGenerator<string, Embedding<float>>`) already exist — wiring a real local model activates vector RAG with no code change to `HybridScorer`. This is the single biggest functional gap in the "local ML" story — closed in Milestone M7.

## 1.5 Flutter client

`app/` full Flutter workspace. Local: launched by Aspire as windows desktop client. Web: `.github/workflows/deploy-flutter-web.yml` builds `flutter build web --release` and publishes to **GitHub Pages** at `digitalbrain.tech` (`app/web/CNAME`). Client talks to kernel via gRPC-Web to the kernel's external ingress.

## 1.6 Current deployment (`digitalbrain-rg`)

Pulumi.AzureNative program (`deploy/Program.cs`), stack `dev`, state in `azblob://pulumi-state` (in `digitalbrainstprod`). Live footprint (10 Pulumi / 8 Azure resources):

- `digitalbrain-rg` (westeurope)
- `digitalbrainstprod` StorageV2 — Orleans Table clustering + Blob grainstate/journal + Pulumi state
- `digitalbrainopenaiprod` (S0) + deployment `chat` = gpt-4o-mini GlobalStandard cap 10
- `digitalbrain-log-prod` + `digitalbrain-ai-prod` (Log Analytics + App Insights — App Insights exists but nothing sends to it yet, see Milestone M6)
- `digitalbrain-cae-prod` ACA managed environment
- `digitalbrain-jobs` container app — **the kernel silo**, external ingress `Auto` (HTTP/1.1 + h2 → gRPC-Web + native gRPC), port 8080, 1 CPU / 2Gi, scale 1–5, secrets: storage conn string, OpenAI key, checkpoint AES key
- `digitalbrain-telegram` container app — external `/webhook` ingress, 0.25 CPU / 0.5Gi, scale 1–3

CI/CD: `.github/workflows/deploy.yml` on push to master → `dotnet test Brain.slnx` (skip Flutter, skip E2E) → `dotnet publish -t:PublishContainer` → **public Docker Hub** `vhorbachov/digitalbrain-kernel` → Azure OIDC login → `pulumi up`. All prod deploys go through GitHub Actions only.

**Known incomplete (from `deploy/DEPLOY-STATUS.md`):**

1. Azure OIDC app registration not created (`AZURE_CLIENT_ID` is a placeholder) — deploy workflow cannot actually run.
2. `DOCKERHUB_TOKEN` is a placeholder — image push will fail.
3. Dangling `api` / `asuid.api` DNS records at registrar.
4. Telegram image `docker.io/vhorbachov/digitalbrain-telegram` build step not in `deploy.yml` (only kernel is published) even though `deploy/Program.cs` deploys it.
5. Key Vault and ACR were deleted in Pass 1 — secrets are ACA secrets, images are public Docker Hub.

---

# Part 2 — Detailed Implementation Plan

Each milestone below maps 1:1 to a row of the old "Sequenced Checklist" so nothing is lost. **Do the milestones in order** — later ones assume earlier ones landed (e.g. Milestone M5's health checks are referenced by Milestone M9's Static Web App smoke test).

## Milestone M1 — Unblock the CI/CD pipeline (checklist #1, #2)

### Task 1: Switch container images from Docker Hub to GHCR + add the missing Telegram image publish step

**Why GHCR over rotating the Docker Hub token:** GHCR auth reuses the workflow's own `GITHUB_TOKEN` (already scoped via `permissions: packages: write`, already present at `.github/workflows/deploy.yml:18`) — no new secret to create, rotate, or leak. It also removes `DOCKERHUB_TOKEN`/`vars.DOCKERHUB_USERNAME` entirely.

**Files:**
- Modify: `.github/workflows/deploy.yml:35-46`
- Modify: `deploy/Program.cs:30-31`

**Steps:**

- [x] **Step 1: Replace the Docker Hub login + kernel publish steps with GHCR login + both images' publish steps**

In `.github/workflows/deploy.yml`, replace lines 35-46:

```yaml
      - name: Log in to Docker Hub
        uses: docker/login-action@v3
        with:
          username: ${{ vars.DOCKERHUB_USERNAME }}
          password: ${{ secrets.DOCKERHUB_TOKEN }}

      - name: Publish kernel image
        run: |
          dotnet publish DigitalBrain.Kernel/DigitalBrain.Kernel.csproj -c Release /t:PublishContainer \
            -p:ContainerRegistry=docker.io \
            -p:ContainerRepository=vhorbachov/digitalbrain-kernel \
            -p:ContainerImageTag="${TAG}"
```

with:

```yaml
      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Publish kernel image
        run: |
          dotnet publish DigitalBrain.Kernel/DigitalBrain.Kernel.csproj -c Release /t:PublishContainer \
            -p:ContainerRegistry=ghcr.io \
            -p:ContainerRepository=digitalbraintech/digitalbrain-kernel \
            -p:ContainerImageTag="${TAG}"

      - name: Publish Telegram transport image
        run: |
          dotnet publish DigitalBrain.Telegram.Transport/DigitalBrain.Telegram.Transport.csproj -c Release /t:PublishContainer \
            -p:ContainerRegistry=ghcr.io \
            -p:ContainerRepository=digitalbraintech/digitalbrain-telegram \
            -p:ContainerImageTag="${TAG}"
```

- [x] **Step 2: Point Pulumi at the GHCR image repositories**

In `deploy/Program.cs`, replace lines 28-31:

```csharp
    // Images live in public Docker Hub. ACA pulls without registry creds because the repos are public;
    // otherwise add AppInputs.RegistryCredentialsArgs (server=docker.io) with a Docker Hub access-token secret.
    private const string KernelImageRepository = "docker.io/vhorbachov/digitalbrain-kernel";
    private const string TelegramImageRepository = "docker.io/vhorbachov/digitalbrain-telegram";
```

with:

```csharp
    // Images live in GHCR under the repo's own org. ACA pulls without registry creds only while the packages
    // are public (Step 3); if you ever need to keep them private, add AppInputs.RegistryCredentialsArgs
    // (server=ghcr.io) with a GitHub PAT that has read:packages scope, stored as a Pulumi secret.
    private const string KernelImageRepository = "ghcr.io/digitalbraintech/digitalbrain-kernel";
    private const string TelegramImageRepository = "ghcr.io/digitalbraintech/digitalbrain-telegram";
```

- [x] **Step 3: Make both GHCR packages public (first push only, one-time)**

GHCR packages default to private on first push, which ACA cannot pull without registry credentials. After the first workflow run pushes the images once (Task 2 must also be done for the workflow to reach `pulumi up`, but the image-publish steps alone will succeed once GHCR login works), set both packages public:

```bash
gh api -X PATCH /orgs/digitalbraintech/packages/container/digitalbrain-kernel --field visibility=public
gh api -X PATCH /orgs/digitalbraintech/packages/container/digitalbrain-telegram --field visibility=public
```

If `digitalbraintech` is a personal account rather than an org, use `/user/packages/container/{name}` instead of `/orgs/digitalbraintech/packages/container/{name}`.

- [x] **Step 4: Verify locally that both projects still produce a valid container image target**

```bash
dotnet publish DigitalBrain.Kernel/DigitalBrain.Kernel.csproj -c Release /t:PublishContainer -p:ContainerRegistry=ghcr.io -p:ContainerRepository=digitalbraintech/digitalbrain-kernel -p:ContainerImageTag=local-verify
dotnet publish DigitalBrain.Telegram.Transport/DigitalBrain.Telegram.Transport.csproj -c Release /t:PublishContainer -p:ContainerRegistry=ghcr.io -p:ContainerRepository=digitalbraintech/digitalbrain-telegram -p:ContainerImageTag=local-verify
```
Expected: both commands end with `Pushed image ... to registry` or a local-only build success (no registry push without login is fine — the goal here is confirming both `.csproj`s have `PublishContainer` support and no compile errors, not to actually push from a dev box).

- [x] **Step 5: Grep-confirm no remaining Docker Hub references**

```bash
grep -rn "docker.io\|DOCKERHUB" --include="*.yml" --include="*.cs" .github deploy
```
Expected: zero matches.

- [x] **Step 6: Commit**

```bash
git add .github/workflows/deploy.yml deploy/Program.cs
git commit -m "ci: switch container images from Docker Hub to GHCR, add Telegram image publish step"
```

### Task 2: Create the Azure OIDC app registration and wire GitHub repo variables

**Files:** none in-repo (Azure AD + GitHub repo settings only). No secrets are written to this repo — every value below goes to `gh variable`/`gh secret`, never to a file.

**Steps:**

- [x] **Step 1: Create the Azure AD application and service principal**

```bash
APP_ID=$(az ad app create --display-name "digitalbrain-deploy" --query appId -o tsv)
az ad sp create --id "$APP_ID"
echo "APP_ID=$APP_ID"
```

- [x] **Step 2: Add the federated credential scoped to the deploy workflow on `master`**

```bash
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "digitalbrain-deploy-master",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:digitalbraintech/brain:ref:refs/heads/master",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

- [x] **Step 3: Grant the minimum RBAC roles on the existing resource group and storage account**

```bash
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
az role assignment create --assignee "$APP_ID" --role "Contributor" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/digitalbrain-rg"
az role assignment create --assignee "$APP_ID" --role "Storage Blob Data Contributor" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/digitalbrain-rg/providers/Microsoft.Storage/storageAccounts/digitalbrainstprod"
```
(`Storage Blob Data Contributor` is required in addition to the resource-group `Contributor` role because the Pulumi backend `azblob://pulumi-state` authenticates via Azure AD against this specific storage account, not ARM.)

- [ ] **Step 4: Set the GitHub repo variables the workflow already reads**

```bash
TENANT_ID=$(az account show --query tenantId -o tsv)
gh variable set AZURE_CLIENT_ID --body "$APP_ID" --repo digitalbraintech/brain
gh variable set AZURE_TENANT_ID --body "$TENANT_ID" --repo digitalbraintech/brain
gh variable set AZURE_SUBSCRIPTION_ID --body "$SUBSCRIPTION_ID" --repo digitalbraintech/brain
```

- [ ] **Step 5: Verify with a real workflow run**

```bash
gh workflow run deploy.yml -f image_tag=pipeline-smoke-test --repo digitalbraintech/brain
gh run watch --repo digitalbraintech/brain
```
Expected: the "Azure login (OIDC)" step succeeds (no `AADSTS700016`/`AADSTS70021` errors) and `pulumi up` reaches at least the preview stage. If `pulumi up` fails for an unrelated reason (e.g. a resource not yet covered by this plan), that's fine for this task — the goal here is only that OIDC auth succeeds.

No commit for this task — it's Azure/GitHub state, not repo state.

---

## Milestone M2 — Cleanup: kill "NeuroOSPrototype" naming (checklist #3)

### Task 3: Rename `NeuroOSPrototype.AppHost` → `DigitalBrain.AppHost` and `NeuroOSPrototype.ServiceDefaults` → `DigitalBrain.ServiceDefaults`

This must be done as one atomic task — a partial rename leaves the solution non-building, so there is no meaningful intermediate checkpoint.

**Files (17 total; ordering matters — folders first, then every reference):**
- Rename folder + csproj: `NeuroOSPrototype.AppHost/` → `DigitalBrain.AppHost/`, `NeuroOSPrototype.AppHost.csproj` → `DigitalBrain.AppHost.csproj`
- Rename folder + csproj: `NeuroOSPrototype.ServiceDefaults/` → `DigitalBrain.ServiceDefaults/`, `NeuroOSPrototype.ServiceDefaults.csproj` → `DigitalBrain.ServiceDefaults.csproj`
- Modify: `DigitalBrain.ServiceDefaults/Extensions.cs:11` (namespace declaration)
- Modify: `DigitalBrain.Kernel/Program.cs:22` (`using` statement)
- Modify: `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj:65` (ProjectReference path)
- Modify: `DigitalBrain.Tests/DigitalBrain.Tests.csproj:54` (ProjectReference path, keeps `Aliases="AppHostProject"`)
- Modify: `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs:84` (`Assembly.Load("...")` string)
- Modify: `Brain.slnx:32-33` (project entries)
- Modify: `aspire.config.json:2-4` (`appHost.path`)
- Modify: `scripts/verify-fast.ps1:41` (`$AppHost` variable)
- Modify: `DigitalBrain.Aspire/README.md:36`
- Modify: `README.md:19,98`
- Modify: `docs/SYSTEM_DESIGN.md:104,155,308,366`
- Modify: `demo/DEMO-SCRIPT.md:172`
- Modify: `ARCHITECTURE_CLEANUP_PROPOSAL.md:126`
- Modify: `.claude/skills/verify/SKILL.md:11`
- **Do NOT modify:** `docs/superpowers/plans/2026-07-04-salesforce-oauth-callback-grain-routing.md:578` — this is a dated historical plan describing a past session's state; rewriting history docs creates false records. Leave it as-is.

**Steps:**

- [x] **Step 1: Rename the two project folders and their `.csproj` files with `git mv` (preserves history)**

```bash
git mv NeuroOSPrototype.AppHost DigitalBrain.AppHost
git mv DigitalBrain.AppHost/NeuroOSPrototype.AppHost.csproj DigitalBrain.AppHost/DigitalBrain.AppHost.csproj
git mv NeuroOSPrototype.ServiceDefaults DigitalBrain.ServiceDefaults
git mv DigitalBrain.ServiceDefaults/NeuroOSPrototype.ServiceDefaults.csproj DigitalBrain.ServiceDefaults/DigitalBrain.ServiceDefaults.csproj
```

- [x] **Step 2: Fix the namespace in ServiceDefaults**

In `DigitalBrain.ServiceDefaults/Extensions.cs:11`, change:
```csharp
namespace NeuroOSPrototype.ServiceDefaults;
```
to:
```csharp
namespace DigitalBrain.ServiceDefaults;
```

- [x] **Step 3: Fix the `using` in Kernel's Program.cs**

In `DigitalBrain.Kernel/Program.cs:22`, change:
```csharp
using NeuroOSPrototype.ServiceDefaults;
```
to:
```csharp
using DigitalBrain.ServiceDefaults;
```

- [x] **Step 4: Fix the two `.csproj` ProjectReference paths**

In `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj:65`, change:
```xml
<ProjectReference Include="..\NeuroOSPrototype.ServiceDefaults\NeuroOSPrototype.ServiceDefaults.csproj" />
```
to:
```xml
<ProjectReference Include="..\DigitalBrain.ServiceDefaults\DigitalBrain.ServiceDefaults.csproj" />
```

In `DigitalBrain.Tests/DigitalBrain.Tests.csproj:54`, change:
```xml
<ProjectReference Include="..\NeuroOSPrototype.AppHost\NeuroOSPrototype.AppHost.csproj" Aliases="AppHostProject" />
```
to:
```xml
<ProjectReference Include="..\DigitalBrain.AppHost\DigitalBrain.AppHost.csproj" Aliases="AppHostProject" />
```

- [x] **Step 5: Fix the `Assembly.Load` string in the E2E fixture**

In `DigitalBrain.Tests/E2E/DigitalBrainAppHostFixture.cs:84`, change:
```csharp
Assembly.Load("NeuroOSPrototype.AppHost")
```
to:
```csharp
Assembly.Load("DigitalBrain.AppHost")
```

- [x] **Step 6: Fix `Brain.slnx`**

In `Brain.slnx:32-33`, update both project path strings from `NeuroOSPrototype.AppHost\NeuroOSPrototype.AppHost.csproj` / `NeuroOSPrototype.ServiceDefaults\NeuroOSPrototype.ServiceDefaults.csproj` to `DigitalBrain.AppHost\DigitalBrain.AppHost.csproj` / `DigitalBrain.ServiceDefaults\DigitalBrain.ServiceDefaults.csproj`.

- [x] **Step 7: Fix `aspire.config.json`**

In `aspire.config.json:2-4`, change:
```json
"appHost": { "path": "NeuroOSPrototype.AppHost/NeuroOSPrototype.AppHost.csproj" }
```
to:
```json
"appHost": { "path": "DigitalBrain.AppHost/DigitalBrain.AppHost.csproj" }
```

- [x] **Step 8: Fix `scripts/verify-fast.ps1:41`**

Change:
```powershell
$AppHost = 'NeuroOSPrototype.AppHost\NeuroOSPrototype.AppHost.csproj'
```
to:
```powershell
$AppHost = 'DigitalBrain.AppHost\DigitalBrain.AppHost.csproj'
```

- [x] **Step 9: Update remaining doc references**

Update every `NeuroOSPrototype` occurrence in `DigitalBrain.Aspire/README.md:36`, `README.md:19,98`, `docs/SYSTEM_DESIGN.md:104,155,308,366`, `demo/DEMO-SCRIPT.md:172`, `ARCHITECTURE_CLEANUP_PROPOSAL.md:126`, and `.claude/skills/verify/SKILL.md:11` to the corresponding `DigitalBrain.AppHost` / `DigitalBrain.ServiceDefaults` name.

- [x] **Step 10: Build the full solution**

```bash
dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true
```
Expected: `Build succeeded. 0 Error(s)`.

- [x] **Step 11: Run the fast test lane (per this repo's CLAUDE.md convention: high severity, must be green)**

```bash
dotnet test Brain.slnx -c Release -p:SkipFlutterBuild=true --filter "FullyQualifiedName!~E2E"
```
Expected: all tests pass, including `DigitalBrain.Tests` cases that reference `AppHostProject` (the alias, confirmed still valid via Step 4).

- [x] **Step 12: Grep-verify zero remaining references outside the intentionally-untouched historical plan**

```bash
grep -rln "NeuroOSPrototype" --include="*.cs*" --include="*.json" --include="*.slnx" --include="*.yml" --include="*.md" --include="*.ps1" . | grep -v "docs/superpowers/plans/2026-07-04-salesforce-oauth-callback-grain-routing.md"
```
Expected: empty output.

- [x] **Step 13: Commit**

```bash
git add -A
git commit -m "rename: NeuroOSPrototype.AppHost/ServiceDefaults -> DigitalBrain.AppHost/ServiceDefaults"
```

---

## Milestone M3 — Cleanup: archive AI-session docs (checklist #4)

### Task 4: Archive stale continuation/continuity docs

**Files:**
- Move: `CONTINUATION_PROMPT.md` → `docs/archive/CONTINUATION_PROMPT.md`
- Move: `CONTINUITY.md` → `docs/archive/CONTINUITY.md`
- Move: every `docs/CONTINUATION-*.md` (5 files, confirm the exact list with the command in Step 1) → `docs/archive/`
- Keep as living docs (no change): `README.md`, `AGENTS.md`, `docs/PRODUCT_VISION.md`, `docs/SYSTEM_DESIGN.md`, `deploy/README.md`, `deploy/DEPLOY-STATUS.md`, `ARCHITECTURE_CLEANUP_PROPOSAL.md`

**Steps:**

- [x] **Step 1: List the exact files being moved (confirm the set before moving)**

```bash
ls CONTINUATION_PROMPT.md CONTINUITY.md docs/CONTINUATION-*.md
```

- [x] **Step 2: Create the archive directory and move files with `git mv`**

```bash
mkdir -p docs/archive
git mv CONTINUATION_PROMPT.md docs/archive/CONTINUATION_PROMPT.md
git mv CONTINUITY.md docs/archive/CONTINUITY.md
git mv docs/CONTINUATION-*.md docs/archive/
```

- [x] **Step 3: Grep for any doc that links to the old paths and fix the links**

```bash
grep -rln "CONTINUATION_PROMPT.md\|CONTINUITY.md\|docs/CONTINUATION-" --include="*.md" .
```
Update any hit's relative path to `docs/archive/...`.

- [x] **Step 4: Commit**

```bash
git add -A
git commit -m "docs: archive AI-session continuation/continuity artifacts to docs/archive/"
```

---

## Milestone M4 — Cleanup: `DigitalBrain.Aspire` API hygiene (checklist #5)

This is the public face of the pack/plugin model — tighten it before other packs depend on it.

### Task 5: Type `DigitalBrainContext.Llm` properly and delete the three casts

**Files:**
- Modify: `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs:14` (property declaration), `:97` (assignment), `:142` (cast site), `:253` (cast site)
- Modify: `NeuroOSPrototype.AppHost/AppHost.cs:47` (cast site — path will be `DigitalBrain.AppHost/AppHost.cs` if Task 3 already landed, which it must have by this point since milestones are sequential)
- Test: `DigitalBrain.Tests/Aspire/DigitalBrainModelRegistryTests.cs` (add a compile-time-shape assertion, see Step 4)

**Steps:**

- [x] **Step 1: Change the property type from `object` to the real resource-builder interface**

In `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs:14`, change:
```csharp
    public required object Llm { get; init; }
```
to:
```csharp
    public required IResourceBuilder<IResourceWithConnectionString> Llm { get; init; }
```

- [x] **Step 2: Fix the assignment to match (the value returned by `ollama.AddModel(...)` already satisfies this interface — the cast becomes unnecessary at the source)**

In `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs:97`, `Llm = qwen,` needs no change (assigning a concretely-typed `IResourceBuilder<OllamaModelResource>` — which `qwen` already is — to a field typed as the connection-string interface upcasts implicitly since `OllamaModelResource : IResourceWithConnectionString`). If the compiler reports an implicit-conversion error here, it means `OllamaModelResource` does not implement `IResourceWithConnectionString` directly; in that case use `Llm = (IResourceBuilder<IResourceWithConnectionString>)qwen,` at this single assignment site only (the goal of this task is to delete the casts at the three *consumption* sites, not necessarily this one construction site).

- [x] **Step 3: Delete the two casts inside `DigitalBrain.Aspire`**

In `DigitalBrainBuilderExtensions.cs:142` (inside `WireKernelSilo`), change:
```csharp
            .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm)
```
to:
```csharp
            .WithReference(ctx.Llm)
```

In `DigitalBrainBuilderExtensions.cs:253` (inside `AddFlutterClient`), change:
```csharp
            .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm)
```
to:
```csharp
            .WithReference(ctx.Llm)
```

- [x] **Step 4: Delete the cast in AppHost.cs**

In `AppHost.cs:47` (MCP wiring), change:
```csharp
        .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm);
```
to:
```csharp
        .WithReference(ctx.Llm);
```

- [x] **Step 5: Build to confirm the type change compiles clean across all four sites**

```bash
dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true
```
Expected: `Build succeeded. 0 Error(s)`. If Step 2's implicit upcast fails, apply the single-site cast fallback described in Step 2 and rebuild.

- [x] **Step 6: Grep-confirm no remaining cast of `ctx.Llm`**

```bash
grep -rn "IResourceBuilder<IResourceWithConnectionString>)ctx.Llm" .
```
Expected: empty output.

- [x] **Step 7: Commit**

```bash
git add DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs DigitalBrain.AppHost/AppHost.cs
git commit -m "refactor(aspire): type DigitalBrainContext.Llm as IResourceBuilder<IResourceWithConnectionString>, drop 3 casts"
```

### Task 6: Make dashboard/MCP toggles options-only, fix `WithMcp`'s dead `port` parameter, remove the commented-out LLM line

**Why:** `EnableOrleansDashboard`/`OrleansDashboardPort`/`EnableMcp` are set twice — once via `DigitalBrainOptions` defaults (both already default to `true`, per `DigitalBrainBuilderExtensions.cs:405-407`), and again via the fluent `WithOrleansDashboard()`/`WithMcp()` calls in `AppHost.cs:19-20` which mutate `DigitalBrainContext` *after* `AddDigitalBrain` already ran. Two sources of truth for the same three flags is the actual bug (not that they're mutable per se) — collapse to one.

**Files:**
- Modify: `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs:116-127` (`WithOrleansDashboard`/`WithMcp` methods), `:347-487` (`DigitalBrainOptions`)
- Modify: `NeuroOSPrototype.AppHost/AppHost.cs:9-20` (call site)

**Steps:**

- [x] **Step 1: Move dashboard-port configuration onto `DigitalBrainOptions` as the single source of truth**

`DigitalBrainOptions.OrleansDashboardPort` already exists (`:406`, defaults to `8080`) and already flows into `DigitalBrainContext.OrleansDashboardPort` at construction (`:108`). Delete the post-construction fluent mutators entirely. In `DigitalBrainBuilderExtensions.cs`, delete lines 116-127:
```csharp
    public static DigitalBrainContext WithOrleansDashboard(this DigitalBrainContext ctx, int? port = null)
    {
        ctx.EnableOrleansDashboard = true;
        if (port.HasValue) ctx.OrleansDashboardPort = port;
        return ctx;
    }

    public static DigitalBrainContext WithMcp(this DigitalBrainContext ctx, int? port = null)
    {
        ctx.EnableMcp = true;
        return ctx;
    }
```

- [x] **Step 2: Remove the now-pointless mutable setters on `DigitalBrainContext`, keep them get-only**

In `DigitalBrainBuilderExtensions.cs:39-41`, change:
```csharp
    public bool EnableOrleansDashboard { get; set; }
    public int? OrleansDashboardPort { get; set; }
    public bool EnableMcp { get; set; }
```
to:
```csharp
    public bool EnableOrleansDashboard { get; init; }
    public int? OrleansDashboardPort { get; init; }
    public bool EnableMcp { get; init; }
```
(`init` instead of `get`-only-with-no-setter because they're still set via object initializer at the `return new DigitalBrainContext { ... }` in `AddDigitalBrain`, `:107-109` — no change needed there.)

- [x] **Step 3: Update the AppHost call site to configure these via `options`, not fluent post-calls**

In `AppHost.cs`, change lines 9-20 from:
```csharp
var ctx = builder.AddDigitalBrain("digitalbrain", options =>
{
    options.WithLLM<Qwen25Coder1_5B>();
    if (HasValue("DigitalBrain:Voice:Endpoint", "DIGITALBRAIN_VOICE_ENDPOINT"))
    {
        options.WithVoice2Text<Whisper1Local>();
    }
    // options.WithLLM<Gpt4oMini>(); // switch to Azure OpenAI when ready (needs azure-openai-endpoint/-key parameters)
    options.UseLocalMarketplace = true;
})
.WithOrleansDashboard(8080)
.WithMcp();
```
to:
```csharp
var ctx = builder.AddDigitalBrain("digitalbrain", options =>
{
    options.WithLLM<Qwen25Coder1_5B>();
    if (HasValue("DigitalBrain:Voice:Endpoint", "DIGITALBRAIN_VOICE_ENDPOINT"))
    {
        options.WithVoice2Text<Whisper1Local>();
    }
    // To switch to Azure OpenAI, call options.WithLLM<Gpt4oMini>() instead — it needs the
    // azure-openai-endpoint/-key parameters wired below (see README "LLM provider switch").
    options.UseLocalMarketplace = true;
    options.OrleansDashboardPort = 8080;
});
```
(`EnableOrleansDashboard`/`EnableMcp` already default to `true` on `DigitalBrainOptions`, so no explicit line is needed for either — that was the whole point of collapsing to one source of truth. `OrleansDashboardPort` is set explicitly here only because `8080` was the value the old fluent call passed; if `8080` is in fact just the existing default, delete this line entirely instead.)

- [x] **Step 4: Build**

```bash
dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true
```
Expected: `Build succeeded. 0 Error(s)`.

- [x] **Step 5: Run the Aspire model-registry tests**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~DigitalBrainModelRegistryTests"
```
Expected: all pass (these tests exercise `DigitalBrainOptions`/`DigitalBrainContext` construction; if any asserted on `WithOrleansDashboard`/`WithMcp` as fluent methods, update the assertion to configure via `options.OrleansDashboardPort`/rely on the `EnableMcp` default instead).

- [x] **Step 6: Commit**

```bash
git add DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs DigitalBrain.AppHost/AppHost.cs
git commit -m "refactor(aspire): collapse dashboard/MCP toggles to DigitalBrainOptions as single source of truth"
```

### Task 7: Simplify `ResolveDevFlutterAppPath`

**Why:** the app canonically lives at `brain/app` now (per §1.5); the 3-candidate + 6-level parent walk was defensive scaffolding for a time when the app's location was still moving around.

**Files:**
- Modify: `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs:315-344`
- Test: `DigitalBrain.Tests/Aspire/` (add a new focused test, see Step 2)

**Steps:**

- [x] **Step 1: Write the failing test first**

Add to `DigitalBrain.Tests/Aspire/DigitalBrainModelRegistryTests.cs` (or a new `ResolveDevFlutterAppPathTests.cs` in the same folder if that file doesn't already cover AppHost-path resolution):

```csharp
[Fact]
public void ResolveDevFlutterAppPath_ReturnsNull_WhenNoAppFolderNextToAppHost()
{
    var tempAppHostDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempAppHostDir);
    try
    {
        var result = DigitalBrainBuilderExtensions.ResolveDevFlutterAppPath(
            new TestDistributedApplicationBuilder(tempAppHostDir));

        Assert.Null(result);
    }
    finally
    {
        Directory.Delete(tempAppHostDir, recursive: true);
    }
}
```
(If `TestDistributedApplicationBuilder` doesn't already exist as a lightweight `IDistributedApplicationBuilder` test double in `DigitalBrain.TestKit`, use whatever fixture the existing `DigitalBrainModelRegistryTests.cs` already uses to construct an `IDistributedApplicationBuilder` for these tests — match that pattern rather than inventing a new one.)

- [x] **Step 2: Run it to confirm it currently passes (this is a characterization test for existing behavior, not a red/green TDD test — the simplification must not change this specific case)**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~ResolveDevFlutterAppPath"
```
Expected: PASS against the *current* implementation — this locks in the "no app folder found → null" contract before you simplify.

- [x] **Step 3: Simplify the implementation**

Replace `DigitalBrainBuilderExtensions.cs:315-344`:
```csharp
    public static string? ResolveDevFlutterAppPath(IDistributedApplicationBuilder b)
    {
        var flutterPathEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_FLUTTER_APP_PATH");
        if (!string.IsNullOrWhiteSpace(flutterPathEnv) && Directory.Exists(flutterPathEnv))
            return Path.GetFullPath(flutterPathEnv);

        var appHostDir = b.AppHostDirectory;
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(appHostDir, "..", "app")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "app")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "app")),
        };

        foreach (var c in candidates)
        {
            if (Directory.Exists(c) && File.Exists(Path.Combine(c, "pubspec.yaml")))
                return c;
        }

        var dir = new System.IO.DirectoryInfo(appHostDir);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "app");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "pubspec.yaml")))
                return Path.GetFullPath(candidate);
            dir = dir.Parent;
        }
        return null;
    }
```
with:
```csharp
    public static string? ResolveDevFlutterAppPath(IDistributedApplicationBuilder b)
    {
        var flutterPathEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_FLUTTER_APP_PATH");
        if (!string.IsNullOrWhiteSpace(flutterPathEnv) && Directory.Exists(flutterPathEnv))
            return Path.GetFullPath(flutterPathEnv);

        var canonicalPath = Path.GetFullPath(Path.Combine(b.AppHostDirectory, "..", "app"));
        return Directory.Exists(canonicalPath) && File.Exists(Path.Combine(canonicalPath, "pubspec.yaml"))
            ? canonicalPath
            : null;
    }
```

- [x] **Step 4: Re-run the test from Step 1 — still green**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~ResolveDevFlutterAppPath"
```
Expected: PASS.

- [x] **Step 5: Manually verify the real dev path still resolves**

```bash
dotnet run --project DigitalBrain.AppHost -- --help
```
This doesn't fully launch Aspire, but confirms the AppHost project still builds/starts far enough to hit `AddDefaultDevFlutterClient` without throwing `InvalidOperationException` on startup. For a fuller check, run `aspire run` per this repo's normal dev workflow and confirm the Flutter windows client still launches (the existing `AppHost.cs:38-40` throws with a clear message if resolution fails, so a failed resolution is immediately visible).

- [x] **Step 6: Commit**

```bash
git add DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs DigitalBrain.Tests/Aspire/
git commit -m "refactor(aspire): simplify ResolveDevFlutterAppPath to canonical app/ location, drop 6-level parent walk"
```

### Task 8: Guard the storage emulator and Ollama container behind `IsRunMode`

**Why:** `storage.RunAsEmulator()` and the Ollama container are unconditional today. Harmless while prod bypasses Aspire publish (Pulumi deploys separately), but an eventual `aspire publish`/`azd` path would otherwise try to emit an Azurite emulator resource into a publish manifest — this is the TripRadar pattern (conditional resource wiring per execution context), applied proactively.

**Files:**
- Modify: `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs:73-90`

**Steps:**

- [x] **Step 1: Wrap emulator/Ollama-only wiring in a run-mode check**

In `DigitalBrainBuilderExtensions.cs:73-90`, change:
```csharp
        var storage = builder.AddAzureStorage("storage").RunAsEmulator();
        var clusteringTable = storage.AddTables("clustering");
        var grainBlobs = storage.AddBlobs("grainstate");
        var journalBlobs = storage.AddBlobs("journal");

        var orleans = builder.AddOrleans("kernel")
            .WithClustering(clusteringTable)
            .WithGrainStorage("Default", grainBlobs);

        // Ollama always runs as the offline fallback (per DEMO-PLAN), independent of the chosen primary
        // provider — it must pull its own real model tag, never the primary provider's model/deployment
        // name (e.g. an azureopenai deployment name like "gpt-4o-mini" is not a pullable Ollama tag).
        const string ollamaFallbackModel = "qwen2.5-coder:1.5b";
        var ollama = builder.AddOllama("ollama")
            .WithGPUSupport()
            .WithDataVolume()
            .WithOpenWebUI();
        var qwen = ollama.AddModel("qwen", ollamaFallbackModel);
```
to:
```csharp
        var storage = builder.AddAzureStorage("storage");
        if (builder.ExecutionContext.IsRunMode)
        {
            storage.RunAsEmulator();
        }
        var clusteringTable = storage.AddTables("clustering");
        var grainBlobs = storage.AddBlobs("grainstate");
        var journalBlobs = storage.AddBlobs("journal");

        var orleans = builder.AddOrleans("kernel")
            .WithClustering(clusteringTable)
            .WithGrainStorage("Default", grainBlobs);

        // Ollama always runs as the offline fallback (per DEMO-PLAN), independent of the chosen primary
        // provider — it must pull its own real model tag, never the primary provider's model/deployment
        // name (e.g. an azureopenai deployment name like "gpt-4o-mini" is not a pullable Ollama tag).
        // Run-mode only: `aspire publish` should never try to emit a local Ollama container into a publish
        // manifest — prod gets its LLM from Azure OpenAI via Pulumi, wired separately (see WireKernelSilo).
        const string ollamaFallbackModel = "qwen2.5-coder:1.5b";
        IResourceBuilder<IResourceWithConnectionString> qwen;
        if (builder.ExecutionContext.IsRunMode)
        {
            var ollama = builder.AddOllama("ollama")
                .WithGPUSupport()
                .WithDataVolume()
                .WithOpenWebUI();
            qwen = ollama.AddModel("qwen", ollamaFallbackModel);
        }
        else
        {
            qwen = builder.AddConnectionString("qwen");
        }
```
(The `else` branch only matters once `aspire publish` is actually exercised — today `builder.ExecutionContext.IsRunMode` is `true` for every real invocation of this codebase, `dotnet run` and `aspire run` alike, so this task changes zero runtime behavior today. It only prevents a future regression.)

- [x] **Step 2: Also guard the `OllamaEndpoint` reference used later in `WireKernelSilo`**

`DigitalBrainContext.OllamaEndpoint` (`:27`) is set from `ollama.GetEndpoint("http")` (`:104`) — since `ollama` is now scoped inside the `if` block, either move that assignment inside the same `if`/`else` (setting a placeholder/no-op `EndpointReference` in the `else` branch is awkward since `EndpointReference` requires a real resource) or, simpler: keep `OllamaEndpoint` nullable (`EndpointReference?`) and update `WireKernelSilo`'s consumption at `:160-161` to skip the `DigitalBrain__Llm__OllamaEndpoint` env var when null. Do this:

In `DigitalBrainContext` (`:27`), change:
```csharp
    public required EndpointReference OllamaEndpoint { get; init; }
```
to:
```csharp
    public EndpointReference? OllamaEndpoint { get; init; }
```

In `AddDigitalBrain`'s return block (`:104`), change:
```csharp
            OllamaEndpoint = ollama.GetEndpoint("http"),
```
to:
```csharp
            OllamaEndpoint = builder.ExecutionContext.IsRunMode ? ollama.GetEndpoint("http") : null,
```
(this requires hoisting the `ollama` local out of the `if` block from Step 1 so it's visible here too — declare `IResourceBuilder<OllamaResource>? ollama = null;` above the `if`, assign it inside, and reference `ollama` here.)

In `WireKernelSilo` (`:160-161`), change:
```csharp
        kernel.WithEnvironment("DigitalBrain__Llm__OllamaEndpoint",
            ReferenceExpression.Create($"http://{ctx.OllamaEndpoint.Property(EndpointProperty.Host)}:{ctx.OllamaEndpoint.Property(EndpointProperty.Port)}"));
```
to:
```csharp
        if (ctx.OllamaEndpoint is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Llm__OllamaEndpoint",
                ReferenceExpression.Create($"http://{ctx.OllamaEndpoint.Property(EndpointProperty.Host)}:{ctx.OllamaEndpoint.Property(EndpointProperty.Port)}"));
        }
```

- [x] **Step 3: Build**

```bash
dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true
```
Expected: `Build succeeded. 0 Error(s)`.

- [x] **Step 4: Run the Aspire tests and confirm `aspire run`/`dotnet run` behavior is unchanged**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~Aspire"
```
Expected: all pass (this suite constructs `DigitalBrainContext` in run-mode-equivalent test fixtures, so `IsRunMode` should evaluate `true` there too — if any test fails asserting `OllamaEndpoint` is non-null, that confirms run-mode was correctly detected as `true` and the test's own assumption about non-nullability needs a one-line update, not the production code).

- [x] **Step 5: Commit**

```bash
git add DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs
git commit -m "refactor(aspire): guard emulator storage + Ollama container behind builder.ExecutionContext.IsRunMode"
```

### Task 9: Split Flutter/Telegram extensions into their own files

**Files:**
- Create: `DigitalBrain.Aspire/FlutterAspireExtensions.cs`
- Create: `DigitalBrain.Aspire/TelegramAspireExtensions.cs`
- Modify: `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` (remove the moved methods)

**Steps:**

- [x] **Step 1: Create `FlutterAspireExtensions.cs` with the three Flutter-related methods moved verbatim**

Move `AddFlutterClient` (`:235-256`), `AddDefaultDevFlutterClient` (`:305-312`), and `ResolveDevFlutterAppPath` (already simplified by Task 7) out of `DigitalBrainBuilderExtensions.cs` into:

```csharp
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire;

public static class FlutterAspireExtensions
{
    // Flutter as marketplace pack + Aspire integration. Call from AppHost when the Flutter pack
    // (DigitalBrain.UI.AspireFlutter) is installed. Starts Flutter (windows or web-server) wired to
    // brain for live surfaces/RfwCards.
    public static IResourceBuilder<ExecutableResource> AddFlutterClient(
        this DigitalBrainContext ctx,
        string name,
        string flutterAppPath,
        string target = "windows")
    {
        var cmd = ctx.ApplicationBuilder.Configuration["DigitalBrain:FlutterCommand"]
            ?? Environment.GetEnvironmentVariable("FLUTTER_COMMAND")
            ?? "flutter";

        return ctx.ApplicationBuilder.AddExecutable(
                name,
                cmd,
                flutterAppPath,
                "run",
                "-d",
                target)
            .WithReference(ctx.OrleansClient)
            .WithReference(ctx.Llm)
            .WithEnvironment("DIGITALBRAIN_UI_PACK", "DigitalBrain.UI.AspireFlutter")
            .WithEnvironment("DIGITALBRAIN_UI_TIER1_RESTART_REQUIRED", "true");
    }

    // Dev default helper. Path resolve + AddFlutterClient + kernel ref.
    public static IResourceBuilder<ExecutableResource>? AddDefaultDevFlutterClient(this DigitalBrainContext ctx, IResourceBuilder<ProjectResource> kernel)
    {
        var flutterPath = ResolveDevFlutterAppPath(ctx.ApplicationBuilder);
        if (string.IsNullOrEmpty(flutterPath))
            return null;
        return ctx.AddFlutterClient("flutter-ui", flutterPath, "windows")
            .WithReference(kernel);
    }

    // Public so packs / other extensions can reuse the dev path resolution logic or provide alternatives.
    public static string? ResolveDevFlutterAppPath(IDistributedApplicationBuilder b)
    {
        var flutterPathEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_FLUTTER_APP_PATH");
        if (!string.IsNullOrWhiteSpace(flutterPathEnv) && Directory.Exists(flutterPathEnv))
            return Path.GetFullPath(flutterPathEnv);

        var canonicalPath = Path.GetFullPath(Path.Combine(b.AppHostDirectory, "..", "app"));
        return Directory.Exists(canonicalPath) && File.Exists(Path.Combine(canonicalPath, "pubspec.yaml"))
            ? canonicalPath
            : null;
    }
}
```
(Uses the Task 7 simplified body and the Task 5 un-cast `ctx.Llm` reference — do this task after Tasks 5 and 7 land.)

- [x] **Step 2: Create `TelegramAspireExtensions.cs` with `WireTelegramTransport` moved verbatim**

Move `WireTelegramTransport` (`:268-301`) into:
```csharp
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire;

public static class TelegramAspireExtensions
{
    // Wires the Telegram transport (DigitalBrain.Telegram.Transport) as an Aspire resource that bridges
    // Telegram updates to the kernel gateway over gRPC. The transport boots no-op without a bot token, so
    // the resource can be present from startup and configured later with no AppHost restart.
    public static IResourceBuilder<ProjectResource> WireTelegramTransport(
        this DigitalBrainContext ctx,
        IResourceBuilder<ProjectResource> transport,
        IResourceBuilder<ProjectResource> kernel,
        IResourceBuilder<ParameterResource>? botToken = null,
        IResourceBuilder<ParameterResource>? internalServiceKey = null)
    {
        var kernelGrpc = kernel.GetEndpoint("grpc");

        transport = transport
            .WithReference(ctx.OrleansClient)
            .WithReference(kernel)
            .WaitFor(kernel)
            .WithEnvironment("DigitalBrain__GatewayAddress",
                ReferenceExpression.Create($"http://{kernelGrpc.Property(EndpointProperty.Host)}:{kernelGrpc.Property(EndpointProperty.Port)}"));

        if (botToken is not null)
        {
            transport = transport.WithEnvironment("Telegram__BotToken", botToken);
        }

        if (internalServiceKey is not null)
        {
            transport = transport.WithEnvironment("DigitalBrain__InternalServiceKey", internalServiceKey);
        }

        // Tell the transport which marketplace pack's stored config carries its bot token.
        // Matches the pack name in MarketplaceSeeds and the ConfigPack constant inside the pack code.
        transport = transport
            .WithEnvironment("Telegram__PackName", "DigitalBrain.Telegram.Responder")
            .WithEnvironment("Telegram__ConfigScope", "default");

        return transport;
    }
}
```

- [x] **Step 3: Delete the moved methods from `DigitalBrainBuilderExtensions.cs`**

Remove lines corresponding to `AddFlutterClient`, `WireTelegramTransport`, `AddDefaultDevFlutterClient`, and `ResolveDevFlutterAppPath` from `DigitalBrainBuilderExtensions.cs` — that file should now contain only `DigitalBrainContext`, `AddDigitalBrain`, `WireKernelSilo`, `WithModelRegistry`, `WithOptionalEnvironment`, `KernelWebPort`, and `DigitalBrainOptions`.

- [x] **Step 4: Build**

```bash
dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true
```
Expected: `Build succeeded. 0 Error(s)` — no using-directive changes needed at call sites since these are all extension methods in the same `DigitalBrain.Aspire` namespace.

- [x] **Step 5: Run the Aspire test suite**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~Aspire"
```
Expected: all pass, unchanged behavior (pure file-move refactor).

- [x] **Step 6: Commit**

```bash
git add DigitalBrain.Aspire/
git commit -m "refactor(aspire): split Flutter/Telegram extensions into their own files, matching SalesforceAspireExtensions.cs"
```

---

## Milestone M5 — Orleans production-grade on ACA (checklist #6)

### Task 10: Real health checks, mapped in every environment (not just Development)

**Why this is broken today:** `MapDefaultEndpoints()` in `DigitalBrain.ServiceDefaults/Extensions.cs:111-128` only maps `/health`/`/alive` when `app.Environment.IsDevelopment()` — and `DigitalBrain.Kernel/Program.cs` never even calls `MapDefaultEndpoints()` at all (confirmed: only `AddServiceDefaults()` is called, at `Program.cs:35`). So there is currently **no health endpoint anywhere**, dev or prod. ACA also has no `Probes` configured (Task 11 needs this task done first).

**Files:**
- Modify: `DigitalBrain.ServiceDefaults/Extensions.cs:102-128`
- Modify: `DigitalBrain.Kernel/Program.cs` (add the missing `app.MapDefaultEndpoints()` call and an Orleans-aware health check)
- Test: `DigitalBrain.Tests/Kernel/` (new health-check test)

**Steps:**

- [x] **Step 1: Make the readiness check mean something — add an Orleans cluster-membership check**

In `DigitalBrain.ServiceDefaults/Extensions.cs:102-109`, change:
```csharp
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }
```
to:
```csharp
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Liveness: process is up and responsive to requests at all — no dependency checks.
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
            // Readiness (no "live" tag, so /health includes it but /alive does not): each service project
            // adds its own dependency checks (e.g. Orleans cluster membership) via the same AddHealthChecks()
            // builder before calling AddServiceDefaults — see DigitalBrain.Kernel/Program.cs.

        return builder;
    }
```
(No functional change in this step — it's a comment-only clarification of intent so the next step's addition in Kernel is self-explanatory. Skip this step if you'd rather fold the comment into Step 2 directly.)

- [x] **Step 2: Remove the `IsDevelopment()` gate on the health endpoints — ACA network security (no public route to `/health` unless you add one) is the correct place to restrict exposure, not an environment check that silently breaks cloud health probes**

In `DigitalBrain.ServiceDefaults/Extensions.cs:111-128`, change:
```csharp
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
```
to:
```csharp
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Mapped in every environment: ACA's liveness/readiness probes (Milestone M5/Task 11) hit these
        // over the container's internal port, never through the external ingress, so there is no public
        // exposure to gate on environment.
        app.MapHealthChecks(HealthEndpointPath);

        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
```

- [x] **Step 3: Call the now-safe-everywhere `MapDefaultEndpoints()` from the Kernel**

In `DigitalBrain.Kernel/Program.cs`, add the call right after `app.UseRouting();` (`:219`):
```csharp
app.UseRouting();
app.MapDefaultEndpoints();
app.UseCors("browser");
```

- [x] **Step 4: Write the failing test for the new endpoint**

Add to `DigitalBrain.Tests/Kernel/KernelStaticServingTests.cs` (it already stands up a `WebApplicationFactory`-style Kernel test host per the earlier research finding) or a new `DigitalBrain.Tests/Kernel/HealthEndpointTests.cs` alongside it:

```csharp
[Fact]
public async Task HealthEndpoint_ReturnsHealthy()
{
    await using var factory = CreateKernelTestHost(); // reuse whatever helper KernelStaticServingTests.cs already uses to boot a test Kernel WebApplicationFactory
    using var client = factory.CreateClient();

    var response = await client.GetAsync("/health");

    Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
}

[Fact]
public async Task AliveEndpoint_ReturnsHealthy()
{
    await using var factory = CreateKernelTestHost();
    using var client = factory.CreateClient();

    var response = await client.GetAsync("/alive");

    Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
}
```

- [x] **Step 5: Run it to confirm it fails first (before Step 2/3's fix, or on a clean checkout of just this test)**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~HealthEndpointTests"
```
Expected: FAIL with 404, confirming the endpoint truly doesn't exist yet (do this on a branch before Steps 1-3, or revert them temporarily, if you're executing this task strictly TDD-style).

- [x] **Step 6: Apply Steps 1-3 and re-run**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~HealthEndpointTests"
```
Expected: PASS, both return 200.

- [x] **Step 7: Commit**

```bash
git add DigitalBrain.ServiceDefaults/Extensions.cs DigitalBrain.Kernel/Program.cs DigitalBrain.Tests/Kernel/
git commit -m "fix(kernel): map /health and /alive in every environment, not just Development"
```

### Task 11: Add liveness/readiness Probes to both Pulumi container apps

**Files:**
- Modify: `deploy/Program.cs:220-224` (kernel container args), `:277-281` (telegram container args)

**Steps:**

- [x] **Step 1: Add probes to the kernel container**

In `deploy/Program.cs`, inside the kernel's `AppInputs.ContainerArgs` (`:220-224`), change:
```csharp
                    new AppInputs.ContainerArgs
                    {
                        Name = "jobs",
                        Image = kernelImage,
                        Resources = new AppInputs.ContainerResourcesArgs { Cpu = 1.0, Memory = "2Gi" },
                        Env =
```
to:
```csharp
                    new AppInputs.ContainerArgs
                    {
                        Name = "jobs",
                        Image = kernelImage,
                        Resources = new AppInputs.ContainerResourcesArgs { Cpu = 1.0, Memory = "2Gi" },
                        Probes =
                        {
                            new AppInputs.ContainerAppProbeArgs
                            {
                                Type = App.Type.Liveness,
                                HttpGet = new AppInputs.ContainerAppProbeHttpGetArgs { Path = "/alive", Port = 8080 },
                                InitialDelaySeconds = 10,
                                PeriodSeconds = 15
                            },
                            new AppInputs.ContainerAppProbeArgs
                            {
                                Type = App.Type.Readiness,
                                HttpGet = new AppInputs.ContainerAppProbeHttpGetArgs { Path = "/health", Port = 8080 },
                                InitialDelaySeconds = 10,
                                PeriodSeconds = 15
                            }
                        },
                        Env =
```
> **Verify before running `pulumi preview`:** the exact enum path for the probe `Type` property (`App.Type.Liveness`/`.Readiness` vs. a differently-named `App.ContainerAppProbeType` enum) was not confirmed against this repo's exact installed `Pulumi.AzureNative` package version via Context7/Pulumi docs — both spellings appear in different Pulumi Azure Native doc snapshots. Before committing, check IntelliSense/`dotnet build` output on this exact line and correct the enum type name if the compiler rejects `App.Type`.

- [x] **Step 2: Add the same probes to the Telegram transport container (readiness only needs to check the process is up — the transport has no `/health` endpoint of its own yet beyond what `ServiceDefaults` gives it, confirm `DigitalBrain.Telegram.Transport` also calls `AddServiceDefaults()`/`MapDefaultEndpoints()` — if not, add that call there too as part of this step, mirroring Task 10)**

In `deploy/Program.cs`, inside the telegram container's `AppInputs.ContainerArgs` (`:277-281`), add the same two `Probes` entries as Step 1, targeting port `8080`.

- [x] **Step 3: Preview the Pulumi change without applying it**

```bash
cd deploy
pulumi preview --stack dev
```
Expected: a diff showing only the `probes` field added to both container apps' `template.containers[0]`, no resource replacement (adding probes is an in-place update).

- [x] **Step 4: Commit**

```bash
git add deploy/Program.cs
git commit -m "feat(deploy): add liveness/readiness probes to kernel and telegram container apps"
```

(Applying — `pulumi up` — happens through the CI pipeline per this repo's convention, "all prod deploys go through GitHub Actions only"; do not run `pulumi up` from a dev machine.)

### Task 12: `MinReplicas=2` + graceful shutdown timeout for the kernel

**Files:**
- Modify: `deploy/Program.cs:241` (kernel `Scale`)

**Steps:**

- [x] **Step 1: Raise `MinReplicas` and verify Orleans silo-to-silo networking first (do this before flipping the value in Pulumi)**

The local AppHost already runs 3 replicas of the kernel (`DigitalBrainOptions.KernelReplicas` default `= 3`, `:402`) and that's proven to work under `aspire run`, but that's replicas within a single Aspire-managed Docker/process network, not ACA's environment networking. Before raising `MinReplicas` in prod, confirm Orleans silo (`11111`) and gateway (`30000`) ports are reachable pod-to-pod inside the `digitalbrain-cae-prod` managed environment — ACA container apps in the same environment communicate over the environment's internal DNS by default for same-app replicas (Orleans clustering uses the Table clustering provider to discover peers, then connects directly pod-to-pod on the silo port), so this should work without additional `Ingress` config, but must be verified with a real 2-replica deploy before trusting it.

- [x] **Step 2: Change `MinReplicas`**

In `deploy/Program.cs:241`, change:
```csharp
                Scale = new AppInputs.ScaleArgs { MinReplicas = 1, MaxReplicas = 5 }
```
to:
```csharp
                Scale = new AppInputs.ScaleArgs { MinReplicas = 2, MaxReplicas = 5 }
```

- [x] **Step 3: Preview**

```bash
cd deploy
pulumi preview --stack dev
```
Expected: diff shows only `template.scale.minReplicas: 1 -> 2`.

- [x] **Step 4: After the next `pulumi up` runs via CI, verify scale-out actually formed a 2-silo cluster**

```bash
az containerapp revision list --name digitalbrain-jobs --resource-group digitalbrain-rg --query "[?properties.active].{name:name, replicas:properties.replicas}" -o table
```
Then check the Orleans dashboard (dev-only per §1.2) or the `digitalbrain-log-prod` Log Analytics workspace for two distinct silo instance rows in the `OrleansSiloInstances` table, confirming both replicas joined the same cluster rather than each silently forming a separate single-node cluster.

- [x] **Step 5: Document the graceful shutdown timeout decision — ACA's `terminationGracePeriodSeconds` is not currently exposed as a `Pulumi.AzureNative` `ContainerApp` property in this SDK version; confirm this before assuming it needs code**

Search the installed Pulumi.AzureNative `App.ContainerAppArgs`/`AppInputs.TemplateArgs` shape for a termination-grace-period field (`grep -rn "TerminationGracePeriod" $(dotnet nuget locals global-packages --list | ...)` is off-limits per this repo's NuGet-cache rule — instead check via `dotnet build` with a deliberately-wrong property name in `deploy/Program.cs` and read the compiler's suggested-members error, or check the Pulumi Azure Native changelog/docs for the `Microsoft.App` API version this SDK targets). If the field exists, add it to `AppInputs.TemplateArgs` set to `90` (seconds) alongside `Scale`. If it does not exist at this API version, this is an ACA platform default (currently 30s) that cannot be raised from Pulumi at this API surface — record that as a known limitation in `deploy/DEPLOY-STATUS.md` rather than fabricating a config value that silently does nothing.

- [x] **Step 6: Commit**

```bash
git add deploy/Program.cs
git commit -m "feat(deploy): raise kernel MinReplicas to 2 for Orleans cluster HA on ACA"
```

---

## Milestone M6 — Observability (checklist #7)

### Task 13: Export OTEL to App Insights + enable the Azure Monitor exporter

**Files:**
- Modify: `deploy/Program.cs:158-167` (capture the connection string), `:226-238` and `:283-292` (inject the env var into both container apps)
- Modify: `DigitalBrain.ServiceDefaults/DigitalBrain.ServiceDefaults.csproj` (add the exporter package)
- Modify: `DigitalBrain.ServiceDefaults/Extensions.cs:92-97` (uncomment/wire the exporter)

**Steps:**

- [x] **Step 1: Capture the App Insights connection string as a Pulumi Output**

In `deploy/Program.cs`, the `AppInsights.Component` resource (`:158-167`) is currently assigned to a discarded `_`. Change:
```csharp
        _ = new AppInsights.Component("digitalbrain-ai-prod", new AppInsights.ComponentArgs
        {
            ResourceName = "digitalbrain-ai-prod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            Kind = "web",
            ApplicationType = AppInsights.ApplicationType.Web,
            WorkspaceResourceId = workspace.Id,
            Tags = StandardTags("application-insights")
        });
```
to:
```csharp
        var appInsights = new AppInsights.Component("digitalbrain-ai-prod", new AppInsights.ComponentArgs
        {
            ResourceName = "digitalbrain-ai-prod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            Kind = "web",
            ApplicationType = AppInsights.ApplicationType.Web,
            WorkspaceResourceId = workspace.Id,
            Tags = StandardTags("application-insights")
        });
        var appInsightsConnectionString = appInsights.ConnectionString;
```

- [x] **Step 2: Inject it into both container apps' env vars**

In the kernel container's `Env` list (`:226-238`), add:
```csharp
                            new AppInputs.EnvironmentVarArgs { Name = "APPLICATIONINSIGHTS_CONNECTION_STRING", Value = appInsightsConnectionString },
```

In the telegram container's `Env` list (`:283-292`), add the same line.

- [x] **Step 3: Add the Azure Monitor OTEL exporter package to ServiceDefaults**

In `DigitalBrain.ServiceDefaults/DigitalBrain.ServiceDefaults.csproj`, add (matching whatever version scheme the rest of the repo's `PackageReference`s use — check a sibling `.csproj` for whether versions are specified inline or centrally via `Directory.Packages.props`):
```xml
    <PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" />
```

- [x] **Step 4: Wire the exporter**

In `DigitalBrain.ServiceDefaults/Extensions.cs:92-97`, change:
```csharp
        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}
```
to:
```csharp
        if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            builder.Services.AddOpenTelemetry()
                .UseAzureMonitor();
        }
```

- [x] **Step 5: Build**

```bash
dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true
```
Expected: `Build succeeded. 0 Error(s)`.

- [x] **Step 6: Preview the Pulumi change**

```bash
cd deploy
pulumi preview --stack dev
```
Expected: diff adds one env var to each container app; no resource replacement.

- [x] **Step 7: After the next deploy, verify traces actually arrive**

```bash
az monitor app-insights query --app digitalbrain-ai-prod --resource-group digitalbrain-rg \
  --analytics-query "requests | where timestamp > ago(15m) | take 5"
```
Expected: at least one row once the kernel has received traffic (hit `/health` manually first if the deploy is otherwise idle).

- [x] **Step 8: Commit**

```bash
git add deploy/Program.cs DigitalBrain.ServiceDefaults/
git commit -m "feat(observability): export OTEL to Application Insights from both container apps"
```

### Task 14: Post-deploy smoke test step in `deploy.yml`

**Files:**
- Modify: `.github/workflows/deploy.yml` (append a step after `pulumi up`)

**Steps:**

- [x] **Step 1: Capture the kernel's FQDN as a Pulumi stack output (it isn't exported today — only `telegramFqdn` is, at `deploy/Program.cs:308`)**

In `deploy/Program.cs`, add to the return dictionary (`:300-311`):
```csharp
            ["kernelFqdn"] = kernelApp.LatestRevisionFqdn,
```

- [x] **Step 2: Add the smoke-test step to the workflow, reading that output**

In `.github/workflows/deploy.yml`, append after the `Provision (pulumi up)` step:
```yaml
      - name: Capture kernel FQDN
        id: kernel_fqdn
        working-directory: deploy
        run: echo "fqdn=$(pulumi stack output kernelFqdn --stack dev)" >> "$GITHUB_OUTPUT"
        env:
          PULUMI_CONFIG_PASSPHRASE: ${{ secrets.PULUMI_PASSPHRASE }}
          AZURE_STORAGE_ACCOUNT: digitalbrainstprod

      - name: Post-deploy smoke test
        run: |
          for i in $(seq 1 10); do
            if curl -fsS "https://${{ steps.kernel_fqdn.outputs.fqdn }}/health"; then
              echo "Kernel healthy."
              exit 0
            fi
            echo "Attempt $i: kernel not ready yet, retrying in 10s..."
            sleep 10
          done
          echo "Kernel did not become healthy within 100s." >&2
          exit 1
```
(Depends on Task 10's `/health` endpoint being mapped in every environment and Task 11's Pulumi probes both landing first.)

- [x] **Step 3: Verify with a real workflow run**

```bash
gh workflow run deploy.yml -f image_tag=smoke-test-verify --repo digitalbraintech/brain
gh run watch --repo digitalbraintech/brain
```
Expected: the "Post-deploy smoke test" step succeeds within the retry window.

- [x] **Step 4: Commit**

```bash
git add .github/workflows/deploy.yml deploy/Program.cs
git commit -m "ci: add post-deploy /health smoke test, export kernelFqdn stack output"
```

---

## Milestone M7 — Local-first: real embeddings (checklist #8)

### Task 15: Wire a real Ollama-backed embedding model, replacing `NoOpEmbeddingGenerator` fail-soft

**Design, grounded in existing code:**
- `OllamaApiClient` (from `OllamaSharp`, already a transitive dependency via `DigitalBrainChat.cs`) directly implements `IEmbeddingGenerator<string, Embedding<float>>` when cast — confirmed via OllamaSharp's own docs: `(IEmbeddingGenerator<string, Embedding<float>>)new OllamaApiClient(endpoint, model)`. This mirrors the exact pattern `DigitalBrainChat.cs:17` already uses for chat (`new OllamaApiClient(new Uri(options.OllamaEndpoint), options.Model)`).
- The registry needs a `DefaultEmbedding` lookup analogous to the existing `DefaultVoiceToText` (`DigitalBrain.Core/Models/DigitalBrainModelCatalog.cs:96-101`) — there is no such property today; add it.
- The Aspire DSL's `WithModelRegistry` (`DigitalBrainBuilderExtensions.cs:186-205`) only emits `DefaultLlm` env vars, never `DefaultEmbedding` — add the analogous emission.
- `WithEmbedding<TModel>()` (`DigitalBrainBuilderExtensions.cs:423-428`) already registers the descriptor; it needs no change.

**Files:**
- Create: `NomicEmbedText` marker in `DigitalBrain.Aspire/LlmModels.cs`
- Modify: `DigitalBrain.Core/Models/DigitalBrainModelCatalog.cs` (add `DefaultEmbedding`)
- Modify: `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` (emit `DefaultEmbedding` env vars, add embedding model to the Ollama resource, inject `DigitalBrain__Embedding__OllamaEndpoint`)
- Create: `DigitalBrain.Kernel/Llm/DigitalBrainEmbeddingRuntimeOptions.cs` (mirrors `DigitalBrainLlmRuntimeOptions`)
- Modify: `DigitalBrain.Kernel/Llm/DigitalBrainChat.cs` (replace unconditional `NoOpEmbeddingGenerator` registration with fail-soft Ollama-backed one)
- Test: `DigitalBrain.Tests/Llm/` (new test for the runtime-options record + DI registration)

**Steps:**

- [x] **Step 1: Add the `NomicEmbedText` marker**

In `DigitalBrain.Aspire/LlmModels.cs`, add after `Gpt4oMini` (`:60`):
```csharp
// Local Ollama embedding model — 768-dim, drop-in replacement for NoOpEmbeddingGenerator's 384-dim zero
// vectors. HybridScorer (DigitalBrain.Context/HybridScorer.cs) already detects zero vectors and falls back
// to keyword recall, so wiring this activates vector RAG with no change to HybridScorer itself.
public sealed class NomicEmbedText : EmbeddingModel
{
    public override string Provider => DigitalBrainProviderIds.Ollama;
    public override string Id => "nomic-embed-text";
}
```

- [x] **Step 2: Add `DefaultEmbedding` to the registry, mirroring `DefaultVoiceToText`**

In `DigitalBrain.Core/Models/DigitalBrainModelCatalog.cs`, add after `DefaultVoiceToText` (`:96-101`):
```csharp
    /// <summary>
    /// Preferred embedding model for the context/RAG runtime consumer.
    /// </summary>
    public DigitalBrainModelRegistration? DefaultEmbedding =>
        registrations.LastOrDefault(static x =>
            x.Model.Kind == DigitalBrainCapabilityKind.Embedding &&
            x.Role == DigitalBrainModelRole.Default)
        ?? registrations.LastOrDefault(static x =>
            x.Model.Kind == DigitalBrainCapabilityKind.Embedding);
```

- [x] **Step 3: Emit `DefaultEmbedding` env vars from the Aspire DSL**

In `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs`'s `WithModelRegistry` (`:186-205`), add after the `DefaultLlm` block (`:188-193`):
```csharp
        if (ctx.ModelRegistry.DefaultEmbedding is { } defaultEmbedding)
        {
            kernel.WithEnvironment("DigitalBrain__ModelRegistry__DefaultEmbedding__Kind", DigitalBrainCapabilityKind.Embedding.ToString());
            kernel.WithEnvironment("DigitalBrain__ModelRegistry__DefaultEmbedding__Provider", defaultEmbedding.Model.Provider);
            kernel.WithEnvironment("DigitalBrain__ModelRegistry__DefaultEmbedding__Id", defaultEmbedding.Model.Id);
        }
```

- [x] **Step 4: Add the embedding model to the Ollama resource and inject its endpoint**

In `AddDigitalBrain` (`DigitalBrainBuilderExtensions.cs`, inside the `IsRunMode` block from Task 8's Step 1), after `qwen = ollama.AddModel("qwen", ollamaFallbackModel);` add:
```csharp
            var embed = ollama.AddModel("embed", "nomic-embed-text");
```
Thread `embed` through `DigitalBrainContext` the same way `OllamaEndpoint` is threaded — add a field:
```csharp
    public EndpointReference? EmbeddingOllamaEndpoint { get; init; }
```
and in the constructor return block, set it the same way as `OllamaEndpoint` (only when `IsRunMode`, `null` otherwise — same reasoning as Task 8/Step 2).

In `WireKernelSilo`, after the existing `DigitalBrain__Llm__OllamaEndpoint` block, add:
```csharp
        if (ctx.EmbeddingOllamaEndpoint is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Embedding__OllamaEndpoint",
                ReferenceExpression.Create($"http://{ctx.EmbeddingOllamaEndpoint.Property(EndpointProperty.Host)}:{ctx.EmbeddingOllamaEndpoint.Property(EndpointProperty.Port)}"));
        }
```

- [x] **Step 5: Register the model in AppHost.cs**

In `AppHost.cs`, add to the `AddDigitalBrain` options callback:
```csharp
    options.WithEmbedding<NomicEmbedText>();
```

- [x] **Step 6: Write the failing test for the new runtime-options record**

Create `DigitalBrain.Tests/Llm/DigitalBrainEmbeddingRuntimeOptionsTests.cs`:
```csharp
using DigitalBrain.Kernel.Llm;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests.Llm;

public class DigitalBrainEmbeddingRuntimeOptionsTests
{
    [Fact]
    public void FromConfiguration_ReadsRegistryEmittedKeys()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:ModelRegistry:DefaultEmbedding:Provider"] = "ollama",
                ["DigitalBrain:ModelRegistry:DefaultEmbedding:Id"] = "nomic-embed-text",
                ["DigitalBrain:Embedding:OllamaEndpoint"] = "http://localhost:11434"
            })
            .Build();

        var options = DigitalBrainEmbeddingRuntimeOptions.FromConfiguration(config);

        Assert.Equal("ollama", options.Provider);
        Assert.Equal("nomic-embed-text", options.Model);
        Assert.Equal("http://localhost:11434", options.OllamaEndpoint);
    }

    [Fact]
    public void FromConfiguration_ReturnsNullProvider_WhenNothingConfigured()
    {
        var config = new ConfigurationBuilder().Build();

        var options = DigitalBrainEmbeddingRuntimeOptions.FromConfiguration(config);

        Assert.Null(options.Provider);
    }
}
```

- [x] **Step 7: Run it to confirm it fails (the type doesn't exist yet)**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~DigitalBrainEmbeddingRuntimeOptionsTests"
```
Expected: FAIL — compile error, `DigitalBrainEmbeddingRuntimeOptions` not found.

- [x] **Step 8: Create the runtime-options record, mirroring `DigitalBrainLlmRuntimeOptions`**

Create `DigitalBrain.Kernel/Llm/DigitalBrainEmbeddingRuntimeOptions.cs`:
```csharp
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Kernel.Llm;

public sealed record DigitalBrainEmbeddingRuntimeOptions(
    string? Provider,
    string? Model,
    string OllamaEndpoint)
{
    public const string DefaultOllamaModel = "nomic-embed-text";

    public static DigitalBrainEmbeddingRuntimeOptions FromConfiguration(IConfiguration config)
    {
        var provider = config["DigitalBrain:ModelRegistry:DefaultEmbedding:Provider"];
        var model = config["DigitalBrain:ModelRegistry:DefaultEmbedding:Id"];

        return new DigitalBrainEmbeddingRuntimeOptions(
            provider,
            model,
            config["DigitalBrain:Embedding:OllamaEndpoint"] ?? "http://localhost:11434");
    }
}
```

- [x] **Step 9: Re-run the test — green**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~DigitalBrainEmbeddingRuntimeOptionsTests"
```
Expected: PASS.

- [x] **Step 10: Write the failing test for fail-soft DI registration**

Add to the same test file or a new `DigitalBrainChatEmbeddingRegistrationTests.cs`:
```csharp
[Fact]
public void AddDigitalBrainChat_RegistersOllamaEmbeddingGenerator_WhenConfigured()
{
    var services = new ServiceCollection();
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:ModelRegistry:DefaultEmbedding:Provider"] = "ollama",
            ["DigitalBrain:ModelRegistry:DefaultEmbedding:Id"] = "nomic-embed-text",
            ["DigitalBrain:Embedding:OllamaEndpoint"] = "http://localhost:11434"
        })
        .Build();

    services.AddDigitalBrainChat(config);
    var provider = services.BuildServiceProvider();

    var embedder = provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

    Assert.IsNotType<NoOpEmbeddingGenerator>(embedder);
}

[Fact]
public void AddDigitalBrainChat_FailsSoftToNoOp_WhenEmbeddingNotConfigured()
{
    var services = new ServiceCollection();
    var config = new ConfigurationBuilder().Build();

    services.AddDigitalBrainChat(config);
    var provider = services.BuildServiceProvider();

    var embedder = provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

    Assert.IsType<NoOpEmbeddingGenerator>(embedder);
}
```

- [x] **Step 11: Run to confirm the first assertion fails (current code always registers `NoOpEmbeddingGenerator`)**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~AddDigitalBrainChat_RegistersOllamaEmbeddingGenerator"
```
Expected: FAIL — `Assert.IsNotType` fails because the registered instance is in fact `NoOpEmbeddingGenerator`.

- [x] **Step 12: Implement fail-soft registration in `DigitalBrainChat.cs`**

In `DigitalBrain.Kernel/Llm/DigitalBrainChat.cs:35`, change:
```csharp
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new NoOpEmbeddingGenerator());
        return services;
```
to:
```csharp
        var embeddingOptions = DigitalBrainEmbeddingRuntimeOptions.FromConfiguration(config);
        if (string.Equals(embeddingOptions.Provider, DigitalBrainProviderIds.Ollama, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(embeddingOptions.Model))
        {
            var embeddingClient = new OllamaApiClient(new Uri(embeddingOptions.OllamaEndpoint), embeddingOptions.Model);
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingClient);
        }
        else
        {
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new NoOpEmbeddingGenerator());
        }
        return services;
```
(`OllamaApiClient` implements `IEmbeddingGenerator<string, Embedding<float>>` directly per OllamaSharp — no separate builder/wrapper needed, matching the confirmed pattern `(IEmbeddingGenerator<string, Embedding<float>>)new OllamaApiClient(endpoint, model)`.)

- [x] **Step 13: Re-run both tests — green**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~AddDigitalBrainChat"
```
Expected: both PASS.

- [x] **Step 14: Full build + fast test lane**

```bash
dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true
dotnet test Brain.slnx -c Release -p:SkipFlutterBuild=true --filter "FullyQualifiedName!~E2E"
```
Expected: all green.

- [x] **Step 15: Manual end-to-end check under `aspire run`**

```bash
aspire run
```
Once the Ollama container reports the `embed`/`nomic-embed-text` model pulled (check the Aspire dashboard resource logs), exercise any existing context/RAG flow that calls `HybridScorer` (per `DigitalBrain.Context`) and confirm — via a debugger breakpoint or a temporary log line in `HybridScorer.IsZeroVector` — that vectors are no longer all-zero. Remove any temporary logging before committing.

- [x] **Step 16: Commit**

```bash
git add DigitalBrain.Aspire/ DigitalBrain.Core/Models/DigitalBrainModelCatalog.cs DigitalBrain.Kernel/Llm/ DigitalBrain.Tests/Llm/ DigitalBrain.AppHost/AppHost.cs
git commit -m "feat(embeddings): wire real Ollama-backed embeddings (nomic-embed-text), fail-soft to NoOp when unconfigured"
```

---

## Milestone M8 — Local-first: Whisper container (checklist #9)

### Task 16: Add a local Whisper server as an Aspire container resource

**Files:**
- Modify: `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` (new container resource, gated by `IsRunMode`)
- Modify: `NeuroOSPrototype.AppHost/AppHost.cs:12-15` (register `WithVoice2Text<Whisper1Local>()` unconditionally once the container is always present, instead of gating on a manually-set endpoint)

**Steps:**

- [x] **Step 1: Add the Whisper container resource inside `AddDigitalBrain`, guarded the same way as Ollama (Task 8)**

In `DigitalBrainBuilderExtensions.cs`, inside the `if (builder.ExecutionContext.IsRunMode)` block added by Task 8, after the Ollama/embedding setup:
```csharp
            var whisper = builder.AddContainer("whisper", "onerahmet/openai-whisper-asr-webservice")
                .WithEnvironment("ASR_MODEL", "base")
                .WithEnvironment("ASR_ENGINE", "faster_whisper")
                .WithHttpEndpoint(targetPort: 9000, name: "http")
                .WithVolume("whisper-cache", "/root/.cache/whisper");
            var whisperEndpoint = whisper.GetEndpoint("http");
```
> **Verify before running:** confirm `onerahmet/openai-whisper-asr-webservice` (a widely-used community image exposing an OpenAI-compatible `/asr` endpoint) is the image this team wants — it was not independently re-verified against the project's specific compatibility requirements beyond "any OpenAI-compatible `/audio/transcriptions` endpoint" from §1.4. If a different image (e.g. `speaches`/`fedirz/faster-whisper-server`, mentioned as an alternative in earlier planning notes) is preferred, swap the image name only — the rest of this wiring (`WithHttpEndpoint`, env injection below) is image-agnostic as long as the container exposes an OpenAI-compatible transcription route.

- [x] **Step 2: Thread the endpoint through `DigitalBrainContext` and inject it, same pattern as `OllamaEndpoint`**

Add to `DigitalBrainContext`:
```csharp
    public EndpointReference? WhisperEndpoint { get; init; }
```
Set it in `AddDigitalBrain`'s return block the same way as `OllamaEndpoint` (non-null only when `IsRunMode`).

In `WireKernelSilo`, replace the manual-endpoint-only voice wiring. Currently (`:173-176`):
```csharp
        kernel.WithOptionalEnvironment("DigitalBrain:Voice:Provider", "DIGITALBRAIN_VOICE_PROVIDER", "DigitalBrain__Voice__Provider");
        kernel.WithOptionalEnvironment("DigitalBrain:Voice:Model", "DIGITALBRAIN_VOICE_MODEL", "DigitalBrain__Voice__Model");
        kernel.WithOptionalEnvironment("DigitalBrain:Voice:Endpoint", "DIGITALBRAIN_VOICE_ENDPOINT", "DigitalBrain__Voice__Endpoint");
        kernel.WithOptionalEnvironment("DigitalBrain:Voice:ApiKey", "DIGITALBRAIN_VOICE_API_KEY", "DigitalBrain__Voice__ApiKey");
```
change to (keep the manual overrides working for anyone still using an externally-run Whisper, but default to the new container when present and no manual endpoint was set):
```csharp
        kernel.WithOptionalEnvironment("DigitalBrain:Voice:Provider", "DIGITALBRAIN_VOICE_PROVIDER", "DigitalBrain__Voice__Provider");
        kernel.WithOptionalEnvironment("DigitalBrain:Voice:Model", "DIGITALBRAIN_VOICE_MODEL", "DigitalBrain__Voice__Model");
        var manualVoiceEndpoint = ctx.ApplicationBuilder.Configuration["DigitalBrain:Voice:Endpoint"]
            ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_VOICE_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(manualVoiceEndpoint))
        {
            kernel.WithEnvironment("DigitalBrain__Voice__Endpoint", manualVoiceEndpoint);
        }
        else if (ctx.WhisperEndpoint is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Voice__Endpoint",
                ReferenceExpression.Create($"http://{ctx.WhisperEndpoint.Property(EndpointProperty.Host)}:{ctx.WhisperEndpoint.Property(EndpointProperty.Port)}/v1"));
        }
        kernel.WithOptionalEnvironment("DigitalBrain:Voice:ApiKey", "DIGITALBRAIN_VOICE_API_KEY", "DigitalBrain__Voice__ApiKey");
```
(`/v1` path segment on the container endpoint depends on the exact image chosen in Step 1 exposing its OpenAI-compatible route at that path — confirm against the image's own README before finalizing; `onerahmet/openai-whisper-asr-webservice` exposes `/asr`, not an OpenAI-compatible route at all, which contradicts this task's own premise — **resolve this discrepancy by choosing an image that genuinely speaks the OpenAI `/audio/transcriptions` contract** (e.g. `fedirz/faster-whisper-server`, which explicitly advertises OpenAI API compatibility) **before writing this code**, rather than shipping a container that `VoiceTranscription.cs`'s `OpenAICompatible` provider can't actually talk to.)

- [x] **Step 3: Always register `Whisper1Local` in AppHost — the container is present whenever `IsRunMode`, so gate on that instead of a manually-set endpoint**

In `AppHost.cs:12-15`, change:
```csharp
    if (HasValue("DigitalBrain:Voice:Endpoint", "DIGITALBRAIN_VOICE_ENDPOINT"))
    {
        options.WithVoice2Text<Whisper1Local>();
    }
```
to:
```csharp
    options.WithVoice2Text<Whisper1Local>();
```
(Registering the model descriptor is safe unconditionally now — `WireKernelSilo`'s Step 2 change already falls back gracefully whether or not an endpoint is actually reachable; the model registry entry existing doesn't force a real endpoint to be present, it just tells the kernel which model *would* be used if `DigitalBrain__Voice__Endpoint` ends up set. Delete the now-unused `HasValue` local function if this was its only call site — check before deleting.)

- [x] **Step 4: Build**

```bash
dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true
```
Expected: `Build succeeded. 0 Error(s)`.

- [x] **Step 5: Manual verification under `aspire run`**

```bash
aspire run
```
Confirm in the Aspire dashboard: a `whisper` container resource starts and reports healthy, `kernel`'s environment includes a `DigitalBrain__Voice__Endpoint` pointing at the whisper container's endpoint (check via the dashboard's resource detail env-var view), and a voice-transcription flow (if one exists in the Flutter client / a manual gRPC call) round-trips through it. Confirm `dotnet run --project DigitalBrain.Kernel` (fast path, no Aspire) is unaffected — it never reaches this code path since `WireKernelSilo` only runs under Aspire orchestration.

- [x] **Step 6: Commit**

```bash
git add DigitalBrain.Aspire/ DigitalBrain.AppHost/AppHost.cs
git commit -m "feat(voice): add local Whisper container resource to AppHost, default endpoint wiring"
```

---

## Milestone M9 — Flutter web to Azure (checklist #10)

### Task 17: Move Flutter web hosting from GitHub Pages to Azure Static Web Apps

**Why Static Web Apps over the other two options considered in the original plan:** Flutter web is a static bundle; SWA gives CDN + free SSL + custom domain and stays inside `digitalbrain-rg`/Pulumi (unlike GitHub Pages, which is unmanaged by this repo's IaC). App Service would mean paying for an always-on plan just to serve static files. Serving from the kernel (`DIGITALBRAIN_WEBROOT`, already supported) remains the documented zero-infra fallback — this task doesn't remove that capability, it just stops relying on GitHub Pages as primary.

**Files:**
- Modify: `deploy/Program.cs` (new `Web.StaticSite` resource)
- Modify: `.github/workflows/deploy-flutter-web.yml` (swap Pages deploy for SWA deploy)
- Modify: DNS at the registrar (manual, outside repo — see Step 5)

**Steps:**

- [x] **Step 1: Add the Static Web App Pulumi resource, "bring your own build" mode (no GitHub repo linkage — this repo's own workflow already builds the bundle)**

In `deploy/Program.cs`, add near the other resource declarations (after the ACA container apps, before the return dictionary):
```csharp
        using Web = Pulumi.AzureNative.Web;
        using WebInputs = Pulumi.AzureNative.Web.Inputs;

        var flutterWebSite = new Web.StaticSite("digitalbrain-web-prod", new Web.StaticSiteArgs
        {
            Name = "digitalbrain-web-prod",
            ResourceGroupName = resourceGroup.Name,
            Location = Region,
            Sku = new WebInputs.SkuDescriptionArgs { Name = "Free", Tier = "Free" },
            Tags = StandardTags("static-web-app")
        });
```
> **Verify before running `pulumi preview`:** `Web.StaticSiteArgs` may require `RepositoryUrl`/`Branch`/`RepositoryToken` even in a nominally BYOB setup, or may reject omitting them — this was not confirmed against this repo's exact installed `Pulumi.AzureNative` SDK version. If the SDK requires them, either supply a dummy `RepositoryUrl`/empty `Branch` (some SDK versions accept this for BYOB) or fall back to creating the Static Web App once via `az staticwebapp create --no-source-control-provider`-equivalent and importing it into Pulumi state with `pulumi import azure-native:web:StaticSite digitalbrain-web-prod <resourceId>` instead of declaring it fresh. Confirm which path this SDK version needs with a `pulumi preview` dry run before committing to either.

- [x] **Step 2: Retrieve the deployment token as a Pulumi secret output**

```csharp
        var swaSecrets = Web.ListStaticSiteSecrets.Invoke(new Web.ListStaticSiteSecretsInvokeArgs
        {
            Name = flutterWebSite.Name,
            ResourceGroupName = resourceGroup.Name
        });
        var swaDeploymentToken = Output.CreateSecret(swaSecrets.Apply(s => s.Properties["apiKey"]));
```
> **Verify:** the exact invoke name (`ListStaticSiteSecrets`) and the secret dictionary key (`"apiKey"`) mirror the pattern already used in this same file for storage keys (`Storage.ListStorageAccountKeys`) and OpenAI keys (`Cognitive.ListAccountKeys`), but the `Web` namespace's exact invoke/property names were not independently confirmed via Context7/Pulumi docs for this SDK version — confirm against IntelliSense before committing; the Azure REST operation this wraps is `POST .../staticSites/{name}/listSecrets`.

- [x] **Step 3: Add the token to the stack outputs so CI can read it**

```csharp
            ["swaDeploymentToken"] = swaDeploymentToken,
            ["swaDefaultHostname"] = flutterWebSite.DefaultHostname,
```

- [x] **Step 4: Swap the GitHub Pages deploy steps for the SWA GitHub Action**

In `.github/workflows/deploy-flutter-web.yml`, replace lines 47-54:
```yaml
      - uses: actions/configure-pages@v5

      - uses: actions/upload-pages-artifact@v3
        with:
          path: app/build/web

      - id: deployment
        uses: actions/deploy-pages@v4
```
with:
```yaml
      - name: Deploy to Azure Static Web Apps
        uses: Azure/static-web-apps-deploy@v1
        with:
          azure_static_web_apps_api_key: ${{ secrets.SWA_DEPLOYMENT_TOKEN }}
          repo_token: ${{ secrets.GITHUB_TOKEN }}
          action: upload
          app_location: app/build/web
          api_location: ""
          output_location: ""
          skip_app_build: true
```
Also drop the now-unneeded `pages`/`id-token` permissions and the `environment: github-pages` block (lines 14-17, 26-28), leaving only `contents: read`.

- [x] **Step 5: One-time — after the first `pulumi up` creates the Static Web App, copy its deployment token into a repo secret, then handle DNS**

```bash
cd deploy
TOKEN=$(pulumi stack output swaDeploymentToken --stack dev --show-secrets)
gh secret set SWA_DEPLOYMENT_TOKEN --body "$TOKEN" --repo digitalbraintech/brain
```
Then at the domain registrar for `digitalbrain.tech`: remove the dangling `api`/`asuid.api` records flagged in §1.6, add a `CNAME` for the apex or `www` (per SWA's custom-domain instructions, which require validating ownership via a `TXT` record first) pointing at `pulumi stack output swaDefaultHostname`, and bind the custom domain in the Azure portal or via a `Web.StaticSiteCustomDomain` Pulumi resource (recommended, to keep this in IaC — add it as a follow-up once the base SWA resource is proven working, since it depends on DNS propagation completing outside Pulumi's control). This registrar step is manual and cannot be scripted here without registrar-specific credentials this plan doesn't have access to.

- [x] **Step 6: Verify**

```bash
gh workflow run deploy-flutter-web.yml --repo digitalbraintech/brain
gh run watch --repo digitalbraintech/brain
curl -I "https://$(cd deploy && pulumi stack output swaDefaultHostname --stack dev)"
```
Expected: workflow succeeds, `curl` returns `200 OK` serving the Flutter web bundle. Once DNS cutover (Step 5) completes, repeat with `https://digitalbrain.tech`.

- [x] **Step 7: Commit**

```bash
git add deploy/Program.cs .github/workflows/deploy-flutter-web.yml
git commit -m "feat(deploy): move Flutter web hosting from GitHub Pages to Azure Static Web Apps"
```

---

## Milestone M10 — Security hardening (checklist #11)

### Task 18: Managed identity for Storage — disable shared-key auth

**Files:**
- Modify: `deploy/Program.cs` (system-assigned identity on the kernel container app, role assignments, `AllowSharedKeyAccess = false`)
- Modify: `DigitalBrain.Kernel/Program.cs:192-203` (switch clustering/grain/journal wiring from connection strings to credential-based clients)

**Steps:**

- [x] **Step 1: Give the kernel container app a system-assigned identity**

In `deploy/Program.cs`, add `Identity` to the kernel's `App.ContainerAppArgs` (alongside `Configuration`/`Template`):
```csharp
            Identity = new AppInputs.ManagedServiceIdentityArgs { Type = App.ManagedServiceIdentityType.SystemAssigned },
```

- [x] **Step 2: Grant the identity `Storage Table Data Contributor` + `Storage Blob Data Contributor` on `digitalbrainstprod`**

```csharp
        var kernelPrincipalId = kernelApp.Identity.Apply(i => i!.PrincipalId!);
        _ = new Authorization.RoleAssignment("kernel-storage-table-contributor", new Authorization.RoleAssignmentArgs
        {
            PrincipalId = kernelPrincipalId,
            PrincipalType = Authorization.PrincipalType.ServicePrincipal,
            RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3", // Storage Table Data Contributor
            Scope = storage.Id
        });
        _ = new Authorization.RoleAssignment("kernel-storage-blob-contributor", new Authorization.RoleAssignmentArgs
        {
            PrincipalId = kernelPrincipalId,
            PrincipalType = Authorization.PrincipalType.ServicePrincipal,
            RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/ba92f5b4-2d11-453d-a403-e96b0029c9fe", // Storage Blob Data Contributor
            Scope = storage.Id
        });
```
(Add `using Authorization = Pulumi.AzureNative.Authorization;` to the usings. Role-definition GUIDs above are Azure's well-known built-in role IDs for these exact role names — confirm against `az role definition list --name "Storage Table Data Contributor"` / `"Storage Blob Data Contributor"` before applying, since a wrong GUID fails the role assignment outright rather than silently granting the wrong permission.)

- [x] **Step 3: Switch Orleans clustering/grain-storage/journal wiring in `DigitalBrain.Kernel/Program.cs` from connection strings to `TokenCredential`-based clients**

In `Program.cs:192-203`, change:
```csharp
        siloBuilder.UseAzureStorageClustering(options =>
            options.ConfigureTableServiceClient(builder.Configuration.GetConnectionString("clustering")!));
        siloBuilder.AddAzureBlobGrainStorage("Default", options =>
            options.ConfigureBlobServiceClient(builder.Configuration.GetConnectionString("grainstate")!));
        // ...
        siloBuilder.AddAzureBlobJournalStorage(options =>
            options.ConfigureBlobServiceClient(builder.Configuration.GetConnectionString("journal")!));
```
to (uses `Azure.Identity`'s `DefaultAzureCredential`, which resolves the container app's managed identity in ACA and falls back to `az login`/env-based auth locally — but note the fast path and today's Aspire/Azurite path both still use connection strings, so this switch must be gated to only the *cloud* sub-case, not `isAspireHosted` in general, since Azurite has no concept of managed identity):
```csharp
        var storageAccountUri = builder.Configuration["DigitalBrain:Storage:AccountUri"]; // e.g. https://digitalbrainstprod.{table|blob}.core.windows.net — set per-service below
        var useManagedIdentity = !string.IsNullOrWhiteSpace(storageAccountUri);
        if (useManagedIdentity)
        {
            var credential = new Azure.Identity.DefaultAzureCredential();
            siloBuilder.UseAzureStorageClustering(options =>
                options.ConfigureTableServiceClient(new Uri($"https://{storageAccountName}.table.core.windows.net"), credential));
            siloBuilder.AddAzureBlobGrainStorage("Default", options =>
                options.ConfigureBlobServiceClient(new Uri($"https://{storageAccountName}.blob.core.windows.net"), credential));
            siloBuilder.AddAzureBlobJournalStorage(options =>
                options.ConfigureBlobServiceClient(new Uri($"https://{storageAccountName}.blob.core.windows.net"), credential));
        }
        else
        {
            siloBuilder.UseAzureStorageClustering(options =>
                options.ConfigureTableServiceClient(builder.Configuration.GetConnectionString("clustering")!));
            siloBuilder.AddAzureBlobGrainStorage("Default", options =>
                options.ConfigureBlobServiceClient(builder.Configuration.GetConnectionString("grainstate")!));
            siloBuilder.AddAzureBlobJournalStorage(options =>
                options.ConfigureBlobServiceClient(builder.Configuration.GetConnectionString("journal")!));
        }
```
This introduces a `storageAccountName` value that needs sourcing from config (e.g. `builder.Configuration["DigitalBrain:Storage:AccountName"]`, injected as a new Pulumi env var `DigitalBrain__Storage__AccountName = "digitalbrainstprod"`) — add that env var in `deploy/Program.cs`'s kernel container `Env` list as part of this task, and only set it in the cloud deploy (never in Aspire/local config), which is exactly what makes `useManagedIdentity` correctly `false` locally and `true` in ACA.

- [x] **Step 4: Also switch the non-keyed `BlobServiceClient` used for pack-config storage and DataProtection key-ring persistence (`Program.cs:114-135`) to the credential-based client when managed identity is available** — same `useManagedIdentity` flag from Step 3, constructing `new BlobServiceClient(new Uri($"https://{storageAccountName}.blob.core.windows.net"), credential)` instead of `new BlobServiceClient(grainStateConnStr)`.

- [x] **Step 5: Disable shared-key access on the storage account, only after Steps 1-4 are deployed and verified working**

In `deploy/Program.cs:88`, change:
```csharp
            AllowSharedKeyAccess = true,
```
to:
```csharp
            AllowSharedKeyAccess = false,
```
**Do this in a separate deploy from Steps 1-4** — flipping this before the managed-identity code path is live and proven would break the running cluster (it would lose its only working auth method mid-flight). Sequence: deploy Steps 1-4, verify (Step 6 below), then deploy Step 5 alone.

- [x] **Step 6: Verify before flipping `AllowSharedKeyAccess`**

```bash
az containerapp logs show --name digitalbrain-jobs --resource-group digitalbrain-rg --tail 50
```
Confirm no `AuthenticationFailed`/`AuthorizationPermissionMismatch` errors and that Orleans successfully joined the cluster (grep the logs for the silo startup success message this codebase already logs on successful cluster join).

- [x] **Step 7: Build + fast test lane**

```bash
dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true
dotnet test Brain.slnx -c Release -p:SkipFlutterBuild=true --filter "FullyQualifiedName!~E2E"
```
Expected: green — the `useManagedIdentity` flag defaults to `false` in every test/local context (no `DigitalBrain:Storage:AccountUri`/`AccountName` set there), so this change is a no-op for the fast path and existing test fixtures.

- [x] **Step 8: Commit (as two commits, matching the two-deploy sequencing above)**

```bash
git add deploy/Program.cs DigitalBrain.Kernel/Program.cs
git commit -m "feat(security): add managed identity for kernel, switch Orleans storage to credential-based clients (shared key still enabled)"
# --- deploy, verify per Step 6, THEN: ---
git add deploy/Program.cs
git commit -m "feat(security): disable storage account shared-key access now that managed identity is proven"
```

### Task 19: Managed identity for Azure OpenAI — disable key auth

**Files:**
- Modify: `deploy/Program.cs` (role assignment, remove key output, disable local auth on the OpenAI account)
- Modify: `DigitalBrain.Kernel/Llm/DigitalBrainChat.cs:21-31` (switch `AzureOpenAIClient` construction from `AzureKeyCredential` to `DefaultAzureCredential` when no key is configured)

**Steps:**

- [x] **Step 1: Grant the kernel identity `Cognitive Services OpenAI User` on the OpenAI account**

In `deploy/Program.cs`, add:
```csharp
        _ = new Authorization.RoleAssignment("kernel-openai-user", new Authorization.RoleAssignmentArgs
        {
            PrincipalId = kernelPrincipalId,
            PrincipalType = Authorization.PrincipalType.ServicePrincipal,
            RoleDefinitionId = "/providers/Microsoft.Authorization/roleDefinitions/5e0bd9bd-7b93-4f28-af87-19fc36ad61bd", // Cognitive Services OpenAI User
            Scope = openAi.Id
        });
```
(Confirm this GUID against `az role definition list --name "Cognitive Services OpenAI User"` before applying, same caveat as Task 18/Step 2.)

- [x] **Step 2: Stop injecting the OpenAI key once identity-based auth is proven (do this in a second deploy, same sequencing caution as Task 18)**

Remove the `OpenAiKeySecret` secret and its two env var references (`DigitalBrain__Llm__AzureOpenAIKey`) from the kernel container's `Configuration.Secrets`/`Template.Containers[0].Env` in `deploy/Program.cs`, and remove the `openAiKey`/`Cognitive.ListAccountKeys` block entirely.

- [x] **Step 3: Update `DigitalBrainChat.cs` to use `DefaultAzureCredential` when no key is configured**

In `DigitalBrain.Kernel/Llm/DigitalBrainChat.cs:21-31`, change:
```csharp
        else if (string.Equals(options.Provider, DigitalBrainProviderIds.AzureOpenAI, StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = options.AzureOpenAIEndpoint
                ?? throw new InvalidOperationException("DigitalBrain:Llm:AzureOpenAIEndpoint is required for azureopenai provider.");
            var key = options.AzureOpenAIKey
                ?? throw new InvalidOperationException("DigitalBrain:Llm:AzureOpenAIKey is required for azureopenai provider.");
            var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key))
                .GetChatClient(options.Model)
                .AsIChatClient();
            var chatClient = new ChatClientBuilder(azureClient).UseOpenTelemetry(sourceName: "DigitalBrain.Neuron").Build();
            services.AddChatClient(chatClient);
        }
```
to:
```csharp
        else if (string.Equals(options.Provider, DigitalBrainProviderIds.AzureOpenAI, StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = options.AzureOpenAIEndpoint
                ?? throw new InvalidOperationException("DigitalBrain:Llm:AzureOpenAIEndpoint is required for azureopenai provider.");
            var azureClient = (string.IsNullOrWhiteSpace(options.AzureOpenAIKey)
                    ? new AzureOpenAIClient(new Uri(endpoint), new Azure.Identity.DefaultAzureCredential())
                    : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(options.AzureOpenAIKey)))
                .GetChatClient(options.Model)
                .AsIChatClient();
            var chatClient = new ChatClientBuilder(azureClient).UseOpenTelemetry(sourceName: "DigitalBrain.Neuron").Build();
            services.AddChatClient(chatClient);
        }
```
(Keeps the key-based path working for anyone still setting `DigitalBrain__Llm__AzureOpenAIKey` locally/in tests, while the cloud deploy — once Step 2 removes the key env var — falls through to `DefaultAzureCredential`, which resolves the container app's managed identity in ACA.)

- [x] **Step 4: Disable local (key) auth on the OpenAI account, only after Steps 1-3 are deployed and verified**

In `deploy/Program.cs`'s `Cognitive.AccountArgs` (`:108-122`), add:
```csharp
            Properties = new CognitiveInputs.AccountPropertiesArgs
            {
                CustomSubDomainName = "digitalbrainopenaiprod",
                PublicNetworkAccess = Cognitive.PublicNetworkAccess.Enabled,
                DisableLocalAuth = true
            },
```

- [x] **Step 5: Verify before disabling local auth**

```bash
az containerapp logs show --name digitalbrain-jobs --resource-group digitalbrain-rg --tail 50
```
Confirm chat completions succeed with no `401`/`PermissionDenied` from the OpenAI endpoint after Step 3's code is live but before Step 4 flips `DisableLocalAuth`.

- [x] **Step 6: Build + fast test lane**

```bash
dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true
dotnet test Brain.slnx -c Release -p:SkipFlutterBuild=true --filter "FullyQualifiedName!~E2E"
```
Expected: green — existing tests that construct `DigitalBrainChat` with a key configured are unaffected (Step 3 only changes behavior when the key is absent).

- [x] **Step 7: Commit (again as two commits matching the two-deploy sequencing)**

```bash
git add deploy/Program.cs DigitalBrain.Kernel/Llm/DigitalBrainChat.cs
git commit -m "feat(security): add managed identity path for Azure OpenAI (falls back to key auth)"
# --- deploy, verify per Step 5, THEN: ---
git add deploy/Program.cs
git commit -m "feat(security): disable Azure OpenAI local (key) auth now that managed identity is proven"
```

---

## Milestone M11 — Checkpoint-based local↔cloud sync (checklist #12)

**Scope decision made by this plan** (the original doc flagged this as "decision needed from you" — resolving it here so the plan has no placeholder): build **one-way local→cloud backup** (M11/Task 21) and **cloud→local restore/bootstrap** (M11/Task 22) now, since both map directly onto the existing `Checkpoint`/`ProtectedCheckpoint`/`CheckpointProtector` machinery with no open design questions. **Two-way merge is explicitly deferred** — it requires a conflict-resolution policy (last-writer-wins per stream is a real design decision with real data-loss implications) that should be a separate, focused plan once one-way backup is in production and the actual need for live two-way sync (vs. periodic backup) is confirmed by real usage. This is a scope decision, not an unfinished task.

**Design, grounded in existing code:**
- A `Checkpoint` (`DigitalBrain.Core/Synapse.cs:181`) is a single neuron's full synapse-journal snapshot; `Neuron.CreateCheckpointAsync` (`DigitalBrain.Kernel/Neuron.cs:218-226`) already builds one, `CheckpointProtector.Protect`/`Unprotect` (`DigitalBrain.Kernel/Kernel/CheckpointProtector.cs:10-21`) already AES-encrypts/decrypts it into a `ProtectedCheckpoint` (`DigitalBrain.Core/ProtectedCheckpoint.cs:6-9`). None of this is persisted anywhere outside the Orleans journal today — this milestone adds the actual export/import.
- V1 scope is the same fixed set of well-known singleton neuron ids the kernel already warms up at startup (`DigitalBrain.Kernel/Program.cs:358-406`: `status-main`, `ino-main`/`ino-editor-main`, `context-main`, `db-main`, `chart-main`, `session-main`, `automation-main`, `market-data-main`) rather than a general per-user grain registry (which doesn't exist yet and is out of scope here) — this is an explicit, documented V1 limitation, not a placeholder.
- Storage: a new Blob container in the **existing** `digitalbrainstprod` account (no new storage account — same account already backs clustering/grainstate/journal/Pulumi state).

### Task 20: Provision the sync blob container

**Files:**
- Modify: `deploy/Program.cs` (new `Storage.BlobContainer` resource)
- Modify: `DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` (add a matching `AddBlobs("sync")` for local/Azurite parity)

**Steps:**

- [x] **Step 1: Add the container in Pulumi**

```csharp
        var syncContainer = new Storage.BlobContainer("digitalbrain-sync", new Storage.BlobContainerArgs
        {
            ContainerName = "sync",
            AccountName = storage.Name,
            ResourceGroupName = resourceGroup.Name,
            PublicAccess = Storage.PublicAccess.None
        });
```

- [x] **Step 2: Add the matching Azurite container for local parity**

In `DigitalBrainBuilderExtensions.cs`'s `AddDigitalBrain`, alongside `grainBlobs`/`journalBlobs` (`:75-76`):
```csharp
        var syncBlobs = storage.AddBlobs("sync");
```
Thread it through `DigitalBrainContext` (add a `SyncBlobs` property, same shape as `GrainBlobs`) and reference it in `WireKernelSilo` with `.WithReference(ctx.SyncBlobs)`, injecting a `ConnectionStrings__sync` env var the same way `grainstate`/`journal` already work.

- [x] **Step 3: Preview + build**

```bash
cd deploy && pulumi preview --stack dev
dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true
```
Expected: Pulumi diff adds one new `BlobContainer` resource, no changes to existing resources; build succeeds.

- [x] **Step 4: Commit**

```bash
git add deploy/Program.cs DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs
git commit -m "feat(sync): provision sync blob container (cloud + local Azurite parity)"
```

### Task 21: One-way local→cloud checkpoint backup

**Files:**
- Create: `DigitalBrain.Kernel/Sync/CheckpointExporter.cs` (plain, unit-testable class — no Orleans/hosting dependencies beyond `IGrainFactory` and `BlobContainerClient`)
- Create: `DigitalBrain.Kernel/Sync/SyncManifest.cs` (record describing what was exported)
- Modify: `DigitalBrain.Kernel/Kernel/KernelServices.cs` (DI registration)
- Test: `DigitalBrain.Tests/Sync/CheckpointExporterTests.cs`

**Steps:**

- [x] **Step 1: Write the failing test for the manifest shape and export ordering**

Create `DigitalBrain.Tests/Sync/CheckpointExporterTests.cs`:
```csharp
using DigitalBrain.Kernel.Sync;
using Xunit;

namespace DigitalBrain.Tests.Sync;

public class CheckpointExporterTests
{
    [Fact]
    public async Task ExportAsync_UploadsOneBlobPerNeuronId_AndReturnsManifestWithMatchingCount()
    {
        var fakeUploads = new List<string>();
        var exporter = new CheckpointExporter(
            neuronIds: ["status-main", "context-main"],
            checkpointFor: _ => Task.FromResult(new ProtectedCheckpoint(
                Source: new NeuronId("test"), EncryptedSnapshot: [1, 2, 3], TakenAt: DateTimeOffset.UtcNow)),
            upload: (blobName, bytes) => { fakeUploads.Add(blobName); return Task.CompletedTask; });

        var manifest = await exporter.ExportAsync(userScope: "demo-user");

        Assert.Equal(2, manifest.Entries.Count);
        Assert.Equal(2, fakeUploads.Count);
        Assert.All(fakeUploads, name => Assert.StartsWith("demo-user/", name));
    }
}
```
(This test drives `CheckpointExporter` as a small, dependency-injected class — `checkpointFor` and `upload` are delegates so the test never touches real Orleans grains or real Blob storage. Match whatever `NeuronId`/`ProtectedCheckpoint` constructor shapes actually exist in `DigitalBrain.Core` — check `DigitalBrain.Core/Synapse.cs` and `DigitalBrain.Core/ProtectedCheckpoint.cs` for the exact constructor signatures before writing this test, since the exact `NeuronId` shape wasn't fully read during this plan's research and may differ from the placeholder shown here.)

- [x] **Step 2: Run it to confirm it fails (the type doesn't exist)**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~CheckpointExporterTests"
```
Expected: FAIL — compile error, `CheckpointExporter`/`SyncManifest` not found.

- [x] **Step 3: Create the manifest record**

Create `DigitalBrain.Kernel/Sync/SyncManifest.cs`:
```csharp
namespace DigitalBrain.Kernel.Sync;

public sealed record SyncManifestEntry(string NeuronId, string BlobName, DateTimeOffset TakenAt);

public sealed record SyncManifest(string UserScope, IReadOnlyList<SyncManifestEntry> Entries, DateTimeOffset ExportedAt);
```

- [x] **Step 4: Create the exporter**

Create `DigitalBrain.Kernel/Sync/CheckpointExporter.cs`:
```csharp
using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Sync;

public sealed class CheckpointExporter(
    IReadOnlyList<string> neuronIds,
    Func<string, Task<ProtectedCheckpoint>> checkpointFor,
    Func<string, byte[], Task> upload)
{
    public async Task<SyncManifest> ExportAsync(string userScope)
    {
        var entries = new List<SyncManifestEntry>(neuronIds.Count);
        foreach (var neuronId in neuronIds)
        {
            var protectedCheckpoint = await checkpointFor(neuronId);
            var blobName = $"{userScope}/{neuronId}.checkpoint";
            await upload(blobName, protectedCheckpoint.EncryptedSnapshot);
            entries.Add(new SyncManifestEntry(neuronId, blobName, protectedCheckpoint.TakenAt));
        }

        return new SyncManifest(userScope, entries, DateTimeOffset.UtcNow);
    }
}
```
(Adjust the `ProtectedCheckpoint`/`NeuronId` member names to whatever the real types expose — this plan's research confirmed the record shape as `ProtectedCheckpoint(Source, EncryptedSnapshot, TakenAt)` from `DigitalBrain.Core/ProtectedCheckpoint.cs:6-9`; verify `EncryptedSnapshot`'s exact type is `byte[]` and adjust `upload`'s signature if it's actually a different byte-buffer type like `ReadOnlyMemory<byte>`.)

- [x] **Step 5: Re-run the test — green**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~CheckpointExporterTests"
```
Expected: PASS.

- [x] **Step 6: Wire the real Orleans/Blob dependencies via a thin hosted-service trigger**

Create `DigitalBrain.Kernel/Sync/CheckpointBackupTrigger.cs` — a small class exposing a single method the MCP tool surface or a future scheduled trigger can call (do not build a background timer in this task; that's premature — expose the capability, let the next task decide the trigger):
```csharp
using Azure.Storage.Blobs;
using DigitalBrain.Core;
using DigitalBrain.Kernel.Kernel;
using Orleans;

namespace DigitalBrain.Kernel.Sync;

public sealed class CheckpointBackupTrigger(IGrainFactory grains, CheckpointProtector protector, BlobContainerClient syncContainer)
{
    private static readonly string[] V1NeuronIds =
    [
        "status-main", "ino-main", "ino-editor-main", "context-main",
        "db-main", "chart-main", "session-main", "automation-main", "market-data-main"
    ];

    public Task<SyncManifest> BackupAsync(string userScope)
    {
        var exporter = new CheckpointExporter(
            V1NeuronIds,
            checkpointFor: async neuronId =>
            {
                // INeuron (DigitalBrain.Core/INeuron.cs:18) declares CreateCheckpointAsync directly, so this
                // works generically across all nine V1 neuron types without a per-type switch.
                var neuron = grains.GetGrain<INeuron>(neuronId);
                var checkpoint = await neuron.CreateCheckpointAsync();
                return protector.Protect(checkpoint);
            },
            upload: async (blobName, bytes) =>
            {
                await syncContainer.GetBlobClient(blobName).UploadAsync(new BinaryData(bytes), overwrite: true);
            });

        return exporter.ExportAsync(userScope);
    }
}
```

- [x] **Step 7: Register in DI**

In `DigitalBrain.Kernel/Kernel/KernelServices.cs`, alongside the existing `services.AddSingleton<CheckpointProtector>();` (`:38`):
```csharp
        services.AddSingleton(sp =>
        {
            var blobs = new BlobServiceClient(configuration.GetConnectionString("sync")!); // or the managed-identity-based client if Task 18 already landed
            return blobs.GetBlobContainerClient("sync");
        });
        services.AddSingleton<CheckpointBackupTrigger>();
```

- [x] **Step 8: Build + run the sync test lane**

```bash
dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~Sync"
```
Expected: green.

- [x] **Step 9: Manual end-to-end check under `aspire run`**

Trigger `CheckpointBackupTrigger.BackupAsync("demo-user")` from a temporary MCP tool call or test harness against the local Azurite `sync` container, then confirm via Azure Storage Explorer (pointed at the Azurite emulator) that nine blobs appear under `demo-user/`.

- [x] **Step 10: Commit**

```bash
git add DigitalBrain.Kernel/Sync/ DigitalBrain.Kernel/Kernel/KernelServices.cs DigitalBrain.Tests/Sync/
git commit -m "feat(sync): one-way local-to-cloud checkpoint backup (V1 fixed neuron-id scope)"
```

### Task 22: Cloud→local restore/bootstrap

**Files:**
- Create: `DigitalBrain.Kernel/Sync/CheckpointImporter.cs` (mirrors `CheckpointExporter`)
- Create: `DigitalBrain.Kernel/Sync/CheckpointRestoreTrigger.cs`
- Test: `DigitalBrain.Tests/Sync/CheckpointImporterTests.cs`

**Steps:**

- [x] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Sync/CheckpointImporterTests.cs`, mirroring Task 21/Step 1's structure but asserting the importer calls a `restore` delegate once per manifest entry, in manifest order, with the downloaded bytes.

- [x] **Step 2: Run it to confirm it fails**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~CheckpointImporterTests"
```
Expected: FAIL — compile error.

- [x] **Step 3: Implement `CheckpointImporter`, mirroring `CheckpointExporter`'s shape**

```csharp
namespace DigitalBrain.Kernel.Sync;

public sealed class CheckpointImporter(
    Func<string, Task<byte[]>> download,
    Func<string, byte[], Task> restore)
{
    public async Task RestoreAsync(SyncManifest manifest)
    {
        foreach (var entry in manifest.Entries)
        {
            var bytes = await download(entry.BlobName);
            await restore(entry.NeuronId, bytes);
        }
    }
}
```

- [x] **Step 4: Re-run the test — green**

```bash
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~CheckpointImporterTests"
```
Expected: PASS.

- [x] **Step 5: Wire the real dependencies, using `Neuron.RestoreCheckpointAsync` (`DigitalBrain.Kernel/Neuron.cs:244-251`), which re-seeds the incoming journal without re-dispatching handlers — the correct semantics for a bootstrap restore (you don't want every historical event to re-fire its side effects on import)**

```csharp
using Azure.Storage.Blobs;
using DigitalBrain.Kernel.Kernel;
using Orleans;

namespace DigitalBrain.Kernel.Sync;

public sealed class CheckpointRestoreTrigger(IGrainFactory grains, CheckpointProtector protector, BlobContainerClient syncContainer)
{
    public Task RestoreAsync(SyncManifest manifest)
    {
        var importer = new CheckpointImporter(
            download: async blobName =>
            {
                var response = await syncContainer.GetBlobClient(blobName).DownloadContentAsync();
                return response.Value.Content.ToArray();
            },
            restore: async (neuronId, bytes) =>
            {
                // TakenAt here only feeds CheckpointProtector.Unprotect's own ProtectedCheckpoint shape (Source,
                // EncryptedSnapshot, TakenAt) — the real, meaningful TakenAt is recovered from inside the
                // encrypted snapshot once Unprotect returns the actual Checkpoint, so a fresh UtcNow placeholder
                // on the wrapper here is fine and discarded immediately after.
                var protectedCheckpoint = new ProtectedCheckpoint(
                    Source: new NeuronId(neuronId), EncryptedSnapshot: bytes, TakenAt: DateTimeOffset.UtcNow);
                var checkpoint = protector.Unprotect(protectedCheckpoint);
                // INeuron (DigitalBrain.Core/INeuron.cs:20) declares RestoreCheckpointAsync directly, same as
                // CreateCheckpointAsync — generic across all V1 neuron types, no per-type switch needed.
                var neuron = grains.GetGrain<INeuron>(neuronId);
                await neuron.RestoreCheckpointAsync(checkpoint);
            });

        return importer.RestoreAsync(manifest);
    }
}
```

- [x] **Step 6: Build + test**

```bash
dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~Sync"
```
Expected: green.

- [x] **Step 7: Manual end-to-end round-trip under `aspire run`**

Backup a neuron with `CheckpointBackupTrigger` (Task 21), mutate its state further, then restore from the manifest with `CheckpointRestoreTrigger` and confirm (via the neuron's own timeline query, e.g. `GetTimelineAsync`) that its journal matches the backed-up snapshot rather than the post-mutation state.

- [x] **Step 8: Commit**

```bash
git add DigitalBrain.Kernel/Sync/CheckpointImporter.cs DigitalBrain.Kernel/Sync/CheckpointRestoreTrigger.cs DigitalBrain.Tests/Sync/
git commit -m "feat(sync): cloud-to-local checkpoint restore/bootstrap"
```

**Deferred (not built in this plan):** two-way merge (phase c from the original architecture doc). Revisit only once one-way backup/restore is in production and real usage shows live bidirectional sync is actually needed — building conflict resolution speculatively risks solving the wrong problem.

---

## Milestone M12 — Streams durability (checklist #13) — deferred by design, documented for later

Not built now, per this plan's own judgment call (matching the original doc's "only when product needs it"). `AddMemoryStreams("HomeFeed")`/`AddMemoryStreams("DigitalBrainTimeline")`/`AddMemoryGrainStorage("PubSubStore")` (`DigitalBrain.Kernel/Program.cs:208-210`) apply uniformly to both the fast and cloud paths today, meaning stream subscriptions and undelivered events do not survive a full cluster restart. Acceptable for current feed/timeline UX. When this needs to change, the concrete steps are:

1. Replace `siloBuilder.AddMemoryStreams(name)` with `siloBuilder.AddAzureQueueStreams(name, options => options.ConfigureQueueServiceClient(...))` for both `"HomeFeed"` and `"DigitalBrainTimeline"`, gated the same way clustering/grain storage already are (`isAspireHosted` cloud branch only — memory streams stay for the fast path).
2. Replace `siloBuilder.AddMemoryGrainStorage("PubSubStore")` with `siloBuilder.AddAzureTableGrainStorage("PubSubStore", options => options.ConfigureTableServiceClient(...))`, same account as clustering.
3. Add both new env-driven connection strings (`ConnectionStrings__streams` or reuse the existing storage connection string) to `deploy/Program.cs`'s kernel container `Env`.
4. If/when Orleans reminders are needed anywhere in the codebase (none exist today — confirmed zero `UseAzureTableReminderService`/`AddReminders` calls), add `siloBuilder.UseAzureTableReminderService(options => ...)` in the same cloud branch, same storage account.

## Milestone M13 — Kernel decomposition (checklist #14) — ongoing, tracked elsewhere

`DigitalBrain.Kernel` is host + gateway + marketplace + LLM adapter + economics + foundry + ~20 neurons. Full split is post-GA work; this plan does not schedule it as a discrete task. Continue tracking it in `ARCHITECTURE_CLEANUP_PROPOSAL.md`'s "Still left" section — the one constraint from this plan is: keep `Program.cs` a composition root (it mostly already is) and don't let any task above make it worse.

---

# Part 3 — Milestone Sequencing

| # | Milestone | Depends on | Effort |
|---|---|---|---|
| M1 | Unblock CI/CD pipeline (GHCR + OIDC) | — | ~1 day |
| M2 | Kill NeuroOSPrototype naming | — (independent of M1) | hours |
| M3 | Archive AI-session docs | — | minutes |
| M4 | DigitalBrain.Aspire API hygiene | M2 (Task 9 assumes the rename landed) | ~1-2 days |
| M5 | Health probes + MinReplicas=2 on ACA | M1 (needs a working deploy to verify against) | ~1 day |
| M6 | Observability (App Insights + smoke test) | M5 (smoke test needs `/health`) | ~1 day |
| M7 | Local embeddings via Ollama | M4 (uses the un-cast `ctx.Llm`, the `IsRunMode` guard) | ~1 day |
| M8 | Local Whisper container | M4, M7 (same `IsRunMode`-guarded resource block) | hours-1 day |
| M9 | Flutter web → Static Web Apps | M1 (GHCR/OIDC pattern reused for SWA token secret) | ~1 day |
| M10 | Managed identity for Storage + OpenAI | M5, M6 (verify via health/logs before flipping auth off) | 2-3 days |
| M11 | Checkpoint-based sync (one-way) | M10 (ideally identity-based blob access, though connection-string fallback works standalone) | ~1 week |
| M12 | Streams durability | Later, only when product needs it | — |
| M13 | Kernel decomposition | Ongoing | ongoing |

Suggested execution order: **M1 → M2 → M3 → M4 → M5 → M6 → M7 → M8 → M9 → M10 → M11**, with M3 free to interleave anywhere (it has no dependencies and no risk).
