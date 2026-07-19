# SDK Reorganization Analysis Report

## Executive Summary
This analysis catalogs all 23 subdirectories under `sdk/DigitalBrain.SDK/` (and its mirror `sdk/DigitalBrain.SDK.Contracts/`) and designs a precise target architecture aligning them to four primary domains: **Ai**, **Collaboration**, **Development**, and **UI**. It identifies all namespaces, `.csproj` project dependencies, and `using` statements impacted across the codebase, providing a clear path for smooth implementation.

---

## 1. Catalog of Existing Subdirectories
The existing `sdk/DigitalBrain.SDK/` structure contains **23 subdirectories** (excluding top-level code files and properties):

| # | Subdirectory | Core Content & Files | Primary Responsibility |
|---|---|---|---|
| 1 | `Ai` | `Embedding/`, `Llm/`, `Voice/`, `Planning/`, `AiHealth/`, `Intent/`, `GroupChat/`, `Explaining/`, `Slm/` | AI-related core functionalities, reasoning models, planning systems, and voice engines. |
| 2 | `Aspire` | `AspireBootConnector.cs`, `Runtime/` | Orleans/Aspire infrastructure for booting and orchestrating distributed application hosts. |
| 3 | `Canvas` | `CanvasNeuron.cs`, `CanvasPlan.cs`, `ThreeDCanvas.cs` | UI layout rendering canvas, plans, and 3D scenes. |
| 4 | `CodeGraph` | `CodeGraphNeuron.cs`, `CodeGraphDatabaseContext.cs` | C# static analysis representation, parsing, and syntax graph modeling. |
| 5 | `Developer` | `Directory/`, `File/`, `GitHub/`, `CodeReviewer/`, `BrainOSDeveloperBridge.cs` | Developer automation utilities, local file I/O operations, review engines, and GitHub credentials/neurons. |
| 6 | `Google` | `Auth/`, `Gmail/`, `YouTube/`, `Digest/` | Integration and credentials management with Google Services (Gmail, YouTube). |
| 7 | `INO` | `InoAssistantNeuron.cs`, `InoToCSharpTranspiler.cs` | Platform support for transpiling v5 `.ino` single-file logic to runtime C#. |
| 8 | `Identity` | `Identity/` subfolder: `AesEncryption.cs`, `AzureDeploymentNeuron.cs`, `GlobalBrainSyncGateNeuron.cs`, `IdentityPlan.cs` | Encryption, cloud resource deployment, global sync gates, and identity. |
| 9 | `Onboarding` | `Onboarding/` subfolder: `BrainOSOnboardingBridge.cs`, `OnboardingPlan.cs`, `OnboardingStoreNeuron.cs` | Signup gate logic, remote flutter widget terms templates, and agreement store. |
| 10 | `Persistence` | `DigestStoreGrain.cs`, `EfCoreSynapsePersistenceService.cs`, `SynapseDbContext.cs` | Database transaction contexts, mapping services, and durable EF Core synapses. |
| 11 | `Postgres` | `BrainOSPostgresBridge.cs`, connection factories | PostgreSQL specific ADO.NET and EF Core mappings. |
| 12 | `Properties` | `launchSettings.json` | standard development runtime configurations. |
| 13 | `Scripting` | `DynamicScriptingService.cs` | Dynamic Roslyn C# code execution scripting capabilities. |
| 14 | `Security` | `BrainOSSecurityBridge.cs`, `OrleansSecretVault.cs`, `OrleansSettingService.cs` | Settings store grains, local secret DPAPI-vaults, and kernel users. |
| 15 | `SoftwareEngineering`| `SoftwareDeveloperNeuron.cs` | Coding workflow agent simulation logic. |
| 16 | `Sqlite` | `BrainOSDataBridge.cs`, `FileSystem/` (FileReadNeuron), `Sqlite/` (SqliteNeuron) | Local SQLite context, connection factory, and file reader integrations. |
| 17 | `Stripe` | `StripeWebhookNeuron.cs` | External billing integration and webhook handlers. |
| 18 | `Swarm` | `SwarmAgentNeuron.cs`, `SwarmSessionNeuron.cs`, `SwarmWorkspace.cs` | Orleans streaming multi-agent collaboration and Roslyn syntax-review workspace. |
| 19 | `Telegram` | `TelegramAlertNeuron.cs` | External messaging alert integration neuron. |
| 20 | `Testing` | `NeuronTestingSandbox.cs` | Unit/integration testing infrastructure for isolated neurons. |
| 21 | `Visuals` | `Icons/`, `Materials/` | UI/Visual elements (material plan resolvers and icon catalog neurons). |
| 22 | `Windows` | `FileSystem/` (WindowsFileSystemNeuronGrain), `Runtime/` | Native Windows filesystem bindings and OS runtime service extensions. |
| 23 | `XAI` | `Grok/` (GrokConnector.cs) | xAI Grok API integration. |

---

## 2. Precise Reorganization Mapping

All subdirectories under `sdk/DigitalBrain.SDK/` (along with their mirror implementations in `sdk/DigitalBrain.SDK.Contracts/`) are mapped to the four domain-aligned paths. 

*Note: For organizational unity and to avoid solution drift, both folders must undergo the exact same structural paths.*

```
Target Root: sdk/DigitalBrain.SDK/
  ├── Ai/
  ├── Collaboration/
  ├── Development/
  └── UI/
```

### Mapping Table:
| Source Directory | Target Path under `sdk/DigitalBrain.SDK/` | Target Namespace | Target Domain |
|---|---|---|---|
| `Ai` | `Ai/` (Llm, Embedding, Voice, Slm, etc.) | `DigitalBrain.SDK.Ai.*` | **Ai** |
| `XAI` | `Ai/Grok/` | `DigitalBrain.SDK.Ai.Grok` | **Ai** |
| `Swarm` | `Ai/Swarm/` *(Option A)* or `Collaboration/Swarm/` *(Option B)* | `DigitalBrain.SDK.Ai.Swarm` or `Collaboration.Swarm` | **Ai** *(Recommended)* |
| `Google` | `Collaboration/Google/` | `DigitalBrain.SDK.Collaboration.Google.*` | **Collaboration** |
| `Telegram` | `Collaboration/Telegram/` | `DigitalBrain.SDK.Collaboration.Telegram` | **Collaboration** |
| `Stripe` | `Collaboration/Stripe/` | `DigitalBrain.SDK.Collaboration.Stripe` | **Collaboration** |
| `Developer/GitHub` | `Collaboration/GitHub/` | `DigitalBrain.SDK.Collaboration.GitHub` | **Collaboration** |
| `Developer` *(excl. GitHub)* | `Development/Developer/` (or specific subfolders) | `DigitalBrain.SDK.Development.Developer` | **Development** |
| `INO` | `Development/INO/` | `DigitalBrain.SDK.Development.INO` | **Development** |
| `SoftwareEngineering`| `Development/SoftwareEngineering/` | `DigitalBrain.SDK.Development.SoftwareEngineering` | **Development** |
| `Scripting` | `Development/Scripting/` | `DigitalBrain.SDK.Development.Scripting` | **Development** |
| `Testing` | `Development/Testing/` | `DigitalBrain.SDK.Development.Testing` | **Development** |
| `CodeGraph` | `Development/CodeGraph/` | `DigitalBrain.SDK.Development.CodeGraph` | **Development** |
| `Windows` | `Development/Windows/` | `DigitalBrain.SDK.Development.Windows.*` | **Development** |
| `Aspire` | `Development/Aspire/` | `DigitalBrain.SDK.Development.Aspire` | **Development** |
| `Persistence` | `Development/Persistence/` | `DigitalBrain.SDK.Development.Persistence` | **Development** |
| `Postgres` | `Development/Persistence/Postgres/` | `DigitalBrain.SDK.Development.Persistence.Postgres`| **Development** |
| `Sqlite` | `Development/Persistence/Sqlite/` | `DigitalBrain.SDK.Development.Persistence.Sqlite` | **Development** |
| `Security` | `Development/Security/` | `DigitalBrain.SDK.Development.Security` | **Development** |
| `Identity` | `Development/Identity/` | `DigitalBrain.SDK.Development.Identity` | **Development** |
| `Onboarding` | `Development/Onboarding/` *(Option A)* or `UI/Onboarding/` *(Option B)* | `BrainOS.Domains.Onboarding` *(Preserved)* | **Development** *(Recommended)* |
| `Canvas` | `UI/Canvas/` | `DigitalBrain.SDK.UI.Canvas.*` | **UI** |
| `Visuals` | `UI/Visuals/` | `DigitalBrain.SDK.UI.Visuals.*` | **UI** |

---

## 3. Edge Cases & Placement Rationale

### A. `Swarm` (Move to `Ai/Swarm`)
* **Rationale**: Swarm consists of `SwarmAgentNeuron` and `SwarmWorkspace`. `SwarmAgentNeuron` executes `IChatClient` (LLM reasoning models) and coordinates dynamic symbol analyses. While coordinating multiple agents involves collaboration, its runtime capability is strictly **Reasoning (AI)**. Placing it under **Ai** aligns with modern AI agentic framework conventions.

### B. `Aspire` (Move to `Development/Aspire`)
* **Rationale**: .NET Aspire (`AspireBootConnector`, `AspireRuntimeNeuron`) manages orchestrating, starting, and connecting distributed system services at local development time. It has zero external customer-facing features and serves strictly as developer application-hosting runtime configuration.

### C. `Persistence`, `Postgres`, & `Sqlite` (Move to `Development/Persistence/...`)
* **Rationale**: These subdirectories handle direct database schema access (Entity Framework Core DbContexts, ADO.NET Npgsql / SQLite connection factories). They form the database storage layer of the platform and should reside cleanly inside a unified `Development/Persistence/` namespace sub-tree.

### D. `Security` (Move to `Development/Security`)
* **Rationale**: Orleans secret vaults and settings grains handle decryption of system API tokens and credentials using Windows DPAPI or cross-platform AES. It is core infrastructure development code supporting the security posture of the platform.

### E. `Identity` (Move to `Development/Identity`)
* **Rationale**: Contains Azure cloud resource manager deployment neurons, AES utility methods, and global system sync gate neurons. These represent system-wide configuration gates and infrastructure setup, belonging under Development.

### F. `Onboarding` (Move to `Development/Onboarding` or keep root namespace)
* **Rationale**: Onboarding defines the policy gateway that enforces terms acceptance for users.
* *Namespace Anomaly*: Onboarding is unique. Unlike other parts of the SDK which use the `DigitalBrain.SDK.*` prefix, Onboarding declares:
  `namespace BrainOS.Domains.Onboarding.Onboarding;`
  This aligns with domain projects like samples (`BrainOS.Domains.Samples`). We recommend keeping the namespace unmodified to avoid breaking binary-serialization and Orleans grain type references, while physically moving the subdirectory to `Development/Onboarding/` for layout compliance.

---

## 4. Impacted Solution Files

### A. Project Files (`.csproj` references)
No `.csproj` paths will need to change because the project structures `DigitalBrain.SDK.csproj` and `DigitalBrain.SDK.Contracts.csproj` include all C# files recursively (`**/*.cs` is standard). However, relative paths for embedded `.ino` resources in `DigitalBrain.SDK.csproj` are resolved via wildcards:
```xml
<EmbeddedResource Include="**\*.ino" />
<AdditionalFiles Include="**\*.ino" Exclude="bin\**\*;obj\**\*" />
```
As long as `.ino` files remain within the project tree, they will automatically compile. No physical `.csproj` changes are required to support compiling, but all references *to* the SDK remain intact.

### B. Impacted `using` Statements
Moving namespaces will require updating C# file imports across multiple modules in the workspace:

#### 1. In `DigitalBrain.Test/` (Test Project)
* `Ai/LlmExpressiveTests.cs` -> change `DigitalBrain.SDK.Canvas` to `DigitalBrain.SDK.UI.Canvas`
* `Aspire/AspireAppStartedSignalClusterTests.cs` -> change `DigitalBrain.SDK.Aspire.Runtime` to `DigitalBrain.SDK.Development.Aspire.Runtime`
* `CodeGraph/CodeGraphNeuronTests.cs` -> change `DigitalBrain.SDK.CodeGraph` to `DigitalBrain.SDK.Development.CodeGraph`
* `Developer/DeveloperNeuronProjectionTests.cs` -> change `DigitalBrain.SDK.Developer.*` to `DigitalBrain.SDK.Development.Developer.*` and `Collaboration.GitHub`
* `Google/BrainOSGoogleBridgeTests.cs` -> change `DigitalBrain.SDK.Google` to `DigitalBrain.SDK.Collaboration.Google`
* `Persistence/PostgresPersistenceSweepTests.cs` -> change `DigitalBrain.SDK.Sqlite.Persistence` to `DigitalBrain.SDK.Development.Persistence.Sqlite`
* `Swarm/SwarmOrleansTests.cs`, `SwarmWorkspaceTests.cs` -> change `DigitalBrain.SDK.Swarm` to `DigitalBrain.SDK.Ai.Swarm`
* `Swarm/SwarmRealGrokTests.cs` -> change `DigitalBrain.SDK.XAI.Grok` to `DigitalBrain.SDK.Ai.Grok`
* `Windows/WindowsFileSystemNeuronTests.cs` -> change `DigitalBrain.SDK.Windows.FileSystem` to `DigitalBrain.SDK.Development.Windows.FileSystem`

#### 2. In `kernel/` (Platform Kernel Projects)
* `BrainOS.AppHost/Program.cs` -> change `DigitalBrain.SDK.Ai` imports to the restructured `Ai` domains (if nested namespaces change).
* `BrainOS.Boot/AspireBootNeuronHost.cs` -> change `DigitalBrain.SDK.Aspire.Contracts` to `DigitalBrain.SDK.Development.Aspire.Contracts`
* `BrainOS.Kernel/BrainOSKernelBootstrapper.cs` -> change `DigitalBrain.SDK.Sqlite.Contracts`, `DigitalBrain.SDK.Google.Contracts`, `DigitalBrain.SDK.Aspire.Runtime`, `DigitalBrain.SDK.Windows.Runtime` to their reorganized counterparts (`Development.Persistence.Sqlite`, `Collaboration.Google`, `Development.Aspire`, `Development.Windows`).
* `BrainOS.Kernel/Cortex/IntentDispatcher.cs` -> change `DigitalBrain.SDK.Canvas.Contracts`, `DigitalBrain.SDK.Google.Contracts` to `DigitalBrain.SDK.UI.Canvas`, `Collaboration.Google`.
* `BrainOS.Kernel/Runtime/DynamicDomainRegistry.cs` -> change `DigitalBrain.SDK.Sqlite.Sqlite` to `DigitalBrain.SDK.Development.Persistence.Sqlite`.

---

## 5. Architectural Recommendations

1. **Restructure Mirroring Projects Concurrently**:
   Perform the directory restructuring for both `sdk/DigitalBrain.SDK` and `sdk/DigitalBrain.SDK.Contracts` simultaneously. This prevents divergence and ensures contracts are co-located in the same domain structure as their implementations.
2. **Preserve Orleans Serializer Namespaces**:
   Be extremely careful renaming namespaces for types that are registered with Orleans streams (e.g. `SwarmAgentMessage` inside `Swarm`, `AppStartedSignal` in `Aspire`). If these types are serialized into dynamic queues or reminders, renaming their namespace might break backward compatibility with stored states. If state persistence safety is high-priority, keep the old namespace via `[Alias]` attributes, or avoid renaming their namespaces while changing directory paths.
3. **Source Generator Compatibility**:
   Ensure `BrainOS.Core.SourceGen` (the transpilation analyzer for `.ino` files) resolves the updated paths of embedded `.ino` resources correctly. The search patterns are wildcard-based, so path changes are safe, but verify that generated output partial classes match the restructured namespaces.
