# C# files

Single-file C# apps that operate neurons. An agent (or a person) writes one `app.cs`; the `ICSharpFile` neuron keeps it as a string in grain state and sends it to the C# sandbox, which compiles and runs it with `dotnet run app.cs`.

```csharp
#:project /brain/src/Modules/Time/Contracts/DigitalBrain.Modules.Time.Contracts.csproj
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
| `Start()` | Stops the previous run and starts the current source as a new run. |
| `Stop()` / `Delete()` | Stop the run; `Delete` also clears the state. |
| `Read()` / `ReadLogs(tail)` | Live run status (`Stopped`, `Running`, `Exited` + exit code) and console output. |

`CSharpFileChanged` is published on every write and lifecycle change.

## Execution

Scripts run in the `csharp-sandbox` resource: a container built from [`Sandbox/`](Sandbox/) that the AppHost declares stopped (`WithExplicitStart`). Several scripts share it, each in its own folder and process.

1. `Start` asks `IAspire` for the sandbox. If it is not running, the runner subscribes to `ResourceStateChanged`, calls `IAspire.StartResource("csharp-sandbox")` and waits for `Running` + `Healthy`; the sandbox URL comes from the resource state the AppHost reports.
2. It posts `POST /runs?identifier=<runId>` with the source and the script environment: `Gateways`, `ClusterId`, `ServiceId`, `GatewayRelayHost` (loopback silos only) and `CSharpFile__Settings__<name>`. Every `Start` gets a new run id.
3. The sandbox writes `/work/<runId>/app.cs` plus a `Directory.Build.props` referencing `/brain/.../DigitalBrain.Client.csproj`, and starts `dotnet run app.cs`.

| Sandbox API | |
| --- | --- |
| `POST /runs?identifier=` | Start a run (`202`); `409` while that run is still running. |
| `GET /runs/{id}` | `Running` or `Exited` with exit code. |
| `GET /runs/{id}/logs?tail=` | Recent console output. |
| `POST /runs/{id}/stop` | SIGTERM, 10 s grace, then kill. |

* `Stop`, `Delete` and a repeated `Start` stop the run with SIGTERM first: `brain.Stopping` fires and the script's subscriptions unwatch; a killed script would leave every neuron it watched waiting on a dead observer. Loop over `brain.Stopping`, not `CancellationToken.None`.
* A script that does not compile settles on `Exited` with a non-zero code and the compiler output in its logs. Runs are not restarted.
* Orleans addresses a silo by the IP it advertises. For a loopback-advertised silo, the client opens a relay on that loopback endpoint inside the container and forwards it to `GatewayRelayHost`.

Options (`DigitalBrain:CSharp`): `SourceRoot`, the repository the sandbox mounts at `/brain` (default: the repository containing the silo), and `AspireApplication`, the `IAspire` key (default `DigitalBrain`). IntoChat's AppHost composes the module and `AspireModule` in the developer profile.

## Trust

A script has full client access to the brain; the container is not a permission boundary. Compose the module only where the people writing scripts are trusted.

## Tests

```powershell
dotnet test --project src/Modules/Microsoft/CSharp/Tests/Unit/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit.csproj
dotnet test --project src/Modules/Microsoft/CSharp/Tests/E2E/DigitalBrain.Modules.Microsoft.CSharp.Tests.E2E.csproj
```

The E2E needs a Docker daemon with Linux containers: Aspire starts the sandbox on demand, a real script subscribes to a Time neuron and exits cleanly, and a script that does not compile exits with its compiler errors.
