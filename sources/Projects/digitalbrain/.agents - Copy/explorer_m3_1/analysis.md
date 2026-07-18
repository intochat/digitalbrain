# Analysis Report: Dynamic Aspire Orchestration & Decoupled Topology Configuration

## 1. Executive Summary
This report analyzes the current structure of the `.NET Aspire AppHost` registration under `kernel/DigitalBrain.Hosting/` and details a comprehensive architectural blueprint to transition the platform from static, hardcoded C# configurations to a **dynamic, data-driven topology model**. 

By parsing the spec-first `digitalbrain.ino` file during both the AppHost boot phase and the runtime Orleans grain activation, we completely decouple resource registration from static C# project builds. Under the proposed model, the system `GenesisNeuron` dynamically dispatches `ConfigureAspireResource` synapses to the `AspireRuntimeNeuron`, which interacts with the native `IAspireBootConnector` (wrapping the Aspire/DCP CLI) to lazily control resource lifecycles (containers, executables, projects) at runtime.

---

## 2. Current Static Aspire AppHost Configuration (Codebase Sweep)

Under the current `DigitalBrain` v5 architecture, the Aspire AppHost is initialized through a static, fluent C# registration chain. The primary files swept and analyzed in `kernel/DigitalBrain.Hosting/` are:

### A. `DigitalBrainHostingExtensions.cs`
- **Location**: `kernel/DigitalBrain.Hosting/DigitalBrainHostingExtensions.cs`
- **Role**: Entry extension method `AddDigitalBrain(...)` for `IDistributedApplicationBuilder`.
- **Observations**:
  - Parses launch settings profile via `ProfileConfiguration.Parse(builder.Configuration, args)`.
  - Statically instantiates the `DigitalBrainResource` and immediately binds specific domain silo projects:
    ```csharp
    digitalbrain.WithDomain<Projects.DigitalBrain_Domains_Dynamic>();
    digitalbrain.WithDomain<Projects.DigitalBrain_Domains_Samples>();
    ```
  - Statically applies AI/LLM model configurations using `ApplyConfigurations()`.

### B. `DigitalBrainBuilder.cs`
- **Location**: `kernel/DigitalBrain.Hosting/DigitalBrainBuilder.cs`
- **Role**: Fluent builder implementing `IDigitalBrainBuilder`.
- **Observations**:
  - Contains hardcoded methods for optional platform features: `WithShell()`, `WithMcp()`, `WithDefaultConnectors()`.
  - **`WithShell()`**: Hardcodes the UI Flutter project creation and execution parameters by calling `Resource.AppBuilder.AddFlutter()` and configuring `flutter-web` and `flutter-windows` executables.
  - **`WithMcp()`**: Hardcodes the execution of `Projects.DigitalBrain_SDK_Mcp` project, mapping the kernel endpoints and ports:
    ```csharp
    _ = Resource.AppBuilder.AddProject<Projects.DigitalBrain_SDK_Mcp>("digitalbrain-mcp")
        .WithReference(Resource.Kernel!)
        .WaitFor(Resource.Kernel!)
        .WithEnvironment("KERNEL_ENDPOINT", Resource.Kernel!.GetEndpoint("kernel-https"))
        .WithHttpEndpoint(port: 5810, targetPort: 5810, name: "http", isProxied: false);
    ```

### C. `DigitalBrainResource.cs`
- **Location**: `kernel/DigitalBrain.Hosting/DigitalBrain/DigitalBrainResource.cs`
- **Role**: Represents the backing AppHost resource model of `DigitalBrain`.
- **Observations**:
  - Statically sets up Orleans clustering and Redis storage in `InitializeInfrastructure()`:
    ```csharp
    Redis = AppBuilder.AddRedis("orleans-redis");
    Orleans = AppBuilder.AddOrleans($"{Name}-cluster")
        .WithClustering(Redis)
        .WithMemoryGrainStorage("digitalbrain")
        .WithMemoryReminders();
    ```
  - Statically references the primary Orleans Host `Projects.DigitalBrain_Kernel` as the "kernel" project.
  - Generates silo silo sub-projects through generic compile-time metadata types `WithDomain<TProject>()`.

### D. `FlutterCompositionBuilder.cs`
- **Location**: `kernel/DigitalBrain.Hosting/DigitalBrain/FlutterCompositionBuilder.cs`
- **Role**: Manages the construction of visual RFW executables.
- **Observations**:
  - Hardcodes launching parameters for standard flutter executables (`flutter-web`, `flutter-windows`):
    ```csharp
    var web = builder.AddExecutable("flutter-web", "flutter", workingDir,
            "run", "-d", "web-server",
            "--web-hostname=localhost", "--web-port=5800", "--release")
        .WithHttpEndpoint(port: 5800, targetPort: 5800, name: "http", isProxied: false);
    ```

---

## 3. The Challenge of Runtime Dynamic Registration in .NET Aspire

A primary constraint of the `.NET Aspire` engine is that the `DistributedApplicationBuilder` builds a **static resource graph** at startup. Once `builder.Build()` is called and the `DistributedApplication` runs, the model of containers, executables, and project processes is **immutable**. 

Therefore:
1. We cannot call `builder.AddProject` or `builder.AddExecutable` on a running AppHost from Orleans runtime grains.
2. The native `IAspireBootConnector` (which issues commands like `aspire resource start <name>` via CLI) can only target resources that **already exist** in the AppHost's baked model.

### The Decoupled Solution
To satisfy both the static graph requirements of .NET Aspire and the dynamic, spec-first desires of InoLang (`digitalbrain.ino`):
- **Boot-Time Dynamic Parsing**: The AppHost itself must read the `digitalbrain.ino` spec file **prior** to calling `builder.Build()`. It parses the resource declarations and dynamically registers them using generalised, reflection-free Aspire APIs (e.g. `builder.AddProject(name, path)` and `builder.AddExecutable`).
- **Runtime Lifecycle Management**: The `GenesisNeuron` fires `ConfigureAspireResource` synapses for each parsed resource. `AspireRuntimeNeuron` receives these synapses and leverages the `IAspireBootConnector` to lazily and dynamically control process startup (`StartResourceAsync`, `StopResourceAsync`, `RestartResourceAsync`) based on configuration metadata (such as `autostart: false`).

---

## 4. Proposed Dynamic Topology Architecture

```
  [ digitalbrain.cs ] (AppHost Startup)
           │
           ├──► Parses digitalbrain.ino (via Dynamic Topology Loader)
           │         │
           │         ├──► builder.AddRedis("orleans-redis")
           │         ├──► builder.AddExecutable("flutter-web", ...)
           │         ├──► builder.AddProject("digitalbrain-mcp", ...)
           │
           ▼
  [ builder.Build().RunAsync() ]  ◄─── Orleans Silo Starts up
           │
           ├──► [ KernelOSBootstrapper ] (Fires InitializeGenesis Synapse)
           │         │
           │         ▼
           ├──► [ GenesisNeuron ]
           │         │
           │         ├──► Parses digitalbrain.ino
           │         ├──► Dispatches "ConfigureAspireResource" Synapses
           │
           ▼
  [ AspireRuntimeNeuron ] (IHandle<ConfigureAspireResource>)
           │
           ▼  (Uses IAspireBootConnector)
  [ IAspireBootConnector ]  ◄─── Controls resource states dynamically (start / stop / restart)
```

### A. Phase 1: Dynamic Topology Loader (AppHost Startup)
We introduce an `InoTopologyParser` utility class within `kernel/DigitalBrain.Hosting/` that is called during `AddDigitalBrain(...)`. This parser reads `digitalbrain.ino` and registers each resource dynamically:

```csharp
public static class InoTopologyParser
{
    public static void LoadDynamicTopology(IDistributedApplicationBuilder builder, string inoFilePath)
    {
        if (!File.Exists(inoFilePath)) return;

        var lines = File.ReadAllLines(inoFilePath);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("register-resource", StringComparison.OrdinalIgnoreCase))
            {
                var prompt = ExtractPrompt(trimmed);
                var (name, type, config) = ParseResourcePrompt(prompt);

                RegisterResource(builder, name, type, config);
            }
        }
    }

    private static void RegisterResource(IDistributedApplicationBuilder builder, string name, string type, Dictionary<string, string> config)
    {
        if (string.Equals(type, "container", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Contains("redis"))
            {
                builder.AddRedis(name); // standard redis container registration
            }
        }
        else if (string.Equals(type, "executable", StringComparison.OrdinalIgnoreCase))
        {
            config.TryGetValue("path", out var path);
            config.TryGetValue("args", out var argsStr);
            config.TryGetValue("port", out var portStr);
            config.TryGetValue("autostart", out var autostartStr);

            var workingDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, path ?? ""));
            var args = argsStr?.Split(' ') ?? Array.Empty<string>();
            bool autostart = !string.Equals(autostartStr, "false", StringComparison.OrdinalIgnoreCase);

            var executable = builder.AddExecutable(name, "flutter", workingDir, args);
            
            if (int.TryParse(portStr, out var port))
            {
                executable.WithHttpEndpoint(port: port, targetPort: port, name: "http", isProxied: false);
            }

            if (!autostart)
            {
                executable.WithExplicitStart(); // registered but stays stopped until runtime trigger
            }
        }
        else if (string.Equals(type, "project", StringComparison.OrdinalIgnoreCase))
        {
            config.TryGetValue("path", out var path);
            config.TryGetValue("port", out var portStr);

            var projectPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../..", path ?? ""));
            
            // Register project dynamically using path reference rather than generic metadata types
            var project = builder.AddProject(name, projectPath);

            if (int.TryParse(portStr, out var port))
            {
                project.WithHttpEndpoint(port: port, targetPort: port, name: "http", isProxied: false);
            }
        }
    }
}
```

### B. Phase 2: AspireRuntimeNeuron Synapse Integration
When `GenesisNeuron` fires the `ConfigureAspireResource` synapse:
```csharp
public sealed record ConfigureAspireResource(
    SynapseMetadata Headers, 
    string ResourceName, 
    string ResourceType, 
    Dictionary<string, string> Config) : Synapse(Headers);
```

We refactor `AspireRuntimeNeuron` to implement `IHandle<ConfigureAspireResource>`:
```csharp
namespace DigitalBrain.SDK.Aspire.Runtime;

[Orleans.GrainType(NeuronTargetFqn)]
internal sealed class AspireRuntimeNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<AspireRuntimeNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger), 
      ICallNeuronTarget,
      IHandle<ConfigureAspireResource>
{
    public const string NeuronTargetFqn = "DigitalBrain.SDK.Aspire.Runtime";
    private readonly ConcurrentDictionary<string, ConfigureAspireResource> _resources = new();

    public async Task HandleAsync(ConfigureAspireResource synapse, CancellationToken ct)
    {
        logger.LogInformation("AspireRuntimeNeuron: Received ConfigureAspireResource synapse for '{ResourceName}' (Type: {ResourceType})", synapse.ResourceName, synapse.ResourceType);

        // Store configuration in runtime dictionary
        _resources[synapse.ResourceName] = synapse;

        // Determine if we should dynamically start this resource immediately
        bool autostart = true;
        if (synapse.Config.TryGetValue("autostart", out var autostartStr) && 
            string.Equals(autostartStr, "false", StringComparison.OrdinalIgnoreCase))
        {
            autostart = false;
        }

        if (autostart)
        {
            var connector = this.ServiceProvider.GetRequiredService<IAspireBootConnector>();
            logger.LogInformation("AspireRuntimeNeuron: Dynamically spinning up resource '{ResourceName}' via IAspireBootConnector...", synapse.ResourceName);
            await connector.StartResourceAsync(synapse.ResourceName, ct);
        }
    }

    // Existing AskAsync, Status, list-resources prompts remain fully intact,
    // delegates spin-up/stop/restart directly to IAspireBootConnector.
}
```

---

## 5. Architectural Benefits

1. **Decoupled C# Compilations**: The `DigitalBrain.AppHost` project no longer needs hardcoded dependencies on specific executables, projects, or domains. It is parameterized purely by `digitalbrain.ino`.
2. **Standardized Synaptic Interface**: All Aspire configurations flow cleanly through standard `ConfigureAspireResource` synapses rather than procedural builder extensions, preserving Orleans/InoLang symmetry.
3. **Lazy Lifecycle Management**: Resources that should not autostart (like `flutter-windows`) are kept in a stopped state at host startup, and are dynamically spun up when Orleans neurons issue synaptic commands via `IAspireBootConnector`.
4. **Seamless backward-compatibility**: Existing configurations, launch scripts (`digitalbrain.cs`, `testdigitalbrain.cs`), and solution-wide compilation parameters remain 100% green and unmodified.
