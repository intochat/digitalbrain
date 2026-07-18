# Codebase Analysis & Refactoring Design: Represent .NET Aspire Orchestration as AspireNeuron

**Prepared by**: Explorer 2 (Milestone 3)  
**Date**: May 26, 2026  
**Subject**: Codebase analysis under `sdk/DigitalBrain.SDK/Aspire/` and refactoring proposal to implement `IHandle<ConfigureAspireResource>` dynamically using `IAspireBootConnector`.

---

## 1. Executive Summary

This report delivers a comprehensive structural analysis of the .NET Aspire integration inside the DigitalBrain SDK. It details how the `AspireRuntimeNeuron` grain target is constructed, how it interfaces with the `IAspireBootConnector` system component, and designs the clean transition to a dynamic, event-driven model driven by the `ConfigureAspireResource` synapse.

To resolve the circular dependency between `DigitalBrain.Kernel` and `DigitalBrain.SDK`, we propose moving the definition of `ConfigureAspireResource` into `DigitalBrain.Kernel.Contracts` while retaining its namespace. We provide a complete refactoring patch for `AspireRuntimeNeuron` to implement `IHandle<ConfigureAspireResource>` and process topology configurations dynamically using `IAspireBootConnector`.

---

## 2. Codebase Sweep: Current Architecture

Our investigation scanned the files under `sdk/DigitalBrain.SDK/Aspire/`. Below is the mapping of components and their functions:

| Component | Path | Purpose |
|---|---|---|
| `IAspireBootConnector` | `sdk/DigitalBrain.SDK/Aspire/IAspireBootConnector.cs` | Defines the ABI interface to spawn clusters, install domains, and control resource lifecycle (start/stop/restart). |
| `AspireBootConnector` | `sdk/DigitalBrain.SDK/Aspire/AspireBootConnector.cs` | Implements `IAspireBootConnector`. Launches the `DigitalBrain.AppHost` process tree and delegates resource operations to the native `aspire` CLI. |
| `AspireRuntimeNeuron` | `sdk/DigitalBrain.SDK/Aspire/Runtime/AspireRuntimeNeuron.cs` | The Orleans grain implementing the SDK's steady-state `$aspire` neuron target. Translates text-based prompts into connector calls. |
| `AppStartedSignal` | `sdk/DigitalBrain.SDK/Aspire/AppStartedSignal.cs` | Ingress signal emitted when the Aspire cluster completes startup. |
| `AspireAppStartedEmitter` | `sdk/DigitalBrain.SDK/Aspire/Runtime/AspireAppStartedEmitter.cs` | Hosted service that triggers the `AppStarted` signal exactly once after silo boot. |
| `SdkAspireServiceCollectionExtensions` | `sdk/DigitalBrain.SDK/Aspire/Runtime/SdkAspireServiceCollectionExtensions.cs` | Registers the connector and hosted emitter into the silo's DI container. |

### 2.1 AspireRuntimeNeuron Construction Analysis
`AspireRuntimeNeuron` is constructed by the Orleans Silo using standard dependency injection.
- **Grain Identity**: Decorated with `[Orleans.GrainType("DigitalBrain.SDK.Aspire.Runtime")]`.
- **Inheritance & Contracts**: Inherits from the base `Neuron` class (which manages state persistence via `DurableGrain` and stream-based synapse journaling). Implements the public L3 execution contract `ICallNeuronTarget`.
- **Constructor Signature**:
  ```csharp
  internal sealed class AspireRuntimeNeuron(
      [Microsoft.Extensions.DependencyInjection.FromKeyedServices("incoming")] Orleans.Journaling.IDurableList<Synapse> incoming,
      [Microsoft.Extensions.DependencyInjection.FromKeyedServices("outgoing")] Orleans.Journaling.IDurableList<Synapse> outgoing,
      global::Orleans.IGrainFactory grains,
      global::Microsoft.Extensions.Logging.ILogger<AspireRuntimeNeuron> logger)
      : Neuron(incoming, outgoing, grains, logger), ICallNeuronTarget
  ```
- **Local State**: Maintains a private `ConcurrentDictionary<string, string> _registeredResources` mapping a resource name to its raw `.ino` string specification.

### 2.2 Integration with IAspireBootConnector
Currently, `AspireRuntimeNeuron` uses `IAspireBootConnector` inside its `AskAsync(string prompt)` method to handle text commands.
- It resolves the connector lazily via the grain's service provider rather than injecting it in the constructor:
  ```csharp
  var connector = this.ServiceProvider.GetRequiredService<IAspireBootConnector>();
  ```
- It maps the text prompts to connector methods:
  - `spawn-cluster {profile}` $\rightarrow$ `connector.SpawnClusterAsync(...)`
  - `restart resource {name}` / `reload assemblies` $\rightarrow$ `connector.RestartResourceAsync(...)`
  - `spin up resource {name}` $\rightarrow$ `connector.StartResourceAsync(...)`
  - `stop resource {name}` $\rightarrow$ `connector.StopResourceAsync(...)`

---

## 3. Circular Dependency Resolution

### 3.1 The Problem
- `DigitalBrain.Kernel` references `DigitalBrain.SDK` (which contains `AspireRuntimeNeuron.cs`).
- `ConfigureAspireResource` is defined in `kernel/DigitalBrain.Kernel/OS/OSSynapses.cs`.
- If `AspireRuntimeNeuron.cs` implements `IHandle<ConfigureAspireResource>`, the SDK project must import `DigitalBrain.Kernel`, creating a **circular project reference** (`Kernel` $\rightarrow$ `SDK` $\rightarrow$ `Kernel`), which will fail the compiler build.

### 3.2 The Solution
We must move `ConfigureAspireResource` out of `DigitalBrain.Kernel` and into `DigitalBrain.Kernel.Contracts` (which is already referenced by both projects).
- **Project**: Move definition to `DigitalBrain.Kernel.Contracts` under `Runtime/`.
- **Namespace**: Retain the namespace `DigitalBrain.Kernel.OS` so that existing code in `GenesisNeuron.cs` does not require namespace updates or import changes.

---

## 4. Refactoring Design & Proposed Patches

### 4.1 Step 1: Relocate the Synapse Definition
Remove `ConfigureAspireResource` from `kernel/DigitalBrain.Kernel/OS/OSSynapses.cs` (lines 17-18) and create `kernel/DigitalBrain.Kernel.Contracts/Runtime/ConfigureAspireResource.cs`:

```csharp
using DigitalBrain.Core.Neurons;
using System.Collections.Generic;

namespace DigitalBrain.Kernel.OS;

[GenerateSerializer]
public sealed record ConfigureAspireResource(
    SynapseMetadata Headers,
    string ResourceName,
    string ResourceType,
    Dictionary<string, string> Config)
    : Synapse(Headers);
```

### 4.2 Step 2: Implement the Handler on `AspireRuntimeNeuron`
Refactor `AspireRuntimeNeuron.cs` to implement `IHandle<ConfigureAspireResource>` and process the configuration dynamically:

```csharp
using DigitalBrain.Core.Neurons;
using DigitalBrain.Kernel.OS; // Import the shared synapse namespace

namespace DigitalBrain.SDK.Aspire.Runtime;

[Orleans.GrainType(NeuronTargetFqn)]
internal sealed class AspireRuntimeNeuron(
    [Microsoft.Extensions.DependencyInjection.FromKeyedServices("incoming")] Orleans.Journaling.IDurableList<Synapse> incoming,
    [Microsoft.Extensions.DependencyInjection.FromKeyedServices("outgoing")] Orleans.Journaling.IDurableList<Synapse> outgoing,
    global::Orleans.IGrainFactory grains,
    global::Microsoft.Extensions.Logging.ILogger<AspireRuntimeNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger), ICallNeuronTarget, IHandle<ConfigureAspireResource>
{
    // Existing fields and AskAsync implementation remain here...

    public async Task HandleAsync(ConfigureAspireResource synapse, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "AspireRuntimeNeuron received ConfigureAspireResource synapse: Name={Name}, Type={Type}",
            synapse.ResourceName, synapse.ResourceType);

        // 1. Sync local registry state to preserve AskAsync("list-resources") behavior
        var configStr = string.Join(" ", synapse.Config.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        var spec = $"register-resource {synapse.ResourceName} type:{synapse.ResourceType} {configStr}".Trim();
        _registeredResources[synapse.ResourceName] = spec;

        logger.LogInformation("AspireRuntimeNeuron: Registered configured resource '{Name}' locally.", synapse.ResourceName);

        // 2. Resolve the Aspire boot connector
        var connector = this.ServiceProvider.GetRequiredService<IAspireBootConnector>();

        // 3. Process the resource dynamically
        bool autostart = true;
        if (synapse.Config.TryGetValue("autostart", out var autostartVal) &&
            bool.TryParse(autostartVal, out var parsedAutostart))
        {
            autostart = parsedAutostart;
        }

        if (autostart)
        {
            logger.LogInformation("AspireRuntimeNeuron: Spin up resource '{Name}' automatically...", synapse.ResourceName);
            var result = await connector.StartResourceAsync(synapse.ResourceName, cancellationToken);
            logger.LogInformation("AspireRuntimeNeuron: Auto-start result for '{Name}': {Result}", synapse.ResourceName, result);
        }
        else
        {
            logger.LogInformation("AspireRuntimeNeuron: Resource '{Name}' has autostart:false. Skipping auto-start.", synapse.ResourceName);
        }
    }
}
```

---

## 5. Dynamic Resource Processing Recommendation

When the `.ino` file is parsed, resources are declared with distinct types and configuration mappings. We recommend standardizing how these parameters are processed using `IAspireBootConnector`:

1. **Local State Synchronization**: `_registeredResources` should always be kept up-to-date by parsing the synapse payload back into a canonical specification string. This keeps `AskAsync("list-resources")` accurate.
2. **Lifecycle Lifecycle Actions**:
   - **`autostart`**: Extracted from the config dictionary (default: `true`). If true, triggers `connector.StartResourceAsync` inside the handler. If false, leaves the resource dormant until an explicit `AskAsync("spin up resource {name}")` is called.
   - **Port Bindings & Ports**: Config keys such as `port` (e.g. `59330`, `5800`) are mapped as process arguments or environment variables.
   - **Path & Arguments**: Keys `path` (e.g. `../../UI/flutter`) and `args` (e.g. `run -d web-server --release`) specify how the resource's underlying process is booted by the connector.
3. **Connector CLI Invocation**: Under the hood, `IAspireBootConnector` delegates lifecycle commands directly to the Aspire developer dashboard control loops:
   ```powershell
   aspire resource <ResourceName> <Command> --apphost "<AppHostPath>" --non-interactive
   ```
   This ensures that the central Orleans cluster stays isolated from direct operating system process tracking, allowing Aspire's developer tools to handle the heavy lifting.

---

## 6. Verification and Test Plan

To verify this design independently:
1. **Compilation Check**: Move `ConfigureAspireResource` as described. Build the solution using `dotnet build` to ensure no circular references occur.
2. **Dynamic Synapse Test**: Inject a mock `ConfigureAspireResource` synapse targeting `sys.aspire` into the stream and check that `AspireRuntimeNeuron` processes it, updates its internal catalog, and invokes the mock `IAspireBootConnector`.
3. **BDD Scenario**: Verify that when `GenesisNeuron` reads `digitalbrain.ino`, the `ConfigureAspireResource` synapse is successfully fired and handled, satisfying the BDD scenario:
   ```ino
   scenario "system boots and registers core distributed topography"
   ```
