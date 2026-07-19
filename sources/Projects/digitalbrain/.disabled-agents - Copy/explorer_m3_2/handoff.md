# Handoff Report - Explorer 2 (Milestone 3)

Last visited: 2026-05-26T11:27:00+02:00

## 1. Observation

During our sweep of the DigitalBrain SDK and Kernel directories, we observed the following:

- **AspireRuntimeNeuron Definition**: Located in `sdk/DigitalBrain.SDK/Aspire/Runtime/AspireRuntimeNeuron.cs`. It inherits from `Neuron` and implements `ICallNeuronTarget`:
  ```csharp
  [Orleans.GrainType(NeuronTargetFqn)]
  internal sealed class AspireRuntimeNeuron(
      [Microsoft.Extensions.DependencyInjection.FromKeyedServices("incoming")] Orleans.Journaling.IDurableList<Synapse> incoming,
      [Microsoft.Extensions.DependencyInjection.FromKeyedServices("outgoing")] Orleans.Journaling.IDurableList<Synapse> outgoing,
      global::Orleans.IGrainFactory grains,
      global::Microsoft.Extensions.Logging.ILogger<AspireRuntimeNeuron> logger)
      : Neuron(incoming, outgoing, grains, logger), ICallNeuronTarget
  ```
  It resolves `IAspireBootConnector` inside its method bodies dynamically using Orleans service locator (e.g. lines 56, 84, 90, 96, 102):
  ```csharp
  var connector = this.ServiceProvider.GetRequiredService<IAspireBootConnector>();
  ```

- **IAspireBootConnector ABI**: Located in `sdk/DigitalBrain.SDK/Aspire/IAspireBootConnector.cs` and implemented in `AspireBootConnector.cs`:
  ```csharp
  public interface IAspireBootConnector : IAsyncDisposable
  {
      Task<string> SpawnClusterAsync(string profile, CancellationToken ct);
      Task<string> InstallDomainAsync(string domain, CancellationToken ct);
      Task<string> RestartResourceAsync(string resource, CancellationToken ct);
      Task<string> StartResourceAsync(string resource, CancellationToken ct);
      Task<string> StopResourceAsync(string resource, CancellationToken ct);
      Task WaitForShutdownAsync(CancellationToken ct);
  }
  ```

- **ConfigureAspireResource synapse**: Currently defined in `kernel/DigitalBrain.Kernel/OS/OSSynapses.cs` (line 18):
  ```csharp
  [GenerateSerializer]
  public sealed record ConfigureAspireResource(SynapseMetadata Headers, string ResourceName, string ResourceType, Dictionary<string, string> Config) : Synapse(Headers);
  ```

- **GenesisNeuron Parsing & Dispatching**: Located in `kernel/DigitalBrain.Kernel/OS/GenesisNeuron.cs` (lines 91-99). It reads `digitalbrain.ino` topology declarations, calls the `$aspire` grain, and fires the `ConfigureAspireResource` synapse:
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

- **Project Dependencies**: In `sdk/DigitalBrain.SDK/DigitalBrain.SDK.csproj`, the SDK references `DigitalBrain.Kernel.Contracts` but NOT `DigitalBrain.Kernel`. `DigitalBrain.Kernel.csproj` references `DigitalBrain.SDK.csproj`.

---

## 2. Logic Chain

1. **Circular Dependency Hazard**:
   - `DigitalBrain.Kernel` references `DigitalBrain.SDK`.
   - `ConfigureAspireResource` is defined in `DigitalBrain.Kernel.OS` (inside the `DigitalBrain.Kernel` project).
   - If `AspireRuntimeNeuron` (in `DigitalBrain.SDK`) implements `IHandle<ConfigureAspireResource>`, `DigitalBrain.SDK` will have to reference `DigitalBrain.Kernel`, leading to a circular project dependency that fails compilation.
   - *Therefore*, `ConfigureAspireResource` must be relocated to a shared contracts assembly referenced by both (`DigitalBrain.Kernel.Contracts`).

2. **Refactoring of AspireRuntimeNeuron**:
   - `Neuron` base class uses reflection to auto-dispatch received synapses to any implemented `IHandle<TSynapse>` interfaces.
   - By implementing `IHandle<ConfigureAspireResource>` on `AspireRuntimeNeuron`, the synapse stream automatically invokes `HandleAsync`.
   - Under `HandleAsync`, the neuron should extract the config dictionary (e.g. `autostart`, defaulting to `true`), and use `IAspireBootConnector` to dynamically spin up the resource matching the parsed `.ino` parameters.

3. **Dynamic Resource Processing**:
   - The config dictionary contains `autostart`, `port`, `path`, and `args`.
   - If `autostart` is `true` (or unspecified), the handler calls `connector.StartResourceAsync(synapse.ResourceName, CancellationToken)` to boot the project, executable, or container resource.
   - If `autostart` is `false`, the resource remains dormant, matching the `.ino` instruction (`autostart:false`).

---

## 3. Caveats

- We assumed that `IAspireBootConnector` can delegate starting dynamically configured resources via the `aspire` CLI commands (e.g., `aspire resource flutter-web start`). If the underlying resource is not statically declared in the `AppHost/Program.cs` at startup, the CLI command might fail unless the `AppHost` is also refactored to dynamically read from the same catalog at boot time. (This is Explorer 1's domain to coordinate, and we have noted this in our analysis report).

---

## 4. Conclusion

The refactoring of `AspireRuntimeNeuron` to implement `IHandle<ConfigureAspireResource>` is architecturally clean and highly feasible. By moving the `ConfigureAspireResource` synapse definition into the `DigitalBrain.Kernel.Contracts` assembly under the namespace `DigitalBrain.Kernel.OS`, we bypass circular dependency compiler traps entirely. `AspireRuntimeNeuron` can then dynamically resolve `IAspireBootConnector` via its service locator, sync its local resource dictionary, and dynamically spin up configured resources based on the parsed `autostart` flag.

---

## 5. Verification Method

1. **Build and Compilation**:
   Move `ConfigureAspireResource` to `DigitalBrain.Kernel.Contracts` and implement the handler in `AspireRuntimeNeuron.cs`. Run:
   ```powershell
   dotnet build
   ```
   Verify that the compilation completes without circular dependency errors.

2. **Integration Testing**:
   Run the project's test suite to ensure the bootstrap flow works correctly and `ConfigureAspireResource` synapses flow smoothly:
   ```powershell
   dotnet test
   ```
