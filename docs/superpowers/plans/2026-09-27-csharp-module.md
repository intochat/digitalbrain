# CSharp Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Behavior/Synapse with `Microsoft/CSharp`: an `ICSharpFile` neuron whose source string lives in grain state and runs as `dotnet run app.cs` in a .NET SDK container using `DigitalBrainClient.ConnectAsync(args)`.

**Architecture:** Kernel client gains a static connect + `On<T>` + `Setting`. New module owns the neuron, a docker runner over the shared `IProcessRunner`, and the contract catalog (moved from Coding). Apps/IntoChat/Flutter are ported; Behaviors module and Coding's draft/validation/artifact half are deleted.

**Tech Stack:** .NET 11 rc.1, Orleans, Aspire, Docker CLI, xUnit v3 (MTP), Flutter.

**Spec:** `docs/superpowers/specs/2026-09-27-csharp-module-design.md`

## Global Constraints

* Branch `refactor/csharp-module`; never merge.
* Image `mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1`; container args `-p:ArtifactsPath=/work/artifacts`; repo mounted at `/brain:ro`.
* Contracts assembly `DigitalBrain.Modules.Microsoft.CSharp.Contracts`, namespace `DigitalBrain.Microsoft.CSharp`, grain alias/type `microsoft.csharp.file`.
* No meaningless `/// <summary>`; self-explanatory names; small inline comments only for exceptional cases.
* Single process runner: `DigitalBrain.Microsoft.DotNet.IProcessRunner` (HygieneFacts).
* Words `Behavior`/`Synapse` disappear from product code, routes, config keys, tool names (generic `BehaviorRun` test helper in Testing is unrelated and stays).
* Build per project (slnx `dotnet test` handshake bug on Windows); run tests with `--minimum-expected-tests`/high verbosity off; finally `aspire run` + IntoChat E2E green.

## Review Focus

1. Script finishes normally → status `Exited(0)`, not restarted (only `on-failure`).
2. `Start` while running → old container removed, new one started (no name conflict).
3. `Delete` on a never-started file → no docker error surfaces.
4. Settings with odd names (`Account`, `App`) rejected by `PackageRules`; values with spaces/`=` passed intact as separate `-e` args.
5. Gateway host rewrite: silo advertised `127.0.0.1`/`localhost` → `host.docker.internal`; client resolves host names to IPs before static clustering.

---

### Task 1: Kernel client connect

**Files:** Create `Kernel/Client/DigitalBrainClient.cs`, `Kernel/Client/DigitalBrainConnection.cs`, `Kernel/Client/BrainScriptExtensions.cs`; test `Kernel/Tests/Unit/Client/DigitalBrainClientFacts.cs`.

**Produces:** `DigitalBrainClient.ConnectAsync(string[] args, CancellationToken ct = default) : Task<DigitalBrainConnection>`; `DigitalBrainConnection : IDigitalBrain, IAsyncDisposable { IConfiguration Configuration }`; `IDigitalBrain.On<T>(INeuron, CancellationToken) : IAsyncEnumerable<T>`; `DigitalBrainConnection.Setting(string) : string?` reading `CSharpFile:Settings:<name>`; `DigitalBrainClient.GatewayEndpoints(string gateways) : IReadOnlyList<Uri>` (host names resolved to IPs).

- [ ] Facts: gateway parsing resolves `localhost` to an IP URI, keeps IP URIs, rejects empty without `LocalDevelopment`; `Setting` reads `CSharpFile__Settings__X` env mapping.
- [ ] Implement by moving the connection code out of `BehaviorApp` (serializer registration of entry assembly + references, activity propagation).
- [ ] Build Client + run Kernel unit tests; commit.

### Task 2: CSharp module (contracts, neuron, runner, catalog, hosting)

**Files:** Create `src/Modules/Microsoft/CSharp/{Contracts,CSharp,Aspire.Hosting,Tests/Unit,Tests/E2E,Samples}`; add to `DigitalBrain.slnx`.

**Produces:** `ICSharpFile`, `CSharpFileSnapshot`, `CSharpFileStatus`, `CSharpFileChanged` (spec); `CSharpModule : IModule`; `CSharpOptions { Root, SourceRoot, Image, Gateways, ClusterId, ServiceId }` section `DigitalBrain:CSharp`; `ICSharpRunner { StartAsync(CSharpRun), StopAsync(id), InspectAsync(id) : CSharpContainerState, LogsAsync(id, tail) }`; `DockerCSharpRunner`; `CSharpContractCatalog.Read(IReadOnlyList<string> modules) : CSharpContractCatalogSnapshot(Modules, Contracts, Directives, Example, Truncated)`; `CSharpModuleConfiguration.WithDocker(root, sourceRoot)`.

- [ ] Unit facts with a recording `IProcessRunner`: docker run argument list (labels, mounts, env per setting, restart policy, image, `dotnet run app.cs -p:ArtifactsPath=/work/artifacts`); Start removes an existing container first; Delete of missing container tolerated; inspect parsing of `running`/`exited` + exit code; generated `Directory.Build.props` references Client.
- [ ] Unit facts for neuron via Testing.Unit brain with a fake runner: Write persists source (≤128 KiB), Configure persists settings, Start/Stop publish `CSharpFileChanged`.
- [ ] Catalog fact: Time contracts yield `#:project /brain/src/Modules/Time/Contracts/DigitalBrain.Modules.Time.Contracts.csproj`.
- [ ] Sample `Samples/timer-report.cs` in new script shape.
- [ ] E2E (Docker): silo with Time + CSharp, write timer script, Start, observe `TimerTick` log line, Stop → `Stopped`.
- [ ] Build + tests; commit.

### Task 3: Delete Behaviors + Coding behavior half

- [ ] Remove `src/Modules/DigitalBrain/Behaviors/**`, `src/Behaviors/**`, slnx entries, Testing.E2E reference.
- [ ] Coding: delete Drafts, Validation, Artifacts, `CodeExecutionOptions`, their contracts/signals and tests; `CodingModule` keeps ChangeSet/options/GitRunner.
- [ ] Time `TimerProcessFacts` → new sample path; `ci.yml` stale step removed.
- [ ] Build Coding/Time; tests; commit (IntoChat/Apps still broken until Tasks 4–5 — commit together with Task 4/5 if needed).

### Task 4: Apps port

- [ ] `PackageContent(Manifest, Source)`; drop `Artifact` from `CommitPackage`/`PackageRevision`/`AppState`; drop Coding reference.
- [ ] `App`: Deploy = `ICSharpFile(key).Write/Configure/Start`; Retire = `Delete`; settings map `App`, `<setting>`, `Account__<slot>`; `AppSnapshot.CSharpFile`.
- [ ] `PackageRules`: reserved `App`, `Account`.
- [ ] Unit fakes: `RecordingCSharpFile` replaces `RecordingBehaviorProgram`; update AppFacts/PackageCommitFacts; E2E ResearcherPackage/PackageSharingFacts to new script shape.
- [ ] Build + tests; commit.

### Task 5: IntoChat + AppHost + AI

- [ ] Delete `Behavior/*`, synapse package files; add `CSharp/{CSharpEndpoints, CSharpAgentTools, CSharpTools, CSharpIndex, CSharpSharing, CSharpOptions}`; tools `csharp_contracts`, `csharp_write`, `csharp_run`; routes under `/workspaces/{id}/csharp`.
- [ ] `PackageService` drops draft check; `InstalledPackageView(App, CSharpFileSnapshot?)`; config `IntoChat:CSharp:*`.
- [ ] AppHost developer profile composes `CSharpModule.WithDocker`; resource `CSharp`.
- [ ] `AgentToolPolicy` → `csharp_`; developer instructions rewritten.
- [ ] Update unit + E2E tests (Hygiene, Composition, Security, Packages, Golden journey).
- [ ] Build; unit tests; commit.

### Task 6: Flutter + docs

- [ ] `ui_client.dart` csharp requests; `shell/lib/workspace/csharp/*` (list, editor, start/stop, logs, share); tests renamed/updated; `flutter analyze` + `flutter test`.
- [ ] CONTEXT.md, module READMEs (CSharp new, Apps/AI/Time/Coding updated).
- [ ] Commit.

### Task 7: Verification

- [ ] Grep: no `IBehavior|ISynapse|BehaviorApp|SynapseApp|synapse_|behavior_|BehaviorAuthoring` in src (excluding Testing `BehaviorRun`).
- [ ] `aspire run` developer profile; `csharp_write`/start via HTTP; logs show script output.
- [ ] IntoChat E2E + all touched unit suites green; code review of the diff; fix.
