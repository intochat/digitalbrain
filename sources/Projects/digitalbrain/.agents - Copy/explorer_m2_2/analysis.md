# Codebase Sweep & Refactoring Plan: Pure Neuronic Bootstrap Flow

## Executive Summary
This report analyzes the current procedural startup code of `DigitalBrain` and outlines a plan to transition from procedural, compile-time builder chains in C# to a dynamic, data-driven, and pure neuronic bootstrap flow in accordance with the **v5 paradigm** ("One file per behavior", "spec-first composition", and "zero global catalog").

By shifting the application topography configuration out of procedural C# builders (e.g., `.WithShell()`, `.WithMcp()`) and into `digitalbrain.ino` using a dynamic system neuron (`GenesisNeuron` / `DigitalBrain.System`), we achieve a generic, highly decoupled kernel. The host becomes a thin runner, and all distributed topologies, AI providers, and shell resources are configured dynamically via spec-first composition and dynamic synapse dispatch.

---

## 1. Examination of Procedural Startup Code (`digitalbrain.cs` and `testdigitalbrain.cs`)

### `digitalbrain.cs`
The root file `digitalbrain.cs` serves as the single launch entry point. It currently configures and starts the Aspire-based distributed application topology through a compile-time procedural builder chain.
- **Environment Setup**: Hardcodes key environment variables like `ASPNETCORE_URLS` ("http://localhost:18888"), `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL`, and acceptance of license arguments (`--accept-license`).
- **DCP Path Resolution**: Dynamically wires DCP orchestrator and dashboard tools paths (`dcp.exe`, `ext/`, `Aspire.Dashboard.exe`) by scanning the local NuGet package cache (e.g., `E:\nuget` or `%USERPROFILE%\.nuget\packages`).
- **Procedural Builder Chain**:
  ```csharp
  var builder = Aspire.Hosting.DistributedApplication.CreateBuilder(args);

  builder.AddDigitalBrain()
      .WithLlmProvider<OpenAIProvider>()
      .WithLlmProvider<GrokProvider>()
      .WithEmbedding<TextEmbedding3Small>()
      .WithVoice2Text<LargeV3Turbo>()
      .WithDefaultConnectors()
      .WithShell()
      .WithMcp();

  await builder.Build().RunAsync();
  ```
  - `builder.AddDigitalBrain()`: Initialises the `DigitalBrainResource` (under `kernel/DigitalBrain.Hosting`), which procedurally adds a `RedisResource` container (`orleans-redis`) and an `OrleansService` cluster (`digitalbrain-cluster`), and registers the central `Projects.DigitalBrain_Kernel` project.
  - `.WithLlmProvider<>()` / `.WithEmbedding<>()` / `.WithVoice2Text<>()`: Procedurally registers LLM providers and models into an internal `AiDomainBuilder`.
  - `.WithShell()`: Procedurally adds the Flutter shell resource (Web and Windows executables) and sets up their endpoints and references.
  - `.WithMcp()`: Procedurally registers the `DigitalBrain_SDK_Mcp` project resource if running under `Product` or `Local` profiles.

### `testdigitalbrain.cs`
This is a lightweight test-runner script that wraps C# test execution. It:
1. Dynamically invokes `dotnet build DigitalBrain.Test/DigitalBrain.Test.csproj -c Debug` to build the test project.
2. Locates the compiled test suite library (`DigitalBrain.Test.dll`).
3. Runs the compiled test DLL using `dotnet DigitalBrain.Test.dll [args]` while forwarding command-line arguments.

---

## 2. Orleans Silo/gRPC Initialization, Neuron Registry, and Licensing/Primary Brain Creation

Through ripgrep sweeps and file inspections, the current initialization pipelines were mapped:

### Orleans Silo & gRPC Setup
- **Silo Initialization**: Defined in `kernel/DigitalBrain.Core.Hosting/AddDigitalBrainSiloExtensions.cs` via `builder.UseOrleans(silo => { ... })`. It configures localhost clustering, Orleans memory grain storage named `"digitalbrain"`, memory reminders, and the core transactional storage providers.
- **gRPC & Endpoints**: Services are registered in `DigitalBrainKernelBootstrapper.ConfigureServices(...)` via `builder.Services.AddGrpc()` and CORS configurations. They are mapped to Kestrel endpoints in `ConfigurePipeline(WebApplication app)`:
  - `app.MapGrpcService<DigitalBrainGatewayService>()` (Core client ingress).
  - `app.MapGrpcService<BrainWatchService>()` (System observability).
  - `app.MapGrpcService<BrainRegistryService>()` (Multi-brain registry).

### Neuron / Plugin Registration
- **Assembly Loading**: `DigitalBrainKernelBootstrapper.cs` (lines 34-48) dynamically reflects and loads all `DigitalBrain.Domains.*` and `DigitalBrain.*` assemblies in the base directory to ensure Orleans can locate compiled grains at runtime.
- **Static Catalog & Scanning**: The `AssemblyScanningContractCatalog` (configured in `DigitalBrainKernelBootstrapper.cs` line 127) scans the loaded assemblies for types implementing `ICallNeuronTarget` (or sibling target interfaces) containing the `[GrainType]` attribute. It populates a plan cache (`IPlanCache`).
- **Dynamic Interpreted Neuron Loading**: Handled by the `InterpretedNeuronRegistry` (registered in `DigitalBrainKernelBootstrapper.cs` line 156). It aggregates dynamic sources (like `DynamicGeneratedInoSource` and `SqliteDynamicNeuronSource`) to dynamically discover and activate `.ino`-authored interpreted neurons stored in SQLite or the filesystem.
- **Silo Activation Lifecycle**:
  - `KernelOSBootstrapper` is registered as an Orleans `IStartupTask`.
  - When the Silo starts, `KernelOSBootstrapper.Execute` runs.
  - It triggers `IKernelOSNeuron.BootSystemAsync(BootSystem)`, which:
    1. Fires a `DiscoverNeuronsRequest` synapse, invoking `NeuronCatalogScanner` to resolve compiled assembly-reflected neurons.
    2. Start the `InterpretedNeuronRegistry` to register dynamic interpreted paths.
    3. Fires an `InitializeGateway` synapse, which activates `IGatewayNeuron` gateway listeners to begin receiving client traffic.

### Licensing and Primary Brain Creation
- **License Enforcement**: Inside `KernelOSBootstrapper.Execute`, the system retrieves the `ILicenseNeuron` grain (`"global"`) and invokes `CheckLicenseAgreementAsync()`. This enforces the `DIGITALBRAIN_ACCEPT_LICENSE` environment variable requirement at silo start.
- **Primary Brain Verification**: Inside `KernelOSBootstrapper.Execute`, the `IBrainRegistry` grain is fetched. If a brain container named `"primary"` does not exist in Orleans memory storage, it calls `CreateBrainAsync("Primary")` to initialize the default execution context.

---

## 3. Dynamic, Data-Driven Bootstrap Flow Design (`GenesisNeuron`)

To conform to the **v5 spec-first composition** model, we replace the procedural C# setup with a pure neuronic bootstrap flow directed by a `GenesisNeuron` that parses and interprets the top-level topology configuration.

### Spec-First Composition (`digitalbrain.ino`)
The topology configuration is declared declaratively in `digitalbrain.ino` in the workspace root:
```ino
neuron DigitalBrain.System
  "The distributed OS coordinator. Starts core services, manages dynamic resources, and binds the visual shell."

  using loaded            = synapse(DigitalBrain.Kernel.Loaded)
  using brains            = neuron(DigitalBrain.BrainRegistry)
  using aspire            = neuron(DigitalBrain.SDK.AspireRuntime)
  using telemetry         = neuron(DigitalBrain.SDK.TelemetryTracker)
  using created           = synapse(DigitalBrain.BrainCreated)
  using resourceAdded     = synapse(DigitalBrain.ResourceAdded)

  @telemetry:counter:system_boots
  @telemetry:counter:resources_registered

  on loaded:
    log "system: initializing DigitalBrain substrate"
    count system_boots

    # 1. Ensure the primary Orleans brain container exists
    let existing = ask brains to "list"
    if existing:
      log "system: existing brains discovered in Orleans storage"
    else:
      log "system: genesis flow - creating primary brain container"
      ask brains to "create primary"
      emit created(brainId: "primary")

    # 2. Dynamically compose and register distributed Aspire resources via Aspire API
    log "system: mapping distributed application topography via Aspire API"
    
    # Core database clustering
    ask aspire to "register-resource orleans-redis type:container port:59330"
    count resources_registered
    emit resourceAdded(name: "orleans-redis", type: "container")

    # Personal assistant visual environments
    ask aspire to "register-resource flutter-web type:executable path:../../UI/flutter args:run -d web-server --release port:5800"
    count resources_registered
    emit resourceAdded(name: "flutter-web", type: "executable")

    ask aspire to "register-resource flutter-windows type:executable path:../../UI/flutter args:run -d windows --print-dtd port:5821 autostart:false"
    count resources_registered
    emit resourceAdded(name: "flutter-windows", type: "executable")

    # Code intelligence & developer sidecars
    ask aspire to "register-resource digitalbrain-mcp type:project path:sdk/DigitalBrain.SDK.Mcp port:5810"
    count resources_registered
    emit resourceAdded(name: "digitalbrain-mcp", type: "project")

    log "system: distributed application topography successfully established. RFW layers active."
```

### The `GenesisNeuron` & Dynamic Dispatch Synapse Path
1. **Thin Silo Activation**: The Orleans Silo starts up with *only* the kernel and the core SDK connector grains registered.
2. **Interpreter Invocation**: A silo `IStartupTask` (such as `GenesisBootstrapper` replacing the old procedural orchestrator) reads `digitalbrain.ino` from the workspace root.
3. **Compilation**: The bootstrapper compiles `digitalbrain.ino` dynamically using the InoLang compiler (`InoCompiler.Compile`). It performs static syntax verification and ensures that the composition scenario passes.
4. **Loaded Synapse Trigger**: The bootstrapper gets the grain interface for the newly compiled `DigitalBrain.System` neuron and sends a `Loaded` synapse to trigger its execution.
5. **Dynamic Synapse Dispatch to `AspireNeuron`**:
   - The interpreted execution of `DigitalBrain.System` encounters `ask aspire to "register-resource..."` prompts.
   - It routes these prompts dynamically over the grain network to the `AspireRuntimeNeuron` (`DigitalBrain.SDK.Aspire.Runtime`).
   - The `AspireRuntimeNeuron` acts as the interface to the native `IAspireBootConnector` (wrapping Aspire's DCP CLI).
   - `IAspireBootConnector` dynamically configures and spins up the distributed resource processes (`orleans-redis`, `flutter-web`, `flutter-windows`, `digitalbrain-mcp`) at runtime.

```
       [ digitalbrain.cs (Thin Runner) ]
                       │
                       ▼
       [ Orleans Silo Startup Task ]
                       │ (Compiles & Loads digitalbrain.ino)
                       ▼
    [ DigitalBrain.System Neuron (Genesis) ]
        │                             │
        ├─► [list/create]             └─► [register-resource]
        │                                        │
        ▼                                        ▼
 [ BrainRegistry Neuron ]              [ AspireRuntimeNeuron ]
                                                 │
                                                 ▼
                                     [ IAspireBootConnector ]
                                                 │
                                        ┌────────┴────────┐
                                        ▼                 ▼
                                [ Redis Container ]  [ Flutter Web ]
```

---

## 4. Code Changes and File Additions (Transition Plan)

To transition from the procedural builder chains to a pure neuronic bootstrap flow, the following refactoring steps are required:

### A. Deprecate and Strip Procedural Builders in `digitalbrain.cs`
1. **Strip Fluent Chains**: Remove `.WithLlmProvider<>()`, `.WithEmbedding<>()`, `.WithVoice2Text<>()`, `.WithShell()`, and `.WithMcp()` from `digitalbrain.cs`.
2. **Transform into a Thin Host**: Re-write `digitalbrain.cs` to simply construct the bare host and start the InoLang runtime:
   ```csharp
   var builder = Aspire.Hosting.DistributedApplication.CreateBuilder(args);
   // Register only Orleans and local Silo infra
   builder.AddDigitalBrainSubstrate(); 
   await builder.Build().RunAsync();
   ```

### B. Delete/Fold Legacy Hosting Projects & Builders
1. **Folder Consolidation**: Merge `kernel/DigitalBrain.Hosting` and `kernel/DigitalBrain.Boot` directly into the single `kernel/DigitalBrain.Runtime` assembly, satisfying the v5 project-consolidation constraint (19 projects → 5 projects).
2. **Remove Builders**: Delete `DigitalBrainBuilder.cs` and `DigitalBrainHostingExtensions.cs`.

### C. Expand `AspireRuntimeNeuron.cs` for Dynamic Resource Registration
Update the SDK Aspire neuron grain (`sdk/DigitalBrain.SDK/Aspire/Runtime/AspireRuntimeNeuron.cs`) to dynamically accept resource registration strings and map them onto the native `IAspireBootConnector`:
```csharp
public async Task<string> AskAsync(string prompt)
{
    if (prompt.StartsWith("register-resource ", StringComparison.OrdinalIgnoreCase))
    {
        // Parse "register-resource <name> type:<type> path:<path> port:<port> args:<args>"
        var parsedConfig = ResourceConfigParser.Parse(prompt);
        return await connector.RegisterResourceAsync(parsedConfig, CancellationToken.None);
    }
    // ... keep existing stop / start / restart handlers ...
}
```

### D. Update the Orleans `IStartupTask` (`GenesisBootstrapper.cs`)
Create a new `GenesisBootstrapper.cs` startup task (replacing `KernelOSBootstrapper.cs`) inside `DigitalBrain.Kernel` to boot InoLang:
1. Enforce `ILicenseNeuron.CheckLicenseAgreementAsync()`.
2. Read `digitalbrain.ino` from the execution directory.
3. Call `InoCompiler.Compile(inoSource, catalog)` to generate the execution plan.
4. Inject the plan into the dynamic `Interpreter`.
5. Trigger the `DigitalBrain.Kernel.Loaded` synapse to execute the system configuration.

### E. Introduce the Native Resource Registration in `IAspireBootConnector`
Extend `IAspireBootConnector` and its implementation `AspireBootConnector` with `RegisterResourceAsync(ResourceConfig config, CancellationToken ct)` to dynamically tell the Aspire AppHost / DCP runner to register the container, project, or executable without requiring static C# compilation.

---

## 5. Verification Method and Validation Plan

### Step 1: In-Process Scenario Verification
Verify the compiled `digitalbrain.ino` scenario locally using the InoLang test harness:
```powershell
dotnet test kernel/DigitalBrain.Platform.Test/Boot/BootHostTests.cs
```
*Verification condition:* The test `Genesis_ino_compiles_links_and_gates_green` passes, confirming that the Ino compiler successfully parses and executes `digitalbrain.ino`.

### Step 2: Running E2E Integration Suite
Build and execute the full E2E test DLL to confirm the Orleans clustering, dynamic interpretability, and synapse dispatch paths are completely unaffected by the transition:
```powershell
dotnet run --project testdigitalbrain.cs
```
*Verification condition:* The test run exits with code `0` and all integration tests under `DigitalBrain.Test.dll` (including Orleans silo startups and Aspire cluster tests) pass successfully.
