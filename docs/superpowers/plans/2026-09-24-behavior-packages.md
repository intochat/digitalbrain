# Behavior Packages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Share behaviors as git-like packages that install into a workspace in one step.

**Architecture:** `IPackage` neurons hold content-addressed revisions gated by the Coding check;
`IPackageDirectory` indexes published packages; `IApp` installs one revision into a workspace and
drives `IBehaviorProgram`. The toy composition runtime is deleted first to free the names.

**Tech Stack:** .NET 11 RC1, Orleans, Aspire 13 testing, xUnit v3 on MTP.

**Spec:** `docs/superpowers/specs/2026-09-24-behavior-packages-design.md`

## Global Constraints

- One file per class, record, enum and interface.
- No `/// <summary>` boilerplate; short inline comments only where the code cannot say it.
- Serializer aliases prefixed `apps.`; field ids stable.
- Owner regex `^[a-z0-9][a-z0-9-]*$` (≤80), name regex `^[a-z0-9][a-z0-9-]{0,63}$`.
- Errors: `ArgumentException` 400, `UnauthorizedAccessException` 403, `KeyNotFoundException` 404,
  `InvalidOperationException` 409.

## Review Focus

- Retried commands (same operation id) return the original result instead of a duplicate revision.
- A diverged proposal is refused with an actionable message, never silently merged.
- A non-owner cannot commit, publish, pull into or accept on someone else's package.
- An invocation issued before the worker subscribed is still answered (`Pending` drain).
- Unknown or oversized settings are rejected before a deployment is attempted.

---

### Task 1: Delete the composition runtime

**Files:** delete `Apps/Contracts/{Composition,Lifecycle}/**`, `Apps/Contracts/Workspace/**`,
`Apps/Apps/{Composition,Lifecycle,Workspace}/**`, `Apps/Tests/Unit/Workspace/WorkspaceAppFacts.cs`,
`IntoChat/Apps/AppRuntimeEndpoints.cs`, `Tests/E2E/Apps/MarketplaceAppJourneyFacts.cs`,
Flutter `app_studio.dart` and its test; remove `AppManifest.Composition`; replace Kernel
`App<TState>` with `Neuron<TState>` in built-in apps; move assistant activation to
`POST /workspaces/{workspaceId}/built-in/activate`.

- [ ] Delete, fix references, build `DigitalBrain.slnx`.
- [ ] Run Apps, Marketplace, IntoChat unit suites; commit.

### Task 2: Package contracts and commit

**Interfaces produced:** `PackageId(Owner, Name)`, `PackageManifest`, `PackageOperation`,
`PackageSetting`, `PackageContent(Manifest, Source, Tests, ModuleIds)`,
`PackageRevision(Id, Parents, Content, Artifact, Author, Message, CommittedAt)`,
`PackageRevisionRef(Package, Revision)`, `PackageSnapshot`, `CommitPackage`,
`IPackage.Read/ReadRevision/Commit`, signal `PackageChanged`.

- [ ] Failing unit tests (`Tests/Unit/Packages/PackageCommitFacts.cs`) with a fake
  `ICodeArtifactStore`: owner commits; artifact source mismatch rejected; stale head rejected;
  non-owner rejected; retry idempotent.
- [ ] Implement `PackageNeuron`, `PackageRevisionHash`, `PackageRules`; tests pass; commit.

### Task 3: Lineage — fork, pull, merge, propose, accept

- [ ] Failing tests (`PackageLineageFacts.cs`): fork copies lineage and records origin; pull
  fast-forwards and refuses divergence; merge commit has two parents; propose + accept
  fast-forwards upstream; diverged proposal refused until merged; only owners accept.
- [ ] Implement; tests pass; commit.

### Task 4: Publish and directory

- [ ] Failing tests (`PackageDirectoryFacts.cs`): publish lists the package with its stable
  revision and fork origin; republish updates; unknown revision refused.
- [ ] Implement `PackageDirectoryNeuron`; commit.

### Task 5: Installed app

**Interfaces produced:** `IApp.Read/Install/Configure/Upgrade/Uninstall/Invoke/Respond/ReadInvocation/Pending`,
`AppSnapshot`, `AppInvocation`, `AppInvoked`, `AppChanged`.

- [ ] Failing tests (`Tests/Unit/Workspace/AppFacts.cs`) with a recording `behavior.program` grain:
  install deploys artifact with `Behavior__App`/`Behavior__{setting}`; defaults fill; unknown
  setting rejected; configure redeploys; upgrade refuses another package; invoke publishes and
  respond completes; pending drains; uninstall deletes the program.
- [ ] Implement `App`; commit.

### Task 6: Apps E2E with a real worker

- [ ] New project `Apps/Tests/E2E`; journey test compiling a real package behavior; commit.

### Task 7: IntoChat routes and HTTP E2E

- [ ] `PackageService`, `PackageEndpoints`, unit facts for key derivation; E2E with two accounts.

### Task 8: Flutter packages screen, README, aspire run, review

- [ ] Replace App Studio with a Packages screen (browse, install, invoke); README; aspire run;
  full unit + E2E; code review.
