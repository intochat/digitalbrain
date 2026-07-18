# Analysis Report: .NET Aspire Orchestration & Genesis-Aspire Integration

**Milestone 3**: Represent .NET Aspire Orchestration as AspireNeuron  
**Explorer**: Explorer 3  
**Status**: Read-Only Analysis Complete  
**Date**: 2026-05-26  

---

## 1. Executive Summary

This report sweeps and analyzes the boot orchestration flow between `GenesisNeuron` and `.NET Aspire` component structures. We examine the definitions in `OSSynapses.cs` and `GenesisNeuron.cs`, detail how resource registration prompts from `digitalbrain.ino` are parsed and dispatched, expose a critical routing mismatch in the current codebase, and propose a clean, comprehensive Integration Plan to bridge `GenesisNeuron` and `AspireRuntimeNeuron`.

---

## 2. Core Codebase Sweep & Analysis

### A. Definition of `ConfigureAspireResource`
The synapse `ConfigureAspireResource` is defined in `kernel/DigitalBrain.Kernel/OS/OSSynapses.cs` (lines 18–19):
```csharp
[GenerateSerializer]
public sealed record ConfigureAspireResource(
    SynapseMetadata Headers, 
    string ResourceName, 
    string ResourceType, 
    Dictionary<string, string> Config
) : Synapse(Headers);
```
- **`Headers`**: Canonical Orleans synapse metadata holding routing information (Caller, Receiver, CorrelationId, CausationId, etc.).
- **`ResourceName`**: The identifier of the Aspire resource (e.g., `"orleans-redis"`).
- **`ResourceType`**: The resource category (e.g., `"container"`, `"executable"`, `"project"`).
- **`Config`**: Key-value pair configuration parameters (e.g., `port: "59330"`, `path: "../../UI/flutter"`).

---

### B. Prompt Parsing & Dispatch from `digitalbrain.ino`
`GenesisNeuron.cs` is responsible for parsing topology lines inside `digitalbrain.ino` and dispatching them.

#### 1. InoLang Topography Specification (`digitalbrain.ino`)
Lines 30–48 in `digitalbrain.ino` define dynamic Aspire resources:
```ino
    # Core database clustering
    ask aspire to "register-resource orleans-redis type:container port:59330"
    ...
    # Personal assistant visual environments
    ask aspire to "register-resource flutter-web type:executable path:../../UI/flutter args:run -d web-server --release port:5800"
    ...
```

#### 2. Parsing in `GenesisNeuron.cs`
During the boot flow, `GenesisNeuron` reads `digitalbrain.ino` line-by-line:
1. **Extraction**: If a line contains `"register-resource"`, it extracts the prompt from within the double quotes:
   ```csharp
   string prompt = trimmed;
   int quoteStart = trimmed.IndexOf('"');
   int quoteEnd = trimmed.LastIndexOf('"');
   if (quoteStart != -1 && quoteEnd > quoteStart)
   {
       prompt = trimmed[(quoteStart + 1)..quoteEnd];
   }
   ```
2. **Dynamic Registration**: It retrieves the `ICallNeuronTarget` grain with identity `"DigitalBrain.SDK.Aspire.Runtime"` and calls:
   ```csharp
   await aspireNeuron.AskAsync(prompt);
   ```
3. **Structured Parameter Extraction (`ParseRegisterResource`)**:
   It lowers the prompt string (e.g., `"register-resource orleans-redis type:container port:59330"`) into structured fields:
   - Removes the `"register-resource "` prefix.
   - Splits on space to find the **ResourceName** (`"orleans-redis"`).
   - Scans the remainder for known key suffixes: `"type:"`, `"port:"`, `"path:"`, `"args:"`, `"autostart:"`.
   - Iterates through the discovered keys to isolate parameter values and populates the `ResourceType` and `Config` dictionary.

---

### C. The Critical Stream Routing Bug
A critical design mismatch exists in how the synapse header is constructed in `GenesisNeuron.cs` (lines 93–96):
```csharp
var header = SynapseFactory.CreateHeader<IGenesisNeuron, IGenesisNeuron>(
    new NeuronId("sys.genesis"),
    new NeuronId("sys.aspire")
);
```
#### Trace Chain & Consequence:
1. **Generic Resolution**: `SynapseFactory.CreateHeader<TCaller, TReceiver>` resolves `TReceiver` as `IGenesisNeuron`.
2. **Metadata Headers**: This sets `ReceiverNeuronType = "IGenesisNeuron"`.
3. **Stream Routing**: When `FireSynapseAsync` executes, it routes point-to-point stream delivery via:
   ```csharp
   var receiverStream = streamProvider.GetStream<Synapse>(
       StreamId.Create(receiverType, synapse.ReceiverNeuronId));
   ```
   For `sys.aspire`, this stream is created under namespace `"IGenesisNeuron"` with key deterministic `Guid` for `"sys.aspire"`.
4. **Delivery Failure**: Grains targeting `sys.aspire` are supposed to land on `AspireRuntimeNeuron`. However:
   - `AspireRuntimeNeuron` does not subscribe to the `"IGenesisNeuron"` stream namespace.
   - `AspireRuntimeNeuron` implements `ICallNeuronTarget`, not `IGenesisNeuron`.
   - Result: **The `ConfigureAspireResource` synapse is entirely misrouted** and never delivered to `AspireRuntimeNeuron`.

---

## 3. Integration Plan & Recommendations

To successfully bridge `GenesisNeuron` and `AspireRuntimeNeuron` for Milestones 3 and beyond, we recommend the following five integration steps:

### Step 1: Define `IAspireRuntimeNeuron` Interface
Create a clean public marker interface to represent the Aspire runtime/orchestrator neuron. This will be shared and can reside in `sdk/DigitalBrain.SDK/Aspire/Runtime/` or `kernel/DigitalBrain.Kernel.Contracts/Runtime/`:
```csharp
namespace DigitalBrain.SDK.Aspire.Runtime;

using DigitalBrain.Core.Neurons;

/// <summary>
/// Public marker interface representing the Aspire Orchestration and Runtime Neuron.
/// </summary>
public interface IAspireRuntimeNeuron : INeuron;
```

---

### Step 2: Implement Marker and Synapse Handler in `AspireRuntimeNeuron`
Update `sdk/DigitalBrain.SDK/Aspire/Runtime/AspireRuntimeNeuron.cs` to:
1. Implement `IAspireRuntimeNeuron` and `IHandle<ConfigureAspireResource>`.
2. Add the Orleans `[ImplicitStreamSubscription]` attribute pointing to `nameof(IAspireRuntimeNeuron)`.
3. Process incoming synapses to dynamically manage resource configurations.

```csharp
[Orleans.GrainType(NeuronTargetFqn)]
[Orleans.ImplicitStreamSubscription(nameof(IAspireRuntimeNeuron))] // Dynamic routing bridge
internal sealed class AspireRuntimeNeuron(
    [Microsoft.Extensions.DependencyInjection.FromKeyedServices("incoming")] Orleans.Journaling.IDurableList<Synapse> incoming,
    [Microsoft.Extensions.DependencyInjection.FromKeyedServices("outgoing")] Orleans.Journaling.IDurableList<Synapse> outgoing,
    global::Orleans.IGrainFactory grains,
    global::Microsoft.Extensions.Logging.ILogger<AspireRuntimeNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger), 
      ICallNeuronTarget, 
      IAspireRuntimeNeuron, 
      IHandle<ConfigureAspireResource>
{
    public const string NeuronTargetFqn = "DigitalBrain.SDK.Aspire.Runtime";
    private readonly ConcurrentDictionary<string, string> _registeredResources = new(StringComparer.OrdinalIgnoreCase);

    // Dynamic point-to-point synapse subscription handler
    public Task HandleAsync(ConfigureAspireResource synapse, CancellationToken ct)
    {
        logger.LogInformation("AspireRuntimeNeuron: Received ConfigureAspireResource synapse. Resource={Name}, Type={Type}", 
            synapse.ResourceName, synapse.ResourceType);

        // Store config and/or invoke underlying AspireBootConnector commands dynamically
        var specBuilder = $"{synapse.ResourceName} type:{synapse.ResourceType}";
        foreach (var kvp in synapse.Config)
        {
            specBuilder += $" {kvp.Key}:{kvp.Value}";
        }

        _registeredResources[synapse.ResourceName] = specBuilder;
        return Task.CompletedTask;
    }

    public async Task<string> AskAsync(string prompt)
    {
        // Existing dynamic RPC prompt matching remains here...
    }
}
```

---

### Step 3: Correct Synapse Header in `GenesisNeuron.cs`
Update the synapse construction inside `GenesisNeuron.cs` to target `IAspireRuntimeNeuron` as `TReceiver`. This ensures the stream namespace is resolved as `"IAspireRuntimeNeuron"`:
```csharp
// Before:
// var header = SynapseFactory.CreateHeader<IGenesisNeuron, IGenesisNeuron>(new NeuronId("sys.genesis"), new NeuronId("sys.aspire"));

// After:
var header = SynapseFactory.CreateHeader<IGenesisNeuron, IAspireRuntimeNeuron>(
    new NeuronId("sys.genesis"),
    new NeuronId("sys.aspire")
);
```

---

### Step 4: Correct AI Subsystem Routing in `GenesisNeuron.cs`
Symmetric to the Aspire bug, the `ConfigureAiSubsystem` synapse is dispatched to `sys.ai` but also uses `<IGenesisNeuron, IGenesisNeuron>` as its types:
```csharp
// Before:
// var aiHeader = SynapseFactory.CreateHeader<IGenesisNeuron, IGenesisNeuron>(new NeuronId("sys.genesis"), new NeuronId("sys.ai"));

// After:
// Define `IAiNeuron : INeuron` in the AI contracts assembly and dispatch:
var aiHeader = SynapseFactory.CreateHeader<IGenesisNeuron, IAiNeuron>(
    new NeuronId("sys.genesis"),
    new NeuronId("sys.ai")
);
```

---

### Step 5: Verification Strategy & Integration Test
To verify the routing is active and correct:
1. Introduce a test in `DigitalBrain.Test/Aspire/` that mimics the startup boot configuration.
2. Verify that the stream receives the `ConfigureAspireResource` synapse and `AspireRuntimeNeuron` completes handling.
3. run the project test command to verify the assembly scans are green:
   ```powershell
   dotnet test --filter "FullyQualifiedName~Aspire"
   ```

---

## 4. Caveats & Observations
- **Autostart Constraints**: Some resources are labeled `autostart:false` (such as `flutter-windows`). The connector handler must check the config dictionary and avoid auto-spinning them unless explicitly commanded.
- **Port Conflicts**: Multiple local microservices are registering distinct local ports (`59330`, `5800`, `5821`, `5810`). Ensure local system firewalls or developer setups do not block these.
- **No Source Code Modifications**: This report is read-only. No source files have been altered.

---
*End of Report.*
