# Brain as a Neuron: dissolving the workspace concept

Status: ratified in conversation 2026-09-30 (owner: Vladyslav). Supersedes the workspace scoping
model. Branch base: `refactor/csharp-module` (PR #111).

## Problem

"Workspace" is three unrelated things sharing one name:

1. A **tenancy key** — `WorkspaceScope` hashes `(owner, workspaceId)` into `workspace-<sha256>`,
   which keys nearly every grain (apps, C# files, connectors, compute, research rows).
2. A **UI neuron** — `IWorkspace` in the Flutter module: window arrangement, revisions.
3. **Route plumbing** — `/workspaces/{workspaceId}/...` on every endpoint, with
   `WorkspaceAccessFilter` applied per endpoint group and `WorkspaceScope.Current(auth, ws)`
   re-derived at ~45 call sites, each dragging `IOptions<BasicAuthOptions>` into its signature.

The tenancy key is ambient — a string parameter threaded manually through routes, DI factories,
and grain-key concatenation — instead of an addressable identity. Every module re-pays that tax,
which is why endpoints piled up in the IntoChat host: only the host knew how to resolve scope.
Under the four-word rule (neuron, signal, behavior, connector), a non-neuron ambient scope is the
fifth concept, and it shows.

## Decision

A user has one or more **brains**. The brain is a **neuron** — the addressable root of everything
installed in it — and the identity directory maps principals to brains. The word "workspace"
survives only in the Flutter shell's window-layout domain (`IWorkspace`, `WorkspaceStore`), which
is a genuinely different concept.

### The contract

```csharp
// Kernel contracts (DigitalBrain.Contracts)
[Alias("brain"), DefaultGrainType("brain")]
public interface IBrain : INeuron
{
    Task<BrainSnapshot> Read();          // name, owner account, created, member roster summary
    // Growth points (not in v1): Fork, brain-level signals (BrainCreated, BrainShared)
}
```

- **Key derivation is preserved**: the brain grain key remains
  `"workspace-" + sha256hex(owner + "\0" + name)` (now produced by `BrainScope.Create`). Existing
  grain state, blob paths, and Postgres rows survive without migration. The `workspace-` string
  prefix inside the hash id is an opaque storage artifact; renaming it would force a data
  migration for zero benefit. Revisit only alongside a real storage migration.
- **Registry per brain.** The connectors registry (`IConnectors`) and the neuron registry are
  keyed by brain id. Each brain has its own catalog of active neurons; "different brains have
  different registries" holds by construction.

### Identity carries the brain

- `CallerContext.WorkspaceId` → **`BrainId`**. `AccountSession` middleware keeps stamping it on
  the Orleans RequestContext from the route value / session claim — one resolution point.
- `Member` record: `WorkspaceId` property renamed `BrainId` — **`[Id(n)]` slots unchanged**
  (persisted state; rename the C# name only).
- `IIdentityDirectory`: `CanAccessAsync(principal, brainId)`, `ShareBrainAsync(...)` (renamed from
  `ShareWorkspaceAsync`). Registration creates the member's first brain and activates its `IBrain`
  neuron. `POST /identity/workspaces` → `POST /identity/brains`.
- Claim names: `intochat.workspace` claim → `intochat.brain` (sessions are short-lived cookies;
  old sessions just re-login).

### One scope seam, no ambient auth

```csharp
// Kernel (DigitalBrain.Core.Enforcement)
public static class BrainScope   // replaces WorkspaceScope
{
    public static string CurrentId();          // from stamped CallerContext; throws if unstamped
    public static string Create(string owner, string name); // the preserved sha derivation
    public static bool IsValidId(string? value);
}
```

- Every `WorkspaceScope.Current(auth.Value, workspaceId)` call site becomes `BrainScope.CurrentId()`.
- `IOptions<BasicAuthOptions>` disappears from all endpoint lambdas and services
  (PackageService, CSharpSharing, FilesEndpoints, …). `BasicAuthOptions` remains only inside
  `AccountSession` where the credential actually lives.

### Enforcement applied once

- Kernel truth: the existing `GrantCallFilterStage` / `ICallFilterStage` pipeline, unchanged.
- HTTP edge: `MapDigitalBrainModules` maps modules inside a `/brains/{brainId}` route group that
  carries the membership filter (today's `WorkspaceAccessFilter.EnforceAsync`, renamed
  `BrainAccessFilter`) **once**. The ~30 per-endpoint `AddEndpointFilter` calls are deleted.
- `BrainAccessFilter.Decide` survives for the one body-carried case (`POST /agent`).

### Routes rename now

- `/workspaces/{workspaceId}/...` → **`/brains/{brainId}/...`** in the same change, C# and Dart
  together (repo rule: wire-shape changes mirror into the Dart screens in the same change).
- Route value name `workspaceId` → `brainId` everywhere (AccountSession reads it; filters read it).
- Flutter shell: `DigitalBrainUiClient` paths, `defaultWorkspaceId` → `defaultBrainId`. The
  shell's user-facing word for a brain in the switcher UI is a product copy decision, not code.

## What gets deleted

- `WorkspaceScope.Current` and every `IOptions<BasicAuthOptions>` endpoint/service parameter that
  fed it (~45 lambdas across FilesEndpoints, WorkspaceEndpoints, WorkspaceConnectionsEndpoints,
  AppEndpoints, AgentEndpoints, ComputeUsageEndpoints, VoiceEndpoints, BuiltInAppEndpoints,
  ApplicationEndpoints, PackageService, CSharpSharing, CSharpEndpoints).
- ~30 per-endpoint `AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync)` calls.
- `OperationsEndpoints.cs` + `ProblemReport` (dead — never mapped).
- `BrowserLoginWorkspace` re-wrapping in connections endpoints.

## What moves (unblocked by this design)

Removing the app-local scope resolution is what frees endpoints to leave the host. Each endpoint
file is refactored to `BrainScope` **and moved to its module in the same pass**:

| Host file | Destination module |
|---|---|
| CSharp/CSharpEndpoints.cs (+ `/csharp/{id}/share`, CSharpSharing) | Microsoft/CSharp |
| Agent/ComputeUsageEndpoints.cs + `/compute/limits` | Compute |
| Apps/AppEndpoints.cs, Apps/BuiltInAppEndpoints.cs | Apps |
| Packages/PackageEndpoints.cs + PackageService + DTOs | Registry/Apps |
| Agent/AgentEndpoints.cs, Agent/VoiceEndpoints.cs | AI |
| Applications/ApplicationEndpoints.cs | Assistant / CustomerResearcher |
| Workspace/WorkspaceEndpoints.cs, WorkspaceConnectionsEndpoints.cs | Flutter (UI windows) / Connector |

## What stays untouched

- `IWorkspace` window-arrangement neuron, `WorkspaceStore`, shell persistence — UI domain keeps
  its name.
- Grant pipeline, cookie session issuance, `IIdentityDirectory` semantics.
- All stored grain state and derived keys (derivation preserved).

## Renames (full, now)

| Old | New |
|---|---|
| `WorkspaceScope` | `BrainScope` |
| `CallerContext.WorkspaceId` | `CallerContext.BrainId` |
| `WorkspaceAccessFilter` | `BrainAccessFilter` |
| `IWorkspaceAccess` / `DirectoryWorkspaceAccess` | `IBrainAccess` / `DirectoryBrainAccess` |
| `Member.WorkspaceId` | `Member.BrainId` (same `[Id]`) |
| `ShareWorkspaceAsync` | `ShareBrainAsync` |
| route `/workspaces/{workspaceId}` | `/brains/{brainId}` |
| claim `intochat.workspace` | `intochat.brain` |
| `SessionView.WorkspaceId` | `SessionView.BrainId` (Dart mirror) |

Flutter-module types named Workspace* (window layout) are exempt.

## Testing

- Identity unit facts extend to: registering creates a brain neuron; `CanAccessAsync` honors
  membership; sharing a brain grants access; brain ids derived stably.
- Kernel fact: an unstamped call to `BrainScope.CurrentId()` throws; a stamped call resolves.
- Route facts: module endpoints under `/brains/{brainId}` reject non-members (403) and unstamped
  callers (401) via the single group filter.
- Existing E2E suites keep passing with `BrainScope.Create("owner", ...)` (derivation unchanged);
  renames are mechanical.
- Bar per repo rules: affected unit suites + `aspire run` all-Healthy; skip the 18-minute E2E.

## Sequencing

1. Kernel + Identity: `IBrain`, `BrainScope`, CallerContext rename, directory rename, route group
   + single filter in `MapDigitalBrainModules`, `/identity/brains`.
2. Module-owned endpoint files in place (Files, Flutter UiHttp): drop auth params, adopt
   `BrainScope`, route prefix.
3. Host endpoint files one at a time: refactor to `BrainScope` **and** move to destination module
   in the same pass (CSharp first).
4. Dart shell route/name mirror alongside each wire change.
5. Delete dead code (`OperationsEndpoints`), final sweep for `WorkspaceScope`/`workspaceId`
   stragglers.

## Out of scope

- Brain forking/sharing UX, brain-level signals, cross-brain subscription permissions (open
  design front in CLAUDE.md).
- Renaming the stored `workspace-` hash prefix (needs a storage migration; explicitly deferred).
- The Aspire 13.6 migration (separate workplan item).
