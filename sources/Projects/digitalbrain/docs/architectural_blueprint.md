# DigitalBrain Architectural Blueprint: Substrate Boundary & Extension Guide

This document defines the architectural boundaries between the open-source substrate and private proprietary layers of the DigitalBrain ecosystem, followed by a step-by-step extension guide for developing, registering, and distributing custom **Neurons** and **Synapses**.

---

## 1. Architectural & Repository Boundaries

To maintain a healthy, extensible core while protecting commercial integrations and enterprise connectors, the codebase is strictly partitioned into two logical layers:

### A. The Open-Source Substrate (`DigitalBrain.Core` & `DigitalBrain.InoLang`)
The open-source substrate consists of the fundamental SDK interfaces, grammar definitions, compiler systems, and the lightweight virtual actor runtime backbone. 

- **`inolang/DigitalBrain.InoLang/`**
  - **Purpose**: Core compiler services, lexing, parsing, and semantic validation of `.ino` DSL source files.
  - **Contents**: The InoLang parser, AST (Abstract Syntax Tree) generation, and intermediate code linking.
- **`kernel/DigitalBrain.Core/`**
  - **Purpose**: The base runtime interfaces and state containers for the virtual actor substrate.
  - **Contents**: 
    - Base interfaces: `INeuron`, `ISynapse`, `INeuronRegistry`.
    - Key abstractions: `Neuron`, `Synapse`, `NeuronState`, `NeuronTelemetry`.
    - Core diagnostics and metrics (`DigitalBrainTelemetry`).
- **`kernel/DigitalBrain.Domains.Dynamic.Contracts/`**
  - **Purpose**: Unified contracts for standard neuron structures, including the newly introduced parallel `Swarm` contracts (`ISwarmCoordinatorNeuron`, `ISwarmWorkerNeuron`, and `SwarmTrigger` / `SwarmTaskCompleted` synapses).

### B. Closed-Source Proprietary Connector Packages (`DigitalBrain.Kernel` & `DigitalBrain.SDK`)
These packages provide production-grade host environments, enterprise security primitives, and deep integrations with physical operating systems, heavy databases, and AI clients.

- **`kernel/DigitalBrain.Kernel/`**
  - **Purpose**: Private Orleans cluster hosting, dynamic interpreted execution runtime, and the local secure storage vault.
  - **Contents**:
    - Orleans Silo configurations, clustering, and lifetime hooks.
    - Encrypted credentials storage (`ISecretVault`).
    - Real-world storage connectors, including PostgreSQL bridging and dynamic schema table generation via Orleans Keyed DI connection factories (`users_db`, `analytics_db`).
- **`sdk/DigitalBrain.SDK/`**
  - **Purpose**: The unified assembly providing native platform integrations.
  - **Contents**: AI client orchestrators, Google Cloud connectors, SQLite storage engines, Windows registry hooks, MCP (Model Context Protocol) bridges, Visuals, Identity, Stripe billing, Telegram alerting, and Grok (under `XAI/Grok/`) systems.
- **`sdk/DigitalBrain.SDK.Contracts/`**
  - **Purpose**: The consolidated contracts assembly holding all synapse, neuron, and signal records.

---

## 2. Terminology Map

To ensure conceptual consistency across all modules:
- **Neuron** (formerly *Seam*): The fundamental virtual actor or grain processing unit. Every process block, integration wrapper, or domain executor is implemented as a Neuron.
- **Synapse**: The strongly-typed data package transmitted between Neurons. All inter-neuron communication happens asynchronously by passing Synapse events over stream boundaries.

---

## 3. How to Write a Custom Neuron & Synapse

Extending the platform involves defining a typed Synapse (the data carrier) and a matching Neuron (the processing actor).

### Step 1: Define a Synapse
Create a strongly-typed record inheriting from `DigitalBrain.Core.Neurons.Synapse`. Every synapse must include metadata headers for telemetry and routing tracking.

```csharp
using System;
using DigitalBrain.Core.Neurons;

namespace MyCompany.Extension.Synapses
{
    [Orleans.GenerateSerializer]
    public sealed record TextProcessedSynapse(
        SynapseMetadata Headers,
        string InputText,
        string ProcessedResult,
        double ConfidenceScore
    ) : Synapse(Headers);
}
```

### Step 2: Define the Neuron Contract
Neurons are Orleans virtual grains. Define a grain interface inheriting from `INeuron`:

```csharp
using System.Threading.Tasks;
using DigitalBrain.Core.Neurons;
using Orleans;

namespace MyCompany.Extension.Neurons
{
    public interface ITextProcessorNeuron : INeuron, IGrainWithGuidKey
    {
        Task ProcessAsync(TextProcessedSynapse synapse);
    }
}
```

### Step 3: Implement the Neuron Class
Derive your class from the base class `DigitalBrain.Core.Neurons.Neuron`. Inject keyed state histories for tracking incoming and outgoing synapses.

```csharp
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using DigitalBrain.Core.Neurons;
using DigitalBrain.Core.Neurons.State;
using Orleans;
using Orleans.Runtime;
using MyCompany.Extension.Synapses;

namespace MyCompany.Extension.Neurons
{
    public sealed class TextProcessorNeuron : Neuron, ITextProcessorNeuron
    {
        private readonly ILogger<TextProcessorNeuron> _logger;

        public TextProcessorNeuron(
            [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
            [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
            IGrainFactory grains,
            ILogger<TextProcessorNeuron> logger
        ) : base(incoming, outgoing, grains, logger)
        {
            _logger = logger;
        }

        protected override async Task HandleSynapseAsync(Synapse synapse)
        {
            if (synapse is TextProcessedSynapse textSynapse)
            {
                _logger.LogInformation("Processing text: {Text}", textSynapse.InputText);
                
                // Implement your custom business logic here
                var upperResult = textSynapse.InputText.ToUpperInvariant();

                // Create and emit an outgoing synapse
                var outputHeaders = SynapseFactory.CreateHeader<ITextProcessorNeuron, INeuron>(
                    senderId: this.GetNeuronId(),
                    receiverId: new NeuronId(Guid.NewGuid().ToString())
                );

                var outputSynapse = new TextProcessedSynapse(
                    Headers: outputHeaders,
                    InputText: textSynapse.InputText,
                    ProcessedResult: upperResult,
                    ConfidenceScore: 1.0
                );

                await EmitAsync(outputSynapse);
            }
        }
    }
}
```

---

## 4. How to Register custom Extensions

To make your custom Neurons and Synapses discoverable by the DigitalBrain orchestrator, they must be registered inside the host container.

### A. Automatic Assembly Scanning
The standard DigitalBrain host automatically scans referenced assemblies for any classes deriving from `Neuron`. Ensure your custom project is added as a reference to the main `DigitalBrain.AppHost` or Silo Boot project.

### B. Explicit Dependency Injection Configuration
If your Neuron requires additional third-party client integrations (e.g. database connection factories or external API clients), register them in your startup pipeline using Orleans Keyed DI:

```csharp
public static class HostBuilderExtensions
{
    public static IServiceCollection AddCustomNeuronServices(this IServiceCollection services)
    {
        // Example: Keyed DB Context registration for custom persistence
        services.AddKeyedSingleton<IDbContextFactory<MyCustomContext>>("my_custom_db", (sp, key) => {
            return new MyCustomContextFactory();
        });

        return services;
    }
}
```

---

## 5. Distribution Best Practices

1. **Keep Interfaces Lightweight**: Place custom synapse records and grain interfaces in a `.Contracts` class library (e.g., `MyCompany.Extension.Contracts.csproj`). This allows other developers to reference your synapses and compile call-chains without downloading heavy implementation binaries or private SDK packages.
2. **Isolate Connectors**: Place concrete heavy execution logic in discrete provider folders or NuGet packages. Never add direct compile-time references from `DigitalBrain.Core` to physical database drivers, Windows-specific registers, or proprietary AI SDKs.
3. **Verify via InProcessTestCluster**: Before publishing your package, write comprehensive integration tests utilizing `InProcessTestClusterBuilder` to guarantee parallel execution safety, stream compatibility, and memory leak freedom.
