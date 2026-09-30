# Refactoring & Improvement Workplan — 2026-09-30

Scope: PR #111 (`refactor/csharp-module` → master), IntoChat host cleanup, Aspire 13.5.3 → 13.6 migration.
Ordering rationale: land structural refactorings on the PR branch first (they touch the same files the PR already changes), then the Aspire bump (mechanical, isolated), then leverage the new Aspire features.

---

## Priority 1 — IntoChat host → module endpoint refactoring

The host defines endpoints that belong in modules. The module mechanism already exists:
`IModule.Configure(IEndpointRouteBuilder)` (Kernel `IModule.cs:6-11`), wired by
`MapDigitalBrainModules()` in `Program.cs:50`. Gmail, Identity, Files, Secrets, Connector all
use it. **CSharpModule already implements `Configure(IEndpointRouteBuilder)`** (`CSharpModule.cs:50`)
— the natural home for `MapCSharp`.

### 1.1 Move CSharp endpoints into the CSharp module (the one you flagged)
- Move `src/Applications/IntoChat/IntoChat/CSharp/CSharpEndpoints.cs` routes
  (`/workspaces/{ws}/csharp` group: list/get/put/start/stop/delete) into
  `CSharpModule.Configure(IEndpointRouteBuilder)`.
- Move the `ScopedCSharpTools` scoped DI factory from `AddCSharp` into
  `CSharpModule.Configure(ISiloBuilder)` (module services register via `silo.Services`).
- Move the C#-specific `POST /workspaces/{ws}/csharp/{id}/share` route + `CSharpSharing.cs` +
  `ShareCSharpRequest.cs` out of `Packages/` into the module too.
- **Blockers to solve first:**
  - `WorkspaceAccessFilter` / `WorkspaceScope` are app-local (`IntoChat.Workspace`). They must
    move to a shared module/SDK (likely `DigitalBrain.Sdk` next to `IHttpSurface`) or be exposed
    as a pluggable endpoint-filter service the app registers.
  - Developer-mode gating uses app config key `IntoChat:DeveloperMode` via `AgentToolPolicy`.
    Decide: module-owned config key, or an injected policy interface the app configures.

### 1.2 Sweep the remaining app-level endpoint files (same pattern, one per step)
Roughly in order of clean fit:
| File | Routes | Target module |
|---|---|---|
| `Agent/ComputeUsageEndpoints.cs` + `/compute/limits` in Program.cs | compute summary/usage/limits | Compute |
| `Apps/AppEndpoints.cs`, `Apps/BuiltInAppEndpoints.cs` | workspace apps, consent, built-in activate/open | Apps |
| `Packages/PackageEndpoints.cs` + `PackageService` + DTOs | registry, drafts, verify/publish, install | Registry/Apps |
| `Agent/AgentEndpoints.cs`, `Agent/VoiceEndpoints.cs` | /ai/models, /agent, conversations, voice | AI |
| `Applications/ApplicationEndpoints.cs` | customer-researcher/assistant open/start | Assistant / CustomerResearcher modules |
| `Workspace/*Endpoints.cs` | workspace data + connections | probably stays in app (host-owned identity surface) — decide |

### 1.3 Move Marketplace domain logic out of the host (bigger, optional later)
`Marketplace/` (MarketplaceService, AppDrafts, BuilderTools, AgentPrompts, ShippedAppPublisher)
is agent/app-builder domain logic living in the host. Candidate for an Apps/Marketplace module.
Larger surface; do after 1.1–1.2 prove the pattern.

### 1.4 Dead code
- `Operations/OperationsEndpoints.cs` (`MapOperationsEndpoints`, `POST /workspaces/{ws}/reports`)
  is never called from Program.cs. Delete or wire it deliberately.

---

## Priority 2 — Aspire 13.5.3 → 13.6.0 migration

Current: `Directory.Packages.props` pins `AspireVersion` 13.5.3 (Hosting, PostgreSQL, Testing,
Azure.Storage, Orleans, Qdrant, ClickHouse, Azure Tables/Blobs, plus per-RID Orchestration/Dashboard
SDK pins at L81-90); AppHost SDK `Aspire.AppHost.Sdk/13.5.3`; CommunityToolkit Ollama 13.5.1-beta.

### 2.1 Mechanical bump
- `AspireVersion` → 13.6.0 in Directory.Packages.props; AppHost SDK → `Aspire.AppHost.Sdk/13.6.0`;
  `aspire` CLI update; check CommunityToolkit.Aspire.Hosting.Ollama for a 13.6-compatible build
  (it lags — may need to stay on the beta).
- Verify the per-RID `Aspire.Hosting.Orchestration.*` / `Aspire.Dashboard.Sdk.*` pins still resolve
  at 13.6.0 (they exist for the AppHost-less packages).
- Breaking changes that touch us: **none obviously** — we don't use MongoDB, Cosmos, Front Door,
  or the terminal APIs yet. Watch for: connection-string portable aliases (hyphenated resource
  names now also emit `ConnectionStrings__underscore_alias`) — audit resource names in
  `ProductSurfaceResources.cs` for hyphens.
- Gate: build, unit suites, `aspire run` from `src/Applications/IntoChat/AppHost` all Healthy.

### 2.2 Leverage new 13.6 features (each its own small change)
- **Dashboard persistence / run history** — free once bumped; SQLite-backed, 10 runs retained,
  100k log/trace/metric limits. Nothing to do but confirm.
- **`WithRepl()`** on PostgreSQL (and Redis if/when added) — interactive DB client in the
  dashboard. One-liner on the postgres resource; high dev-loop value.
- **`aspire init --file-based` / `apphost.cs`** — not for us (we have a full AppHost), but the
  CLI-bundle flag `AspireUseCliBundle` is already set; confirm still supported.
- **`aspire run --launch-profile`** — document in CLAUDE.md if useful for the desktop-app profile.
- **Dashboard-enabled integration tests** (`EnableDashboard`, `GetDashboardUrlAsync()`) via
  Aspire.Hosting.Testing — could improve debugging of the AppHost-less test packages.
- **AppHost terminals** (experimental, now in `Aspire.Hosting.ApplicationModel`) — potential fit
  for surfacing the C# sandbox script output as a docked terminal. Exploratory.
- **`AddDotnetProject` coordinated builds / `WithBuildEnvironment()`** — check whether the
  IntoChat + digitalbrain-mcp projects benefit from the shared restore/build groups.
- Not relevant now: Java/Rust/Deno hosting, Azure Sandboxes/Connector Namespace, Dev Tunnels,
  Foundry toolbox, GitHub Models removal (we don't use it).

---

## Priority 3 — PR #111 hygiene

- The PR carries ~100 docs files (plans/research/specs, incl. `.review-round1.tmp.md` and
  `continuation.md`). Decide what belongs in master; delete temp files from the branch.
- Run the PR's own test plan (unit suites for CSharp, Marketplace, IntoChat; `aspire run`;
  shipped-package install smoke; publish-gate refusal check) before merge.
- Code review pass over the 217 commits' final diff (naming, no boilerplate summaries, grain
  state `[Id(n)]` discipline).

---

## Suggested execution order

1. **1.1** CSharp endpoints → module (incl. WorkspaceAccessFilter relocation decision) — on the PR branch.
2. **1.4** delete dead OperationsEndpoints.
3. **1.2** remaining endpoint sweeps, one module at a time (Compute → Apps → Packages → AI → Applications).
4. **2.1** Aspire 13.6 bump + green gate.
5. **2.2** WithRepl + any features you pick.
6. **3** PR hygiene + review, then merge.
7. **1.3** Marketplace module extraction as a follow-up branch.
