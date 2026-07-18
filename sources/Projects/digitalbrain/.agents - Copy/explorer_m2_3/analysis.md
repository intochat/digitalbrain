# Dynamic Data-Driven Bootstrap Refactoring Analysis & Plan
**Explorer 3 | Milestone 2: Minimal Runtime Host & GenesisNeuron Bootstrap Flow**

---

## 1. Executive Summary
This report analyzes the current procedural startup flow in the `DigitalBrain` codebase and outlines a detailed architectural plan to transition to a pure, data-driven neuronic bootstrap. Under this design:
- **Procedural builder chains** in C# (`digitalbrain.cs`) are replaced with a minimal runtime hosting floor.
- **Boot orchestration** is shifted entirely to the system `GenesisNeuron` (implemented in `.ino` format), which executes scenarios to ensure L6 safety prior to activation.
- **Aspire distributed application topology** is decoupled from hardcoded C# configurations and represented as data in `digitalbrain.ino`.
- **`AspireNeuron`** (Milestone 3) is introduced to receive this configuration data via dynamic synapses and run Aspire resources dynamically.

---

## 2. Examination of Procedural Startup Code

### A. `digitalbrain.cs` (Root)
Currently, `digitalbrain.cs` serves as the manual orchestrator for the Aspire AppHost. It executes a procedural builder chain:
- **Environment Setup**: Hardcodes critical ports and environments (`ASPNETCORE_URLS`, `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL`, etc.) and dynamically configures NuGet package directories to wire DCP paths (`dcp.exe`, `Aspire.Dashboard.exe`).
- **Aspire Distributed Application Builder**: Procedurally creates the builder and invokes the fluent extension `.AddDigitalBrain()` to register standard domains, connectors, visual shells, and MCP configurations.
- **Synchronous Run**: Compiles the entire static topology eager-load assembly at launch and executes `await builder.Build().RunAsync();`.

### B. `testdigitalbrain.cs` (Root)
This script is a simple developer-loop harness:
- **Incremental Build**: Spawns a background `dotnet build` process targeting `DigitalBrain.Test.csproj`.
- **Dynamic Assembly Execution**: Resolves the target compiled DLL under `bin/Debug/net11.0/DigitalBrain.Test.dll` and executes it directly via `dotnet`, forwarding CLI arguments.

### C. `DigitalBrainKernelBootstrapper.cs` (Kernel Project)
As the primary services bootstrapper, it configures Dependency Injection (DI) and lifecycle services:
- **Assembly Force Load**: Eagerly walks the execution directory to reflection-load all `DigitalBrain.*.dll` assemblies to ensure Orleans registers grain mappings.
- **DI Substrate**: Binds core services including the link-stage scanner (`NeuronCatalogScanner`), compiler cache (`InMemoryPlanCache`), interpretation engine (`InterpretedNeuronRegistry`), dynamic sources (`DynamicGeneratedInoSource`, `SqliteDynamicNeuronSource`), and OTel telemetry tickers.
- **Silo Lifecycle Hooks**: Injects `KernelOSBootstrapper` as an Orleans `IStartupTask` to trigger downstream boot logic.

### D. `KernelOSBootstrapper.cs` & `KernelOSNeuron.cs`
- **`KernelOSBootstrapper.cs`**: Executed automatically upon Orleans Silo startup. It invokes the license check (`CheckLicenseAgreementAsync`), checks if the `"primary"` brain exists in database registry (creating it if missing), and invokes the `BootSystemAsync` synapse of the Guid-keyed `IKernelOSNeuron` grain.
- **`KernelOSNeuron.cs`**: Handles the `BootSystem` synapse procedurally. It triggers `NeuronCatalogScanner` to update active assembly mappings, starts the `InterpretedNeuronRegistry` to load `.ino` descriptors, and boots `IGatewayNeuron` to listen for gRPC-web connections.

---

## 3. Current Orleans Silo & gRPC Initialization

### A. Orleans Silo Configuration
Orleans Silo setup resides in `AddDigitalBrainSiloExtensions.cs` (`kernel/DigitalBrain.Core.Hosting/`):
- **Clustering & Storage**: Uses local clustering and in-memory grain storage `digitalbrain` when testing, otherwise falling back to Redis.
- **Stream Substrate**: Registers synapse stream providers (`synapse-streams` / memory streams).
- **Startup Invariant Verification**: Schedules `NeuronCatalogScanner` as a startup task to scan loaded assemblies and register the discovered static `NeuronCatalogEntry` schemas to the `"global"` `IBrainCatalog` grain.

### B. Gateway and gRPC Setup
gRPC-web routing is configured in `DigitalBrainKernelBootstrapper.ConfigurePipeline`:
- Maps the primary gateway (`DigitalBrainGatewayService`), timeline visualizer (`BrainWatchService`), and registry (`BrainRegistryService`).
- Enforces CORS policies (`flutter-web` on ports `5800`/`5801`) to support browser-based shells.

---

## 4. Proposed Dynamic, Data-Driven Bootstrap Design

To align with the v5 "spec-first" architecture, the hardcoded C# builder chains and Orleans startup tasks must be replaced with a pure neuronic lifecycle managed by `GenesisNeuron`.

```
                    ┌─────────────────────────┐
                    │       digitalbrain.cs   │ (Minimal Host)
                    └────────────┬────────────┘
                                 │
                                 ▼ (Loads & Compiles)
                    ┌─────────────────────────┐
                    │      GenesisNeuron      │ (digitalbrain.ino)
                    └────────────┬────────────┘
                                 │
                   ┌─────────────┴─────────────┐
                   ▼ (Synapse: Loaded)         ▼ (Synapse: Activate)
      ┌─────────────────────────┐   ┌─────────────────────────┐
      │      BrainRegistry      │   │      AspireNeuron       │ (Milestone 3)
      │  (Ensure Primary Brain) │   │ (Configure Topology Data)│
      └─────────────────────────┘   └─────────────┬───────────┘
                                                  │
                                                  ▼ (Spawns Child Processes)
                                     [ Redis / MCP / UI Shell ]
```

### A. `GenesisNeuron` Design (`digitalbrain.ino`)
The `GenesisNeuron` is an interpreted neuron defined in InoLang. It handles the `loaded` synapse (the system power-on trigger). 
- **Topology Configuration Parsing**: It treats the list of dynamic distributed services (`orleans-redis`, `flutter-web`, `flutter-windows`, `digitalbrain-mcp`) as pure declarative topology data.
- **Scenario Verification (L6)**: The cold-start host (`BootHost`) runs the compiled `GenesisNeuron` scenarios in-process. If the scenario fails, the host immediately aborts, preventing broken compositions from starting.
- **Synapse Dispatch**:
  - Emits query synapses to `BrainRegistry` to check/create the `"primary"` brain context.
  - Dynamically dispatches configuration synapses (e.g. `ConfigureTopology`) to `AspireNeuron`, passing the declared topology data.

### B. `AspireNeuron` Bridge (Milestone 3)
- **Role**: Decouples the .NET Aspire distributed application pipeline by representing the builder itself as a platform-access neuron.
- **Configuration Synapse**: Receives a `ConfigureTopology(resources: list)` synapse containing the resource topology.
- **C# Sidecar**:
  - Implements the `delegate` methods of `AspireNeuron.ino`.
  - Uses the parsed topology arguments to dynamically configure the Aspire `DistributedApplicationBuilder` at runtime and boot the dashboard, Redis containers, executable shells, and developer sidecars in-process.

---

## 5. Required Code Changes & File Additions

### A. File Additions

#### 1. `sdk/DigitalBrain.SDK/Aspire/AspireNeuron.ino`
Establishes the spec-first contract for the Aspire runtime orchestrator:
```ino
neuron DigitalBrain.SDK.AspireRuntime
  "Handles dynamic orchestration of the Aspire distributed application topology."

  synapse RegisterResource(name: string, type: string, path: string = "", args: string = "", port: int = 0)
  synapse TopologyActive(resourceCount: int)

  on RegisterResource(r):
    # Handled by C# sidecar
    delegate

  scenario "registers a resource dynamically":
    when RegisterResource(name: "redis", type: "container", port: 59330)
    then TopologyActive emitted with resourceCount == 1
```

#### 2. `sdk/DigitalBrain.SDK/Aspire/AspireNeuron.cs` (C# Sidecar)
Provides the native hook into DCP/Aspire to spawn containers, executables, and project hosts dynamically based on incoming synapses:
```csharp
namespace DigitalBrain.SDK.Aspire.Runtime;

public sealed partial class AspireNeuron(IDistributedApplicationBuilder builder)
{
    private readonly List<string> _activeResources = new();

    public async Task<TopologyActive> OnRegisterResource(RegisterResource r, CancellationToken ct)
    {
        if (r.Type == "container")
        {
            builder.AddRedis(r.Name, r.Port);
        }
        else if (r.Type == "executable")
        {
            builder.AddExecutable(r.Name, "flutter", Path.GetFullPath(r.Path), r.Args.Split(' '));
        }
        else if (r.Type == "project")
        {
            // Dynamically register project dlls or paths
        }

        _activeResources.Add(r.Name);
        return new TopologyActive(_activeResources.Count);
    }
}
```

### B. Code Changes

#### 1. `digitalbrain.cs` (Root)
Collapse all hardcoded fluent builder configurations. Reduce the file to a simple boot-runner pointing to `digitalbrain.ino`:
```csharp
using DigitalBrain.Boot;

var argsMap = ParseArgs(args);
var outcome = await BootHost.RunFromFileAsync(
    Path.Combine(AppContext.BaseDirectory, "digitalbrain.ino"),
    argsMap,
    new AspireBootNeuronHost(new NativeAspireConnector(), argsMap.GetValueOrDefault("profile", "local")),
    CancellationToken.None
);

if (outcome.ExitCode != 0)
{
    Console.Error.WriteLine($"Boot Fault: {outcome.Message}");
    Environment.Exit(outcome.ExitCode);
}
```

#### 2. `DigitalBrainKernelBootstrapper.cs` (Kernel)
- Delete the registration:
  ```csharp
  builder.Services.AddTransient<Orleans.Runtime.IStartupTask, DigitalBrain.Kernel.OS.KernelOSBootstrapper>();
  ```
- Retain only the minimal dependency injection substrate required to parse and link interpreted neurons eagerly.

#### 3. `KernelOSBootstrapper.cs` & `KernelOSNeuron.cs`
- Fully delete these files.
- The procedural operations previously handled inside them (license check, brain initialization, discovery scanner triggers) are now natively expressed inside the `on loaded:` block of `digitalbrain.ino` (the `GenesisNeuron`).

---

## 6. Verification and Validation Plan

To ensure the refactored neuronic boot sequence functions successfully, the following verification suite must be executed:

1. **Unit Scenarios**:
   Run `dotnet test --filter "FullyQualifiedName~BootHostTests"` to ensure the cold-start harness successfully parses `digitalbrain.ino` and passes all safety invariants.
2. **Cluster Boot Test**:
   Execute `testdigitalbrain.cs` to compile and run the full test suite, ensuring no startup invariants drift or throw catalog reservation exceptions.
3. **AppHost Launch**:
   Execute `dotnet run digitalbrain.cs` to verify that `GenesisNeuron` compiles, verifies, and successfully hands off execution to the runtime silo with the primary brain activated.
