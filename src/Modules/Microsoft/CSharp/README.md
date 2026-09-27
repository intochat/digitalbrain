# C# files

Single-file C# apps that operate neurons. An agent (or a person) writes one `app.cs`; the `ICSharpFile` neuron keeps it as a string in grain state and runs it with `dotnet run app.cs` in a .NET SDK container.

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

`DigitalBrain.Client` is referenced and `DigitalBrain.Contracts` / `DigitalBrain.Core` are imported by a generated `Directory.Build.props`, so a script only adds `#:project` lines for the module contracts it uses. `CSharpContractCatalog` (the `csharp_contracts` tool) prints those lines per installed module.

## Contract

| `ICSharpFile` | |
| --- | --- |
| `Write(source)` | Stores the source (≤ 128 KiB). A running container keeps the previous source until `Start`. |
| `Configure(settings)` | Stores settings; the script reads them with `brain.Setting(name)`. Names are letters, digits and underscores. |
| `Start()` | Replaces any container and runs the current source. |
| `Stop()` / `Delete()` | Remove the container; `Delete` also clears the state. |
| `Read()` / `ReadLogs(tail)` | Live container status (`Stopped`, `Running`, `Restarting`, `Exited` + exit code) and console output. |

`CSharpFileChanged` is published on every write and lifecycle change.

## Execution

`Start` writes `app.cs` and `Directory.Build.props` into `<Root>/<container>/` and runs:

```
docker run --detach --label digitalbrain.csharp=<ServiceId> --restart on-failure:5 --add-host host.docker.internal:host-gateway
  -v <SourceRoot>:/brain:ro -v <work>:/work -v digitalbrain-csharp-nuget:/root/.nuget/packages
  -e Gateways=… -e ClusterId=… -e ServiceId=… [-e GatewayRelayHost=host.docker.internal]
  -e CSharpFile__Settings__<name>=<value>
  mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1 dotnet run app.cs -p:ArtifactsPath=/work/artifacts
```

* `Stop`, `Delete` and a restarting `Start` run `docker stop --time 10` before removing the container. The script's host sees SIGTERM, `brain.Stopping` fires and its subscriptions unwatch; a killed script would leave every neuron it watched waiting on a dead observer. Loop over `brain.Stopping`, not `CancellationToken.None`.
* A script that exits non-zero is restarted up to 5 times (`--restart on-failure:5`): enough to ride out a silo restart. A script that does not compile shows its compiler output in the logs while `Restarting`, then settles on `Exited(1)`. A script that finishes stays `Exited(0)`.
* Orleans addresses a silo by the IP it advertises. For a loopback-advertised silo, the client opens a relay on that loopback endpoint inside the container and forwards it to `GatewayRelayHost`.

Options (`DigitalBrain:CSharp`): `Root` (default: temp) and `SourceRoot` (default: the repository containing the silo). The gateway, relay host and cluster ids come from the silo's own endpoint and cluster options. IntoChat's AppHost composes the module only in the developer profile, with `IntoChat:CSharp:Root`.

## Trust

A script has full client access to the brain; the container is not a permission boundary. Compose the module only where the people writing scripts are trusted.

## Tests

```powershell
dotnet test --project src/Modules/Microsoft/CSharp/Tests/Unit/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit.csproj
dotnet test --project src/Modules/Microsoft/CSharp/Tests/E2E/DigitalBrain.Modules.Microsoft.CSharp.Tests.E2E.csproj
```

The E2E needs a Docker daemon with Linux containers: a real script subscribes to a Time neuron from the container and exits cleanly.
