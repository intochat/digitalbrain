# CSharp module: single-file C# apps that operate neurons

Status: proposed (2026-09-27). Replaces the Behavior/Synapse module and the behavior-only half of Coding.
Option A from the research: `dotnet run app.cs` inside a .NET SDK container, repo source mounted read-only. Option B (Roslyn compile in silo, runtime-only container) stays a later upgrade behind the same neuron contract.

## What a script looks like

```csharp
#:project /brain/src/Modules/Time/Contracts/DigitalBrain.Modules.Time.Contracts.csproj
using DigitalBrain.Contracts;
using DigitalBrain.Time.Timers;
using DigitalBrain.Time.Timers.Signals;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var timer = brain.Get<ITimer>(brain.Setting("TimerId") ?? "tea");
await foreach (var tick in brain.On<TimerTick>(timer))
    Console.WriteLine($"TimerTick {tick.TimerId} {tick.ObservedAt:O}");
```

No `IBehavior`, `ISynapse`, `BehaviorApp`, `SubscriptionRequirement`, readiness pipe or xUnit tests.
`DigitalBrain.Client` is referenced automatically; the script adds `#:project` lines only for the module contracts it uses (the contracts tool prints those lines).

Spike (done): `dotnet run app.cs -p:ArtifactsPath=/work/artifacts` in `mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1` with the repo mounted `:ro` and `#:project` to Client + Time contracts builds and runs in ~12 s cold. Nothing is written into the mount.

## Kernel client additions (`Kernel/Client`)

* `DigitalBrainClient.ConnectAsync(string[] args, CancellationToken)` → `DigitalBrainConnection : IDigitalBrain, IAsyncDisposable`.
  Moved out of `BehaviorApp`: generic host + `UseOrleansClient` + `AddDigitalBrain()` + serializer registration of the entry assembly and its references.
  Reads `Gateways` / `ClusterId` / `ServiceId` (env or args); `LocalDevelopment=true` → localhost clustering. A gateway host name (`host.docker.internal`) is resolved to an IP before static clustering.
* `brain.On<T>(neuron, ct)` → `IAsyncEnumerable<T>` (subscribe + `ReadAllAsync`, disposes the subscription).
* `brain.Setting(name)` → reads `CSharpFile__Settings__<name>` from the connection configuration (settings the owner/app configured).

## Module `src/Modules/Microsoft/CSharp`

| Project | Assembly |
| --- | --- |
| Contracts | `DigitalBrain.Modules.Microsoft.CSharp.Contracts` (namespace `DigitalBrain.Microsoft.CSharp`) — name required by `NeuronRegistry` |
| CSharp | `DigitalBrain.Modules.Microsoft.CSharp` — module, neuron, docker runner, contract catalog |
| Aspire.Hosting | `DigitalBrain.Modules.Microsoft.CSharp.Aspire.Hosting` — `WithDocker(workRoot, sourceRoot)` projection |
| Tests/Unit, Tests/E2E | E2E needs a Docker daemon (real container against a real silo) |
| Samples/timer-report.cs | used by the Time process test |

### Contract

```csharp
[Alias("microsoft.csharp.file")]
public interface ICSharpFile : INeuron
{
    Task<CSharpFileSnapshot> Read(CancellationToken cancellationToken = default);
    Task<CSharpFileSnapshot> Write(string source, CancellationToken cancellationToken = default);
    Task<CSharpFileSnapshot> Configure(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default);
    Task<CSharpFileSnapshot> Start(CancellationToken cancellationToken = default);   // restarts if running
    Task<CSharpFileSnapshot> Stop(CancellationToken cancellationToken = default);
    Task<string> ReadLogs(int tail = 200, CancellationToken cancellationToken = default);
    Task Delete(CancellationToken cancellationToken = default);                       // stop + clear state
}

public sealed record CSharpFileSnapshot(string Id, string Source, IReadOnlyDictionary<string, string> Settings,
    CSharpFileStatus Status, int? ExitCode, DateTimeOffset? StartedAt);
public enum CSharpFileStatus { Stopped, Building, Running, Exited }   // live from docker inspect
public sealed record CSharpFileChanged(string FileId, CSharpFileStatus Status) : Signal;
```

State: `IPersistentState<CSharpFileState>` on the default blob grain storage, `CSharpFileState(string Source, Dictionary<string,string> Settings)`. Source ≤ 128 KiB.

### Execution (`DockerCSharpRunner`, uses the shared `IProcessRunner` from Microsoft/DotNet)

* Work dir `<Root>/<sha(fileId)>/`: `app.cs` + generated `Directory.Build.props` (ProjectReference to Client, `PublishAot=false`).
* `docker run -d --name csharp-<sha> --label digitalbrain.csharp=<ServiceId> --restart on-failure --add-host host.docker.internal:host-gateway -v <sourceRoot>:/brain:ro -v <workDir>:/work -v digitalbrain-csharp-nuget:/root/.nuget/packages -w /work -e Gateways=… -e ClusterId=… -e ServiceId=… -e CSharpFile__Id=… -e CSharpFile__Settings__<k>=<v> <image> dotnet run app.cs -p:ArtifactsPath=/work/artifacts`
* Stop/Delete = `docker rm -f`; status = `docker inspect`; logs = `docker logs --tail`.
* `--restart on-failure` replaces supervisor + heartbeat: a script that loses the silo exits non-zero and Docker restarts it; a script that finishes stays `Exited(0)`.
* Options `DigitalBrain:CSharp`: `Root`, `SourceRoot`, `Image` (default `mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1`), `Gateways`/`ClusterId`/`ServiceId` (default from silo, gateway host rewritten to `host.docker.internal`).

### Contract catalog (moved from Coding `ContractCatalog`)

Reflects loaded `*.Contracts` assemblies in the silo; per module returns neuron interfaces, signals and the exact `#:project /brain/<repo-relative csproj>` directive (csproj located by assembly name under `SourceRoot`). Example text rewritten for the new script shape.

## Deletions

* `src/Modules/DigitalBrain/Behaviors/**` (6 projects) and `src/Behaviors/**`.
* Coding behavior half: `ICodeDraft` + draft contracts/signals, `CodeArtifactRef`, `Drafts/*`, `Validation/*` (incl. `BehaviorBuildTemplate`, `CodeValidationService`, `ContainedProcessRunner`, `WindowsContainedProcess`, `ContractCatalog` → moved), `Artifacts/*`, `CodeExecutionOptions` and their tests. Coding keeps ChangeSet + `CodingModuleOptions` + `GitRunner`.
* IntoChat `Behavior/*`, `SynapseSharing`, `ShareSynapseRequest`, `SynapseAccountOptions`, `PackageCheckFailedException`, behavior/synapse routes and MCP endpoints.

## Ports

* **Apps.** `PackageContent(Manifest, Source)` (no Tests/ModuleIds); `CommitPackage`/`PackageRevision` lose `Artifact`; `PackageNeuron` loses `VerifyArtifact`. `App` deploys generation `g` as `ICSharpFile("app-csharp-" + sha(appKey\0g))`: `Write(source)` → `Configure(settings)` → `Start()`; retire = `Delete()`. `AppState.Artifact` removed. Settings env: `CSharpFile__Settings__App` = app key, `…__<setting>`, `…__Account__<slot>`; reserved setting names `App`, `Account`. `AppSnapshot.BehaviorProgram` → `CSharpFile`.
* **IntoChat.** `CSharp/` folder: tools `csharp_contracts`, `csharp_write` (name, source), `csharp_run` (name, action start|stop|status|logs|delete); a trimmed name index per workspace for listing; routes `/workspaces/{id}/csharp[/{name}[/start|stop|logs|share]]` (developer mode only, as today); `CSharpSharing` = read source → commit → publish. Config `IntoChat:CSharp:{Root, AllowActivation, ModelProfile}` replaces `IntoChat:BehaviorAuthoring:*`. `PackageService` drops the draft check and `RequireCoding`. `InstalledPackageView(App, CSharpFileSnapshot?)`.
* **AppHost.** Developer profile composes `CSharpModule` (resource name `CSharp`) with `WithDocker(root, repoRoot)`; Coding stays for `WithSolution`.
* **AI.** `AgentToolPolicy`: prefix `csharp_`, `RequestsCSharpAuthoring`.
* **Time.** `TimerProcessFacts` runs `Microsoft/CSharp/Samples/timer-report.cs` on the host (`dotnet run --file`, `#:project` relative paths).
* **Flutter.** `behaviors/*` → `csharp/*` against the new routes/JSON (list, source editor, start/stop, logs, share); Dart tests follow.
* **Docs.** `CONTEXT.md` Behavior section, module READMEs, Apps/AI/Time READMEs, stale `ci.yml` step.

## Known gaps (accepted for v1)

* A script has full client authority over the brain (the open caller-context gap from behavior packages).
* No compile check at `Write` or at package commit; errors surface as `Exited` + logs. Option B fixes this.
* Orphaned containers when the silo host dies stay running; they are labelled by ServiceId for cleanup.
* Requires Docker on the silo host (developer profile only, as behaviors were).
