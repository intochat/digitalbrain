# DigitalBrain

The open-source C# framework for building AI-native reactive apps and modular neuron systems. [IntoChat OS](https://github.com/intochat/os) and [IntoChat Modules](https://github.com/intochat/modules) are built on it.

## Quick Start

```bash
dotnet new install DigitalBrain.Templates
dotnet new intochat-app -n MyApp
dotnet new intochat-module -n MyModule
cd MyApp && dotnet test
```

## Example: Clean `App<TState>` with Executable `[Scenario]` BDD Verification

See [`examples/Calculator/CalculatorApp.cs`](examples/Calculator/CalculatorApp.cs):

```csharp
[App("calculator")]
public sealed class CalculatorApp : App<CalculatorState>
{
    [On]
    public void Add(CalcBinary input) =>
        Set(new(input.A + input.B, $"{input.A} + {input.B} = {input.A + input.B}", Error: null));

    [On("settings", Contract = typeof(IUserSettings))]
    public void OnSettingsChanged(UserSettingsChanged changed) =>
        Set(State with { Expression = $"Theme: {changed.Theme}" });

    public View Render() => new(
        "Calculator",
        new Column([new Text(State.Expression), new Text(State.Value.ToString())]));

    [Scenario("Adding two numbers updates state and main view")]
    public void VerifyAdditionScenario()
    {
        Add(new CalcBinary(2, 3));
        if (State.Value != 5)
            throw new InvalidOperationException($"Expected 5, got {State.Value}.");
    }
}
```

## Core Vocabulary

| Concept | Type / Attribute | Purpose |
|---|---|---|
| **Neuron** | `[Neuron("type")]` / `Neuron<TState>` | Addressable stateful unit (`brain/type/key`) that runs one turn at a time and persists state + signals atomically via `Save(next, signal)`. |
| **Signal** | `[Signal("alias")]` / `Signal` | Immutable typed fact emitted by a neuron when its state changes. |
| **Reaction** | `[On]` / `[On("sourceKey")]` | Method on a `Neuron` or `App<TState>` that runs when an operation or background neuron signal arrives. |
| **App** | `[App("id")]` / `App<TState>` | Typed reactive UI & behavior unit with `State`, `Set(next)`, `Get<T>(key)`, and `Render()` / `RenderSide()`. |
| **BDD Scenario** | `[Scenario("title")]` / `BddNeuron` (`IBdd`) | Executable specification verified by `BddRunner.VerifyAsync<TApp>()` and recorded in `BddNeuron`. |
| **DDD Module** | `[Module("id")]` / `ModuleManifest` | Self-contained capability or connector unit declaring `Provides`, `Requires`, `Needs` (`Need.HttpClient`, `Need.Webhook`, `Need.Container`), `Auth`, `Settings`, and `Secrets`. |
| **TestOS** | `TestOS.Local()` | Zero-Docker, deterministic in-process runtime for testing neurons, synapses, modules, and apps in milliseconds. |

## Packages

| Package | Description |
|---|---|
| `DigitalBrain` | Core vocabulary (`Neuron<TState>`, `App<TState>`, `Signal`, `BddNeuron`, `ModuleManifest`, SPI) + Roslyn analyzers. Zero Orleans dependency. |
| `DigitalBrain.UI` | Declarative UI value trees (`View`, `Column`, `Text`, `Collection`, `Entry`, `TextField`). |
| `DigitalBrain.Testing` | `TestOS.Local()`, `BddRunner`, `AppBaseline`, capability conformance suites, and fakes. |
| `DigitalBrain.Testing.Silo` | Dual-tier harness running the same fact on `TestOS.Local()` and `SiloHost.Local()`. |
| `DigitalBrain.Analyzers` | Compile-time analyzers enforcing reaction shapes, spec comments, and module invariants. |
| `DigitalBrain.Templates` | `dotnet new intochat-app` and `dotnet new intochat-module`. |
| `DigitalBrain.Runtime.Orleans` | Distributed Orleans runtime for `local` and `local+cloud` topologies. |
| `DigitalBrain.Runtime.Orleans.Azure` | Cloud topology provider on Azure Storage and Key Vault. |
| `DigitalBrain.Runtime.Http` | ASP.NET Core edge seam for module routes, webhooks, and session cookies. |

## Documentation & Ecosystem

- **Framework Documentation**: [`intochat/digitalbrain.docs`](https://github.com/intochat/digitalbrain.docs)
- **Official & Community Modules**: [`intochat/modules`](https://github.com/intochat/modules)
- **IntoChat OS**: [`intochat/os`](https://github.com/intochat/os) & [`intochat/os.docs`](https://github.com/intochat/os.docs)

## License

Licensed under the [MIT License](LICENSE).
