# Milestone 1 Analysis: SDK Unification & Aspire Readiness

## 1. Executive Summary

This report delivers a read-only investigation and deep-dive analysis of the DigitalBrain codebase for **Milestone 1: SDK Unification & Aspire Readiness**. All findings have been verified through direct source-code inspection, builds, and test runs.

Key takeaways:
1. **SDK Standalone Project Footprint**: Standalone projects in `sdk/` are logically cohesive but create substantial process overhead and compilation delays during development and local testing.
2. **SDK Unification Strategy**: A concrete plan is formulated to collapse the 10+ standalone connector neuron implementations into a single C# native connector assembly (`DigitalBrain.SDK`) and separate their lightweight wire contracts into a clean `DigitalBrain.SDK.Contracts` assembly.
3. **Aspire & Production Readiness**: The AppHost utilizes a profile-gated configuration (`BrainOSAppHostProfile`) to safely isolate and exclude heavyweight resources (Flutter UI, MCP Server) with hardcoded ports (`5800`, `5821`, `5810`) during test runs. Testing harnesses are completely isolated and prevented from leaking using a keyed, thread-safe bootstrapper cache (`TestBrainOSBootstrapper`).
4. **Build & Test Health**: The fast test solution `BrainOS.Fast.slnx` builds successfully with zero compiler errors/warnings, and all 408 tests pass in less than 19 seconds.

---

## 2. Standalone SDK Projects: Analysis & Structure

We investigated the following standalone directories under `sdk/`:
* `DigitalBrain.SDK.Ai` (AI logic, embedding models, LLM provider registry, Whisper-based speech-to-text)
* `DigitalBrain.SDK.Aspire` (.NET Aspire host-level integration, ingress signal emitters)
* `DigitalBrain.SDK.Canvas` (state machine scenes, durable lists)
* `DigitalBrain.SDK.Google` (Gmail service, YouTube searches, YouTube plan, Stripe Webhooks, Telegram alerts)
* `DigitalBrain.SDK.Grok` (Direct x.ai IChatClient provider wrapper)
* `DigitalBrain.SDK.Identity` (Login, onboarding, user terms, encryption, local brain spawning)
* `DigitalBrain.SDK.Mcp` (ASP.NET Web API Model Context Protocol server exposing gRPC-backed tools)
* `DigitalBrain.SDK.Sqlite` (SQLite Context, Postgres DB context factory, durable state persistence)
* `DigitalBrain.SDK.Visuals` (Visual catalogs, icon spec resolvers, material plan overrides)
* `DigitalBrain.SDK.Windows` (Windows OS-level process launching seam target)

### 2.1 Common Structure
Every native SDK connector adheres to an extremely disciplined architectural pattern:
1. **Contracts Assembly**: Serializable data schemas representing synapses/signals and wire-level constants (e.g. `DigitalBrain.SDK.Ai.Contracts`, `DigitalBrain.SDK.Google.Contracts`).
2. **Domain Silo Bridges**: Standard registrations implementing `IBrainOSSiloBridge` (or in AI's case, `IBrainOSLlmBridge`) discovered by the Kernel via reflection during `AddBrainOSDomain()` to register services in Dependency Injection.
3. **Colocated Reqnroll Triplets**: Native business logic neurons are tested using colocated BDD files:
   - `Neuron.cs` (native C# class or Orleans Virtual Actor)
   - `Neuron.feature` (Gherkin scenario specs)
   - `Neuron.Steps.cs` (Reqnroll step definitions running the scenarios)
4. **Stateful/OS Seam Grains**: System-level stateful or OS-specific capabilities (e.g. `Windows`, `Aspire`, `Sqlite`) are structured as Orleans grains implementing specialized seam interfaces:
   - `ICallSeamTarget`: For single delegated calls (e.g. `WindowsRuntimeSeamGrain` launching process commands).
   - `IPredicateSeamTarget`: For SLM semantic predicate checks.
   - `IResourceSeamTarget`: For KV or persistence interactions (e.g., `save into ~port`).
   - `IStreamSeamTarget`: For streaming data boundaries.

---

## 3. SDK Unification Strategy

The proposed unification collapses the fragmented standalone projects into a single unified directory layout.

```
sdk/
├── DigitalBrain.SDK.Contracts/    <-- Consolidated contracts (pure metadata & POCOs)
│   └── DigitalBrain.SDK.Contracts.csproj
└── DigitalBrain.SDK/              <-- Consolidated implementations (AI, Google, SQLite, etc.)
    └── DigitalBrain.SDK.csproj
```

### 3.1 Contract Separation vs. Heavy Implementations
It is **critical** to extract and isolate all contracts into a single `DigitalBrain.SDK.Contracts` assembly separate from implementation. 

* **Why**: The contracts assembly contains only POCO models representing synapses, wire-level FQN constants, and basic abstractions. It has **zero heavy dependencies** (no Microsoft.Extensions.AI, no Google API SDKs, no EF Core/Postgres drivers, no Whisper.net C++ runtimes).
* **Benefits**: 
  - Allows both the `BrainOS.Kernel` (VM, Navigator routing) and external compiled/scripted scenarios to reference contracts without loading hundreds of megabytes of heavy third-party DLLs.
  - Ensures extremely fast type-linking and Roslyn-compilation.
  - Minimizes memory footprint and startup delays across the cluster.

### 3.2 Single Host Silo (Worker Silo)
Currently, `BrainOS.AppHost` runs separate process instances for every individual domain.
* **Problem**: Booting 10+ silos creates enormous local memory overhead, excessive Redis client connection pools, and port conflicts.
* **Unified Strategy**: Consolidate all domain implementations into the single `DigitalBrain.SDK` worker. Calling `builder.AddBrainOSDomain()` on startup will dynamically invoke all domain bridges (`IBrainOSSiloBridge`) in the single worker process. This collapses the cluster footprint down to just **two active silos** in development and production:
  1. `BrainOS.Kernel` (VM, interpreter, navigator, private vault)
  2. `DigitalBrain.SDK` (All native AI, database, Google, and OS connector services)

### 3.3 Platform-Specific Packages Guard
All heavy dependencies from the individual projects will be merged into `DigitalBrain.SDK.csproj` using central package management. Multi-platform guards (such as Whisper.net platform runtimes) are conditionalized cleanly:
```xml
<PackageReference Include="Whisper.net" />
<PackageReference Include="Whisper.net.Runtime" />
<PackageReference Include="Whisper.net.Runtime.Cuda" Condition="$([MSBuild]::IsOSPlatform('Windows'))" />
<PackageReference Include="Whisper.net.Runtime.CoreML" Condition="$([MSBuild]::IsOSPlatform('OSX'))" />
<PackageReference Include="Whisper.net.Runtime.Vulkan" Condition="$([MSBuild]::IsOSPlatform('Linux'))" />
```

---

## 4. Aspire Configuration & Production Readiness

We performed a deep-dive inspection of `kernel/BrainOS.AppHost`.

### 4.1 Gating Heavy Port-Bound Resources
The AppHost uses a profile-gated approach to decouple development vs test configurations:
* **Identified Port Collisions**: 
  - Flutter Web: Hardcoded to port `5800` (`--web-port=5800`).
  - Flutter Windows: Hardcoded to port `5821` (`--vm-service-port=5821`).
  - MCP Server: Hardcoded to port `5810`.
* **The Gating Mechanism**: 
  `BrainOSAppHostProfile` configuration determines the run posture.
  ```csharp
  var appHostProfile = BrainOSAppHostProfileConfiguration.From(builder.Configuration);
  if (appHostProfile == BrainOSAppHostProfile.Product)
  {
      builder.AddFlutter() ...
      builder.AddProject<Projects.DigitalBrain_SDK_Mcp>("brainos-mcp") ...
  }
  ```
  During test runs, `Test` profile is passed via the command line or environment (`--BrainOS:AppHost:Profile=Test`), which completely excludes Flutter and MCP, preventing all port collisions.

### 4.2 Dynamic Port Configurations for Production
For production setups, Aspire should utilize dynamic port assignments rather than fixed local port maps (e.g. omit `port:` in `.WithHttpEndpoint()` or let proxies handle them), allowing multiple instances to run side-by-side without network clashes.

### 4.3 Keyed Test Bootstrapper (No Leaks)
Process leaks are mitigated in `TestBrainOSBootstrapper.cs` using a concurrent dictionary keyed by `TestBrainOSOptions` to cache the boot instances:
```csharp
static readonly ConcurrentDictionary<TestBrainOSOptionsKey, Lazy<Task<TestBrainOS>>> Boots = new();
```
- Option-equivalent scenarios share a single `DistributedApplication` instance.
- The `ShutdownIfBootedAsync()` method handles teardown during test collection fixtures and process exit hooks, ensuring **zero leaked DCP or silo processes** remain after test execution.

---

## 5. Build & Test Health

We compiled and tested `BrainOS.Fast.slnx`:
1. **Compilation**: `dotnet build BrainOS.Fast.slnx /nodeReuse:false` completed successfully in **7.72 seconds** with **0 Errors** and **0 Warnings**. Disabling MSBuild node reuse prevents transient resolver failures on Windows.
2. **Tests Execution**: `dotnet test BrainOS.Fast.slnx --no-build` executed the entire fast test suite in **18.6 seconds** with **0 Failures**!
   - **Total Tests**: 408
   - **Passed**: 408
   - **Failed**: 0
   - **Skipped**: 0
3. **Conclusion**: The fast test environment is in an extremely healthy, stable, and high-performance state.

---

## 6. Concrete Implementation Plan for the Worker

When implementing the unification in Milestone 1, the worker should execute the following steps in order:

### Phase 1: Establish Consolidated Contracts
1. Create `sdk/DigitalBrain.SDK.Contracts/` with `DigitalBrain.SDK.Contracts.csproj`.
2. Move all `.Contracts` directory files into this project under domain namespaces:
   - `DigitalBrain.SDK.Ai.Contracts/*` -> `DigitalBrain.SDK.Contracts/Ai/*`
   - `DigitalBrain.SDK.Aspire.Contracts/*` -> `DigitalBrain.SDK.Contracts/Aspire/*`
   - `DigitalBrain.SDK.Canvas.Contracts/*` -> `DigitalBrain.SDK.Contracts/Canvas/*`
   - `DigitalBrain.SDK.Google.Contracts/*` -> `DigitalBrain.SDK.Contracts/Google/*`
   - `DigitalBrain.SDK.Identity.Contracts/*` -> `DigitalBrain.SDK.Contracts/Identity/*`
   - `DigitalBrain.SDK.Sqlite.Contracts/*` -> `DigitalBrain.SDK.Contracts/Sqlite/*`
   - `DigitalBrain.SDK.Visuals.Contracts/*` -> `DigitalBrain.SDK.Contracts/Visuals/*`
3. Resolve namespace references and ensure the single Contracts assembly compiles cleanly.

### Phase 2: Create the Unified SDK Assembly
1. Create `sdk/DigitalBrain.SDK/` with `DigitalBrain.SDK.csproj`.
2. Reference `DigitalBrain.SDK.Contracts.csproj` and `BrainOS.Kernel.Contracts.csproj`.
3. Consolidate package references from the former project files, ensuring OS platform-guards are kept for Whisper.net.
4. Move all implementation source files (`.cs`, `.ino`, `.feature`) into domain subfolders:
   - `Ai/`, `Aspire/`, `Canvas/`, `Google/`, `Grok/`, `Identity/`, `Sqlite/`, `Visuals/`, `Windows/`
5. Consolidate the domain silo bridges (`BrainOSAiBridge`, `BrainOSGoogleBridge`, etc.) or registers them centrally.

### Phase 3: Transition Silos & Update AppHost
1. Merge the domain project launch configurations in `BrainOS.AppHost` to reference the single unified `DigitalBrain.SDK` worker project.
2. In the AppHost, register `DigitalBrain_SDK` as a single Orleans Silo (Worker Silo) containing all bridges.
3. Update `BrainOS.Kernel`'s startup touches (`GC.KeepAlive(typeof(...))`) to touch the unified SDK's types, guaranteeing their assemblies get loaded for cataloging at boot.
4. Validate that all fast tests and build configurations compile cleanly and execute successfully.
