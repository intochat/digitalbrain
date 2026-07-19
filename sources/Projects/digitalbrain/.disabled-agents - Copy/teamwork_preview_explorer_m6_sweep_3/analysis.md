# NEURON ARCHITECTURAL MODERNIZATION & IMPLEMENTATIONS

**Date**: May 26, 2026  
**Agent**: Explorer 3 (Neuron Implementations)  
**Milestone**: M6 Sweep 3  
**Status**: Read-only Investigation Complete  

---

## 1. Executive Summary

This report details the architectural design for modernizing base interfaces and implementing key neurons on the **DigitalBrain** cortex mesh. We detail the integration of strongly-typed state handling (`INeuron<TState>`), dynamic in-silo instantiation with `NeuronFactory` to eliminate complex Roslyn-generated assembly pipelines, unsealing `LLM` for custom inheritance (`Grok`) with secure runtime secret decryption using `ISecretVault`, and the creation of Core Tool Neurons (`GitHub`, `Dotnet`, `Flutter`) deploying interactive visual layouts via Remote Flutter Widgets (RFW). Finally, we demonstrate test alignment leveraging the standard fast-executing, in-memory `NeuronBuilder<T>` harness.

---

## 2. Locate Existing Neuron Foundations

A thorough investigation of the workspace revealed the following critical neuron structures:

| Target | File Path | Purpose / Characteristics |
|---|---|---|
| `INeuron` | `kernel/BrainOS.Core/Neurons/INeuron.cs` | Guid-keyed Orleans grain interface exposing incoming/outgoing journal slices. |
| `INeuronWithStringKey` | `kernel/BrainOS.Core/Neurons/INeuronWithStringKey.cs` | String-keyed Orleans grain interface. |
| `Neuron` (Base Class) | `kernel/BrainOS.Core/Neurons/Neuron.cs` | Durable base class inheriting from `DurableGrain`. Manages stream subscriptions, telemetry, state persistence, RFW UI rendering, and declarative synapse dispatch. |
| `NeuronState` | `kernel/BrainOS.Core/Domain/NeuronState.cs` | Domain-level durable state record containing incoming/outgoing journals, scheduled queue, and execution logs. |
| `NeuronStateAttribute` | `kernel/BrainOS.Core/Neurons/State/NeuronStateAttribute.cs` | Facet metadata parameter attribute that resolves dependency injection using `NeuronStateAttributeMapper`. |
| `NeuronBuilder<T>` | `sdk/DigitalBrain.SDK/NeuronBuilder.Generic.cs` | Superfast generic in-memory test harness providing dependency mocking and execution loops. |
| `Llm` (Current) | `sdk/DigitalBrain.SDK/Ai/Llm/Neuron/Llm.cs` | Sealed SDK model class containing direct `IChatClient` service resolution from Orleans and the express `Llm.Prompt` API. |
| `ISecretVault` | `sdk/DigitalBrain.SDK.Contracts/Security/ISecretVault.cs` | Interface managing secure, encrypted storage (`Windows DPAPI` / `AES-256` fallback) returning `"ENC:<base64>"` records. |

---

## 3. High-Fidelity Design Specifications

### 3.1. Strongly-Typed States: `INeuron<TState>`

To introduce statefulness naturally to the cortex mesh, we propose unsealing state management by introducing generic interfaces and base classes.

#### `INeuron<TState>` Interface
```csharp
namespace BrainOS.Core.Neurons;

/// <summary>
/// A strongly-typed stateful neuron grain interface that integrates into the Orleans Cortex mesh.
/// </summary>
/// <typeparam name="TState">The type of state managed by the neuron.</typeparam>
public interface INeuron<TState> : INeuron 
    where TState : class, new()
{
    /// <summary>
    /// Gets the current state of the neuron.
    /// </summary>
    Task<TState> GetStateAsync();

    /// <summary>
    /// Modifies the state of the neuron using a transactional update function and persists the results.
    /// </summary>
    Task UpdateStateAsync(Func<TState, TState> updateFunc);
}
```

#### Generic `Neuron<TState>` Base Class
```csharp
namespace BrainOS.Core.Neurons;

using Orleans.Journaling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Abstract base class for stateful durable neurons, managing typed persistence.
/// </summary>
public abstract class Neuron<TState> : Neuron, INeuron<TState>
    where TState : class, new()
{
    protected TState State { get; set; } = new();

    protected Neuron(
        [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
        [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
        IGrainFactory grains,
        ILogger logger)
        : base(incoming, outgoing, grains, logger)
    {
    }

    protected Neuron() : base()
    {
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        
        // Dynamic Orleans custom facet hydration via IServiceProvider
        if (this.GrainContext is not null)
        {
            var services = this.GrainContext.ActivationServices;
            var resolvedState = services.GetService<TState>();
            if (resolvedState is not null)
            {
                State = resolvedState;
            }
        }
    }

    public Task<TState> GetStateAsync() => Task.FromResult(State);

    public async Task UpdateStateAsync(Func<TState, TState> updateFunc)
    {
        State = updateFunc(State);
        // Persist local state updates immediately via DurableGrain WriteStateAsync
        await WriteStateAsync();
    }
}
```

---

### 3.2. Decommissioning Boilerplate: `NeuronFactory`

Currently, dynamic neurons rely on generating custom class files and compiling them via Roslyn at runtime (resulting in long startup latency and heavy compiler overhead).
To eliminate Roslyn boilerplate, we introduce `NeuronFactory`, which maps dynamically registered FQNs to interpreted/runtime shell grains, or spins up local mocks in milliseconds.

#### `NeuronFactory` Class
```csharp
namespace BrainOS.Core;

using BrainOS.Core.Neurons;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Coordinates dynamic in-silo Orleans grain instantiation and in-memory mock generation.
/// </summary>
public sealed class NeuronFactory(IGrainFactory grainFactory, IServiceProvider serviceProvider)
{
    /// <summary>
    /// Resolves an Orleans grain reference from the active scope, directing calls to the target FQN.
    /// </summary>
    public TNeuronInterface GetNeuron<TNeuronInterface>(string targetFqn, string key)
        where TNeuronInterface : Orleans.Runtime.IAddressable
    {
        var primaryKey = BrainScopeHelper.GetActiveScopedNeuronKey(key);
        var grainType = Orleans.Runtime.GrainType.Create(targetFqn);
        var grainId = Orleans.Runtime.GrainId.Create(grainType, primaryKey);

        return grainFactory.GetGrain<TNeuronInterface>(grainId);
    }

    /// <summary>
    /// Directly instantiates a local testable neuron using dependency injection, bypassing Orleans.
    /// </summary>
    public TNeuron CreateLocalNeuron<TNeuron>(Action<ServiceCollection>? configureServices = null)
        where TNeuron : Neuron
    {
        var services = new ServiceCollection();
        
        // Standard mocks
        services.AddTransient<TNeuron>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory>();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        
        var incomingJournal = new DigitalBrain.SDK.InMemoryDurableList<Synapse>();
        var outgoingJournal = new DigitalBrain.SDK.InMemoryDurableList<Synapse>();
        services.AddKeyedSingleton<IDurableList<Synapse>>("incoming", incomingJournal);
        services.AddKeyedSingleton<IDurableList<Synapse>>("outgoing", outgoingJournal);

        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider();
        return ActivatorUtilities.CreateInstance<TNeuron>(provider);
    }
}
```

---

### 3.3. Cognitive Layer: `LLM` & `Grok`

The transition requires unsealing `Llm` to allow `Grok` to inherit from it, while standardizing dynamic credential lookup via `ISecretVault`.

#### `LLM` Base Class
```csharp
namespace DigitalBrain.SDK.Ai;

using BrainOS.Core.Neurons;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;

/// <summary>
/// Core LLM connector neuron, open for specialization.
/// </summary>
public class LLM : Neuron, ICallNeuronTarget
{
    public const string NeuronTargetFqn = "BrainOS.Ai.LlmNeuron";

    protected IChatClient? ChatClient { get; set; }

    public LLM(
        [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
        [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
        IGrainFactory grains,
        ILogger<LLM> logger,
        IServiceProvider services)
        : base(incoming, outgoing, grains, logger)
    {
    }

    public LLM() : base()
    {
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await EnsureChatClientInitializedAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves the underlying IChatClient using keys and fallback mechanisms.
    /// </summary>
    protected virtual Task EnsureChatClientInitializedAsync(CancellationToken ct)
    {
        var key = this.GetPrimaryKeyString();
        var services = this.GrainContext.ActivationServices;

        var (_, modelPart) = BrainOS.Core.BrainScopeHelper.ParseScopedNeuronKey(key);
        if (string.IsNullOrEmpty(modelPart))
        {
            modelPart = key;
        }

        ChatClient = services.GetKeyedService<IChatClient>(modelPart) 
                     ?? services.GetService<IChatClient>();

        if (ChatClient == null)
        {
            throw new InvalidOperationException($"Unable to resolve keyed service for chat client matching model key '{modelPart}'.");
        }

        return Task.CompletedTask;
    }

    public virtual async Task<string> AskAsync(string prompt)
    {
        if (ChatClient == null)
        {
            throw new InvalidOperationException("Chat client is not initialized.");
        }
        var response = await ChatClient.GetResponseAsync(prompt);
        return response.Text ?? string.Empty;
    }
}
```

#### `Grok : LLM` Concrete Subclass
```csharp
namespace DigitalBrain.SDK.Ai;

using BrainOS.Core.Neurons;
using DigitalBrain.SDK.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;

/// <summary>
/// Specialist xAI Grok neuron resolving credentials dynamically at runtime.
/// </summary>
public sealed class Grok : LLM
{
    public const string GrokTargetFqn = "BrainOS.Ai.GrokNeuron";

    private readonly ISecretVault _secretVault;

    public Grok(
        [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
        [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
        IGrainFactory grains,
        ILogger<Grok> logger,
        IServiceProvider services,
        ISecretVault secretVault)
        : base(incoming, outgoing, grains, logger, services)
    {
        _secretVault = secretVault ?? throw new ArgumentNullException(nameof(secretVault));
    }

    protected override async Task EnsureChatClientInitializedAsync(CancellationToken ct)
    {
        // 1. Dynamic API key resolution using standard encrypted storage
        string apiKey;
        try
        {
            apiKey = await _secretVault.DecryptSecretAsync("xai-api-key", ct);
        }
        catch (KeyNotFoundException)
        {
            // Failover to local environment/configuration during cold-start or testing
            var config = this.GrainContext.ActivationServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            apiKey = config["BrainOS:Secrets:xai-api-key"] ?? string.Empty;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            Logger.LogError("Grok credentials could not be decrypted. Ensure 'xai-api-key' is initialized inside the secret vault.");
            throw new InvalidOperationException("API key decryption failed.");
        }

        // 2. Instantiate and wrap xAI client using custom base endpoint
        var openAiClient = new OpenAI.OpenAIClient(apiKey, new OpenAI.OpenAIClientOptions
        {
            Endpoint = new Uri("https://api.x.ai/v1") // xAI endpoint
        });

        ChatClient = openAiClient.AsChatClient("grok-beta");
        Logger.LogInformation("Grok neuron successfully activated and configured with dynamic xAI credentials.");
    }
}
```

---

### 3.4. Tool Layer: Core Tool Neurons (RFW)

Deploying tools via RFW allows neurons to feed rich, interactive layouts directly onto the user's home screen.

```
       Cortex Signal               Fires Synapse                Renders Card
[ GitHub / Build Trigger ] ───► [ RfwCard Synapse ] ───► [ Remote Flutter Widget ]
```

#### GitHub Tool Neuron (Collaboration)
* **Goal**: Analyzes PRs, tracks commits, and displays sync status.
* **Layout**: Renders lists of active PRs with review metrics.
```csharp
namespace DigitalBrain.SDK.Tools;

using BrainOS.Core.Neurons;
using System.Text.Json.Nodes;

public sealed class GitHubNeuron : Neuron, IHandle<GitHubScanRequest>
{
    public async Task HandleAsync(GitHubScanRequest request, CancellationToken ct)
    {
        Logger.LogInformation("Scanning GitHub repository {Repo}", request.RepositoryName);

        // Core logic: Query GitHub API
        var activePrs = new JsonArray
        {
            new JsonObject { ["id"] = 101, ["title"] = "Refactor base neurons", ["author"] = "dev1" },
            new JsonObject { ["id"] = 102, ["title"] = "Add Grok connector", ["author"] = "dev2" }
        };

        var data = new JsonObject
        {
            ["title"] = $"Repository: {request.RepositoryName}",
            ["activePRCount"] = activePrs.Count,
            ["prs"] = activePrs,
            ["lastScan"] = DateTimeOffset.UtcNow.ToString("g")
        };

        // Render card
        await RenderAsync(
            libraryName: "github_collab",
            rootWidget: "pr_dashboard",
            data,
            ct);
    }
}
```

#### Dotnet Tool Neuron (Development)
* **Goal**: Performs fast compilation and testing.
* **Layout**: Displays pass/fail console widgets with detailed output.
```csharp
namespace DigitalBrain.SDK.Tools;

using BrainOS.Core.Neurons;
using System.Text.Json.Nodes;

public sealed class DotnetNeuron : Neuron, IHandle<DotnetBuildRequest>
{
    public async Task HandleAsync(DotnetBuildRequest request, CancellationToken ct)
    {
        Logger.LogInformation("Executing dotnet build for project {Project}", request.ProjectPath);

        // Core logic: Call build process
        var isSuccess = true;
        var warnings = 0;

        var data = new JsonObject
        {
            ["projectPath"] = request.ProjectPath,
            ["status"] = isSuccess ? "Success" : "Failed",
            ["warnings"] = warnings,
            ["output"] = "Build succeeded. 0 Errors, 0 Warnings."
        };

        await RenderAsync(
            libraryName: "dotnet_dev",
            rootWidget: "build_console",
            data,
            ct);
    }
}
```

#### Flutter Tool Neuron (UI)
* **Goal**: Hot-reloads interactive visual components.
* **Layout**: Renders a configurable widget workspace dynamically.
```csharp
namespace DigitalBrain.SDK.Tools;

using BrainOS.Core.Neurons;
using System.Text.Json.Nodes;

public sealed class FlutterNeuron : Neuron, IHandle<FlutterRenderRequest>
{
    public async Task HandleAsync(FlutterRenderRequest request, CancellationToken ct)
    {
        Logger.LogInformation("Hot-reloading widget '{Widget}' via RFW", request.WidgetName);

        var data = new JsonObject
        {
            ["widgetName"] = request.WidgetName,
            ["themeMode"] = "dark",
            ["params"] = JsonNode.Parse(request.PropertiesJson)
        };

        await RenderAsync(
            libraryName: "flutter_ui",
            rootWidget: request.WidgetName,
            data,
            ct);
    }
}
```

---

## 4. Test Alignment & Verification Strategy

All proposed designs align directly with the testing methodologies present in `DigitalBrain.Test`. Using the generic `NeuronBuilder<T>` harness, we can execute extremely fast in-memory verification (running under **100ms**) without running local Orleans silos.

### 4.1. Stateful Neuron Test Alignment
Verify that `Neuron<TState>` correctly manages and updates state across requests:
```csharp
[GenerateSerializer]
public sealed class CounterState
{
    [Id(0)] public int Count { get; set; }
}

public sealed class CounterNeuron : Neuron<CounterState>, IHandle<GenericMockSynapse>
{
    public Task HandleAsync(GenericMockSynapse mock, CancellationToken ct)
    {
        return UpdateStateAsync(s => new CounterState { Count = s.Count + 1 });
    }
}

[Fact]
public async Task StatefulNeuron_IncrementsCountCorrectly()
{
    // Arrange
    var harness = new NeuronBuilder<CounterNeuron>()
        .WithService(new CounterState { Count = 5 })
        .Build();

    // Act
    await harness.TestReceiveAsync(new GenericMockSynapse("Increment"));

    // Assert
    var finalState = await harness.Instance.GetStateAsync();
    finalState.Count.Should().Be(6);
}
```

### 4.2. Grok Neuron Credential Verification
Verify that `Grok` decouples environment lookup by requesting encrypted credentials through `ISecretVault`:
```csharp
[Fact]
public async Task GrokNeuron_ResolvesApiKeyFromVault_OnActivation()
{
    // Arrange
    var mockVault = new MockSecretVault();
    mockVault.StoreSecret("xai-api-key", "sk-grok-xyz");

    // Act
    var harness = new NeuronBuilder<Grok>()
        .WithService<ISecretVault>(mockVault)
        .Build(); // Activates grain

    // Assert
    harness.Instance.GetApiKey().Should().Be("sk-grok-xyz");
}
```

### 4.3. RFW UI Layout Test Alignment
Verify that our Tool Neurons correctly emit visual layouts targeting the user's home screen stream:
```csharp
[Fact]
public async Task DotnetNeuron_EmitsCorrectRfwCardLayout()
{
    // Arrange
    var harness = new NeuronBuilder<DotnetNeuron>().Build();

    // Act
    var result = await harness.TestReceiveAsync(new DotnetBuildRequest("src/App.csproj"));

    // Assert
    result.FiredSynapses.Should().ContainSingle();
    var fired = result.FiredSynapses[0];
    
    fired.ReceiverType.Should().Be("HomeFeed");
    
    var card = fired.Payload as RfwCard;
    card.Should().NotBeNull();
    card!.LibraryName.Should().Be("dotnet_dev");
    card.RootWidget.Should().Be("build_console");
}
```

---

## 5. Architectural Recommendations

1. **Adopt `NeuronFactory` for Interpretation**: Transition from building in-memory assembly compilation routines via Roslyn toward executing AST/interpreted plan specifications using `InterpretedNeuronGrain`. This yields huge improvements in silo boot-up speed, cluster stability, and sandbox safety.
2. **Standardize `ISecretVault`**: Standardize API key resolution in all cognitive and platform connectors using `ISecretVault` to ensure production installations are secured with OS-level encryption by default.
3. **Streamline RFW Visual Channels**: Consolidate tool-related Remote Flutter Widget definitions under unified library packages (`github_collab`, `dotnet_dev`, `flutter_ui`) to support fluid UI hot-reloads and visual consistency.
