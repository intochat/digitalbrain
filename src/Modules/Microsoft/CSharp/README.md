# C# files

Single-file C# apps that operate neurons. An agent (or a person) writes one `app.cs`; the `ICSharpFile` neuron keeps it as a string in grain state and sends it to the C# sandbox, which compiles and runs it with `dotnet run app.cs`.

```csharp
#:project /brain/src/Modules/Time/DigitalBrain.Modules.Time.Contracts/DigitalBrain.Modules.Time.Contracts.csproj
using DigitalBrain.Time.Timers.Signals;
using ITimer = DigitalBrain.Time.Timers.ITimer;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var timer = brain.Get<ITimer>(brain.Setting("TimerId") ?? "tea");
await foreach (var tick in brain.On<TimerTick>(timer, brain.Stopping))
{
    Console.WriteLine($"TimerTick {tick.TimerId} {tick.ObservedAt:O}");
}
```

`DigitalBrain.Client` is referenced and `DigitalBrain.Contracts` / `DigitalBrain.Client` are imported by the `Directory.Build.props` the sandbox writes next to the script, so a script only adds `#:project` lines for the module contracts it uses. `CSharpContractCatalog` (the `csharp_contracts` tool) prints those lines per installed module.

## Contract

| `ICSharpFile` | |
| --- | --- |
| `Write(source)` | Stores the source (≤ 128 KiB). A running script keeps the previous source until `Start`. |
| `Configure(settings)` | Stores settings; the script reads them with `brain.Setting(name)`. Names are letters, digits and underscores. |
| `Start()` | Stops the previous run, starts the current source as a new run and keeps the file running (see Reconcile). |
| `Arm(trigger)` | Runs on signals instead: every `trigger.Signal` from neuron `trigger.Neuron` (`"<grain type>/<key>"`) starts one run that reads it with `brain.Trigger<T>()`. |
| `Stop()` / `Delete()` | Stop the run, the reconcile and any trigger; `Delete` also clears the state. |
| `Read()` / `ReadLogs(tail)` | Live run status (`Stopped`, `Running`, `Restarting`, `Exited` + exit code, `ShouldRun`, `Failures`) and console output. |

`CSharpFileChanged` is published on every write and lifecycle change.

## Execution

Scripts run in the `csharp-sandbox` resource: a container built from [`Sandbox/`](Sandbox/) that the AppHost declares stopped (`WithExplicitStart`). Several scripts share it, each in its own folder and process.

1. `Start` asks `IAspire` for the sandbox. If it is not running, the runner subscribes to `ResourceStateChanged`, calls `IAspire.StartResource("csharp-sandbox")` and waits for `Running` + `Healthy`; the sandbox URL comes from the resource state the AppHost reports.
2. It posts `POST /runs/<runId>` with the source and the script environment: `DigitalBrain__Edge`, `DigitalBrain__Token`, `CSharpFile__Settings__<name>` and, for a triggered run, `CSharpFile__Trigger`. Every run gets a new run id.
3. The sandbox writes `/work/<runId>/app.cs` plus a `Directory.Build.props` referencing `/brain/.../DigitalBrain.Client.csproj`, and starts `dotnet run app.cs`.

| Sandbox API | |
| --- | --- |
| `POST /runs/{id}` | Start a run (`202`); `409` while that run is still running. |
| `GET /runs/{id}` | `Running` or `Exited` with exit code. |
| `GET /runs/{id}/logs?tail=` | Recent console output. |
| `POST /runs/{id}/stop` | SIGTERM, 10 s grace, then kill. |

* `Stop`, `Delete` and a repeated `Start` stop the run with SIGTERM first: `brain.Stopping` fires and the script's subscriptions unwatch; a killed script would leave every neuron it watched waiting on a dead observer. Loop over `brain.Stopping`, not `CancellationToken.None`.

## Script edge

Scripts never join the cluster. `DigitalBrainClient.ConnectAsync(args)` reads `DigitalBrain:Edge` and `DigitalBrain:Token` and talks HTTPS to the brain:

| Edge | |
| --- | --- |
| `POST /scripts/v1/invoke` | `{ contract, key, method, arguments }` → one grain call; the JSON result or `204`. |
| `GET /scripts/v1/signals?contract=&key=&signal=` | Server-sent events: each signal of that type the neuron publishes, as JSON. |

`brain.Get<T>(id)` returns a proxy whose calls become invocations; `SubscribeAsync`/`On<T>` read the signal stream.

* The run token is HMAC-signed (file, run, 7-day expiry). The edge also asks the file: a token speaks for it only while it should run (the current run, or any run while armed), so `Stop` and `Delete` revoke every token.
* Only neuron interfaces from installed contract assemblies are callable, never `Watch`/`Unwatch`.
* Each call carries the owner's caller context (whoever started or armed the file) re-stamped as `App` behind `AppProxy`, with the file as `AppId`; grants and allowances apply to it. A file started without a caller calls unstamped.
* The edge address is `EdgeUrl`, or in development this brain's own HTTP address on `host.docker.internal`. `RunTokenKey` must be configured and shared by every silo in production.

## Production

With `SessionPoolEndpoint` set, runs go to an Azure Container Apps custom-container session pool running the same Sandbox image: one Hyper-V session per owner (`identifier=u-<hash of the principal>`), that owner's scripts side by side in it. The silo authenticates with `DefaultAzureCredential` (needs *Azure ContainerApps Session Executor* on the pool). Reads call `.management/getSession` first so inspecting never allocates a session. Set `Sandbox__IdleShutdown` on the pool so a session with no runs exits and frees itself (`OnContainerExit` lifecycle). Still to do before the product profile composes the module: publish the image, pool provisioning, egress restricted to the edge, contracts as packages instead of the `/brain` mount.

## Reconcile

`Start` records `ShouldRun` and the owner (the caller's principal: production keys one sandbox session per user by it) and registers a one-minute `reconcile` reminder:

| Run as the sandbox reports it | Reconcile |
| --- | --- |
| `Running` | nothing |
| `Exited(0)` | done: `ShouldRun = false` |
| `Exited(n ≠ 0)` | retry as a new run; after 5 consecutive failures, give up (`Exited`, `ShouldRun = false`) |
| gone (run or whole sandbox lost) | start a new run, bringing the sandbox back through `IAspire`; not counted as a failure |

An armed file keeps watching its trigger instead: the reconcile re-watches it after a silo restart, and a signal that arrives after the file was collected reactivates it. Triggered runs are not retried (the next signal is the next attempt); 5 failing runs in a row disarm the file.

A file that should run but is between runs reads as `Restarting`. A script that does not compile is retried like any crash, so its compiler output stays in the logs. Signals published while a script is down are not replayed.

Options (`DigitalBrain:CSharp`): `SourceRoot`, the repository the sandbox mounts at `/brain` (default: the repository containing the silo); `AspireApplication`, the `IAspire` key (default `DigitalBrain`); `EdgeUrl`; `RunTokenKey`; `SessionPoolEndpoint`. IntoChat's AppHost composes the module and `AspireModule` in the developer profile.

## Trust

A script acts as its owner through the edge, limited to installed neuron contracts; it no longer holds an Orleans client. The development sandbox is one shared container without egress limits, so compose the module only where the people writing scripts are trusted until the production pool is in place.

## Tests

```powershell
dotnet test --project src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit.csproj
dotnet test --project src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Tests.E2E/DigitalBrain.Modules.Microsoft.CSharp.Tests.E2E.csproj
```

The E2E needs a Docker daemon with Linux containers: Aspire starts the sandbox on demand, a real script subscribes to a Time neuron and exits cleanly, and a script that does not compile exits with its compiler errors.
