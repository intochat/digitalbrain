# Handoff Report: Milestone 3 - Represent .NET Aspire Orchestration as AspireNeuron (Explorer 1)

## 1. Observation

A sweep of `kernel/DigitalBrain.Hosting/` and the system bootstrap elements confirms:
- **`DigitalBrainHostingExtensions.cs`**:
  Line 9-12:
  ```csharp
  public static IDigitalBrainBuilder AddDigitalBrain(
      this IDistributedApplicationBuilder builder,
      string name = "digitalbrain")
  ```
  Lines 20-21:
  ```csharp
  digitalbrain.WithDomain<Projects.DigitalBrain_Domains_Dynamic>();
  digitalbrain.WithDomain<Projects.DigitalBrain_Domains_Samples>();
  ```
- **`DigitalBrainBuilder.cs`**:
  Lines 49-61 (Under `WithShell()`):
  ```csharp
      public IDigitalBrainBuilder WithShell()
      {
          if (profile.AutostartShell)
          {
              var appBuilder = Resource.AppBuilder;
              var flutterBuilder = appBuilder.AddFlutter();
  
              // Always add web
              flutterBuilder.WithWeb();
  
              // Always add windows, but autostart it for Product and Local profiles
              bool autostartWindows = profile.Profile == DigitalBrainProfile.Product || profile.Profile == DigitalBrainProfile.Local;
              flutterBuilder.WithWindows(autostart: autostartWindows);
  ```
  Lines 70-81 (Under `WithMcp()`):
  ```csharp
      public IDigitalBrainBuilder WithMcp()
      {
          if (profile.Profile == DigitalBrainProfile.Product || profile.Profile == DigitalBrainProfile.Local)
          {
              _ = Resource.AppBuilder.AddProject<Projects.DigitalBrain_SDK_Mcp>("digitalbrain-mcp")
                  .WithReference(Resource.Kernel!)
                  .WaitFor(Resource.Kernel!)
                  .WithEnvironment("KERNEL_ENDPOINT", Resource.Kernel!.GetEndpoint("kernel-https"))
                  .WithHttpEndpoint(port: 5810, targetPort: 5810, name: "http", isProxied: false);
          }
  ```
- **`digitalbrain.ino`**:
  Line 31: `ask aspire to "register-resource orleans-redis type:container port:59330"`
  Line 36: `ask aspire to "register-resource flutter-web type:executable path:../../UI/flutter args:run -d web-server --release port:5800"`
  Line 40: `ask aspire to "register-resource flutter-windows type:executable path:../../UI/flutter args:run -d windows --print-dtd port:5821 autostart:false"`
  Line 45: `ask aspire to "register-resource digitalbrain-mcp type:project path:sdk/DigitalBrain.SDK.Mcp port:5810"`

- **`GenesisNeuron.cs`**:
  Lines 91-98:
  ```csharp
                    // Parse components for ConfigureAspireResource synapse dispatch
                    var parsed = ParseRegisterResource(prompt);
                    var header = SynapseFactory.CreateHeader<IGenesisNeuron, IGenesisNeuron>(
                        new NeuronId("sys.genesis"),
                        new NeuronId("sys.aspire")
                    );
                    var configureSynapse = new ConfigureAspireResource(header, parsed.Name, parsed.Type, parsed.Config);
                    await FireSynapseAsync(configureSynapse, ct);
  ```

---

## 2. Logic Chain

1. **Static Coupling**: The current .NET Aspire AppHost configuration uses compile-time C# references (`Projects.DigitalBrain_Domains_Dynamic`, `Projects.DigitalBrain_SDK_Mcp`) and static extension methods (`WithShell()`, `WithMcp()`) to build the resource graph (Observations in `DigitalBrainHostingExtensions.cs` and `DigitalBrainBuilder.cs`).
2. **Aspire Graph Immutability**: Aspire's resource topology is immutable after `builder.Build()` is called at Host startup. Thus, runtime dynamic registration inside the Orleans Silo (like calling `AskAsync` or synapse handlers in `AspireRuntimeNeuron`) cannot expand the baked Aspire resource model post-startup.
3. **Decoupled Boot-time Loading**: To overcome this, the AppHost should dynamically load the topology configurations from `digitalbrain.ino` at boot-time (prior to `Build()`). By parsing the `register-resource` prompt statements directly at AppHost startup using reflection-free path resolution APIs (e.g. `builder.AddProject(name, path)` and `builder.AddExecutable`), we decouple the AppHost from static C# compilation boundaries.
4. **Synaptic Lifecycle Orchestration**: When Orleans starts, the `GenesisNeuron` reads `digitalbrain.ino` and dispatches a `ConfigureAspireResource` synapse for each resource (Observation in `GenesisNeuron.cs`).
5. **Runtime Lifecycle Management**: By refactoring `AspireRuntimeNeuron` to handle `ConfigureAspireResource`, it stores the dynamic configs. It then dynamically starts/stops/restarts processes via `IAspireBootConnector` (which wraps the `aspire resource` CLI tool), allowing resources configured with `autostart: false` (like `flutter-windows`) to be activated lazily when needed.

---

## 3. Caveats

- **Port & CLI Availability**: Managing multiple dynamic CLI processes concurrently requires robust port availability checking to prevent conflicts, especially if processes are spun up or restarted rapidly.
- **Path Resolutions**: Dynamic path resolution assumes relative paths in `digitalbrain.ino` (e.g., `../../UI/flutter`) are resolvable from the AppHost project directory.

---

## 4. Conclusion

The hardcoded C# configurations in `DigitalBrain.Hosting` can be fully decoupled by:
1. Implementing an `InoTopologyParser` utility that parses `digitalbrain.ino` at AppHost boot time to dynamically register resources.
2. Implementing `IHandle<ConfigureAspireResource>` in `AspireRuntimeNeuron` to capture dynamic synapse configurations.
3. Leveraging the `IAspireBootConnector` within `AspireRuntimeNeuron` to control the dynamic resource lifecycles at runtime.

---

## 5. Verification Method

- **Compilation Verification**:
  Ensure the entire solution builds cleanly under `.NET 11` with:
  ```powershell
  dotnet build DigitalBrain.slnx
  ```
- **Test Invariant Verification**:
  Run the test suite to confirm zero regressions:
  ```powershell
  dotnet run testdigitalbrain.cs
  ```
- **Inspect Key Files**:
  - `kernel/DigitalBrain.Hosting/DigitalBrainHostingExtensions.cs` to verify removal of static C# project dependencies.
  - `sdk/DigitalBrain.SDK/Aspire/Runtime/AspireRuntimeNeuron.cs` to verify integration with `IAspireBootConnector` and implementation of `IHandle<ConfigureAspireResource>`.
