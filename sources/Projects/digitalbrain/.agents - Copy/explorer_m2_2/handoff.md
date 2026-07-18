# Handoff Report: Pure Neuronic Bootstrap Flow Refactoring Plan

This handoff report summarizes the read-only investigation, findings, logical reasoning, and refactoring plan for transitioning the `DigitalBrain` startup sequence from procedural C# builder chains to a spec-first, neuronic bootstrap flow directed by `GenesisNeuron` in the **v5 paradigm**.

---

## 1. Observation

Direct observations and file paths examined during the sweep:

1. **Procedural Host Setup** (`digitalbrain.cs`, lines 34-45):
   - Sets up Aspire application using:
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

2. **Orleans Silo Initialization** (`kernel/DigitalBrain.Core.Hosting/AddDigitalBrainSiloExtensions.cs`, lines 18-20):
   - Configures Orleans on the host:
     ```csharp
     public static IHostApplicationBuilder AddDigitalBrainSilo(this IHostApplicationBuilder builder)
     {
         builder.UseOrleans(silo =>
         ...
     ```

3. **DI Registrations and Assembly Load** (`kernel/DigitalBrain.Kernel/DigitalBrainKernelBootstrapper.cs`, lines 34-48, 159):
   - Wires dynamic neuron catalogs, scanning, and registers the startup task:
     ```csharp
     var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet();
     foreach (var file in System.IO.Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll"))
     ...
     builder.Services.AddTransient<Orleans.Runtime.IStartupTask, DigitalBrain.Kernel.OS.KernelOSBootstrapper>();
     ```

4. **Silo Lifecycle Startup Task** (`kernel/DigitalBrain.Kernel/OS/KernelOSBootstrapper.cs`, lines 10-36):
   - Inspects global license settings, verifies `primary` brain registry container, and fires `BootSystem` synapse:
     ```csharp
     public async Task Execute(CancellationToken cancellationToken)
     {
         var licenseNeuron = grains.GetGrain<DigitalBrain.Kernel.Runtime.Neurons.ILicenseNeuron>("global");
         await licenseNeuron.CheckLicenseAgreementAsync();

         var registry = grains.GetGrain<DigitalBrain.Kernel.Contracts.Brain.IBrainRegistry>(Guid.Empty);
         ...
         var osNeuron = grains.GetGrain<IKernelOSNeuron>(Guid.Empty);
         ...
         await osNeuron.BootSystemAsync(bootSynapse);
     }
     ```

5. **Dynamic Target Catalog Resolution & Gateway Activation** (`kernel/DigitalBrain.Kernel/OS/KernelOSNeuron.cs`, lines 28-73):
   - Coordinates dynamic catalog scanning and gateway spin-up:
     ```csharp
     var scanner = serviceProvider.GetRequiredService<NeuronCatalogScanner>();
     await scanner.Execute(ct);
     ...
     var gateway = Grains.GetGrain<IGatewayNeuron>(Guid.Empty);
     await gateway.EnsureActivatedAsync();
     ```

6. **The v5 Spec-First Vision Document** (`docs/v5plan/VISION.md`, lines 33-50, 94):
   - Details the architectural cuts and invariants:
     - `V5-1 One file per behavior.` ("A neuron is one `.ino` file. No more `.cs` + `.feature` + `.Steps.cs` triplet.")
     - `V5-3 No global catalog.` (`MapCatalog.With(...)` is deleted. Ports resolve at activation time, not boot time.)
     - `kernel/DigitalBrain.Boot ──> folded into digitalbrain.cs`
     - `Hand-built IDigitalBrain seam in digitalbrain.cs ──> One DigitalBrain.Launch(args) call`

7. **The Topology Spec File** (`digitalbrain.ino`, lines 1-10):
   - Declares system composition:
     ```ino
     neuron DigitalBrain.System
       using loaded            = synapse(DigitalBrain.Kernel.Loaded)
       using brains            = neuron(DigitalBrain.BrainRegistry)
       using aspire            = neuron(DigitalBrain.SDK.AspireRuntime)
     ```

8. **Existing SDK Aspire Neuron** (`sdk/DigitalBrain.SDK/Aspire/Runtime/AspireRuntimeNeuron.cs`, lines 21-29):
   - Wraps the native connector:
     ```csharp
     [GrainType(NeuronTargetFqn)]
     public sealed class AspireRuntimeNeuron(IAspireBootConnector connector) : Grain, ICallNeuronTarget
     {
         public const string NeuronTargetFqn = "DigitalBrain.SDK.Aspire.Runtime";
     ```

---

## 2. Logic Chain

The reasoning linking observations to our conclusions:
1. **Observation 1 & 6** demonstrate that `digitalbrain.cs` currently utilizes procedural fluent builders to set up the distributed topology, whereas the **Vision v5** architecture explicitly mandates the elimination of procedural configuration in favor of a pure dynamic, spec-first composition flow.
2. **Observation 7** shows that `digitalbrain.ino` already has a declarative spec (`DigitalBrain.System`) mapping the required Aspire topographies (`orleans-redis`, `flutter-web`, `flutter-windows`, `digitalbrain-mcp`).
3. **Observation 4 & 5** establish that Orleans Silo startup triggers `KernelOSBootstrapper`, which verifies the license and forces `KernelOSNeuron` to scan catalogs and activate the gateway.
4. **Observation 8** confirms that `AspireRuntimeNeuron` (`DigitalBrain.SDK.Aspire.Runtime`) is ready to act as the Orleans bridge to the native `IAspireBootConnector`.
5. Therefore, a pure neuronic bootstrap flow can be achieved by:
   - Stripping the compile-time builders from `digitalbrain.cs` and making it a thin runner that merely boots Orleans and the core Ino interpreter.
   - Replacing the static `KernelOSBootstrapper` logic with a dynamic `GenesisBootstrapper` that parses, compiles, and interprets `digitalbrain.ino` at runtime.
   - Running the compiled plan using the dynamic `Interpreter`, which triggers a `Loaded` synapse on the `DigitalBrain.System` (acting as `GenesisNeuron`).
   - Having the `DigitalBrain.System` dynamically dispatch synapses/asks to `brains` (`BrainRegistry`) and `aspire` (`AspireRuntimeNeuron`) to dynamically configure the platform on-the-fly.

---

## 3. Caveats

- **External Assembly Scanning**: The project relies on base directory `.dll` loading to find plugin assemblies. If domains are installed dynamically via `git` (as per `V5-5`), the runtime catalog lookup must correctly scan the user local app-data directory (`~/AppData/Local/DigitalBrain/brains/{id}/domains/`) rather than the base directory.
- **Port Conflicts**: Since the DCP runner (`IAspireBootConnector`) spins up processes asynchronously, timing delays are critical. Port availability check must be robust when multiple domains are dynamic.
- **Boot vs. Steady Mode**: In pure boot-mode, the `BootHost` compiles and runs without the full cluster. In steady mode, the interpreted neuron runs inside the Orleans Silo. The `digitalbrain.ino` system composition plan needs to be registered with Orleans as a singleton system grain.

---

## 4. Conclusion

The transition to a pure neuronic bootstrap flow is highly feasible and directly aligned with the v5 core architecture. It drastically simplifies the host, reduces the project footprint by folding `DigitalBrain.Boot` and `DigitalBrain.Hosting` projects, and shifts the entire topology configuration into a single declarative, spec-first file (`digitalbrain.ino`).

**Actionable Refactoring Next Steps:**
1. Strip fluent builders from `digitalbrain.cs`.
2. Delete static builders (`DigitalBrainBuilder`, `DigitalBrainHostingExtensions`).
3. Expand `AspireRuntimeNeuron.cs` to handle `"register-resource"` dynamic prompts.
4. Update Orleans Silo startup (`KernelOSBootstrapper` / `GenesisBootstrapper`) to read and compile `digitalbrain.ino` using `InoCompiler` and emit `Loaded` synapse to trigger interpreted composition.

---

## 5. Verification Method

To verify the plan and subsequent implementation:
1. **Compile & Spec Verification**:
   Run the InoLang compiler test suite to ensure the system composition spec compiles flawlessly:
   ```powershell
   dotnet test kernel/DigitalBrain.Platform.Test/Boot/BootHostTests.cs
   ```
2. **Whole Suite Run**:
   Verify the full Orleans and Aspire cluster pipeline by running the test DLL wrapper:
   ```powershell
   dotnet run --project testdigitalbrain.cs
   ```
3. **Invalidation Conditions**:
   - Compilation of `digitalbrain.ino` fails due to unsupported vocabulary or syntax changes.
   - Orleans Silo fails to startup within the 5-second cold start budget because of eager catalog compilation.
