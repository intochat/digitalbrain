# AppName

An IntoChat app that follows the app convention.

| File | Holds |
| --- | --- |
| `AppName/AppName.cs` | The `[App]` class: one `[On]` method per operation |
| `AppName/AppName.spec.md` | The spec comment: the description, `Role`, `Shipped`, `Operations` and the scenarios |
| `AppName.Tests.Unit/AppNameFacts.cs` | The generated baseline theory (`AppBaseline`) and one fact per scenario |

```
dotnet build -warnaserror
dotnet test --project AppName.Tests.Unit/AppName.Tests.Unit.csproj
```

The tests run on `TestOS`, without a runtime, a silo or a network. The DigitalBrain analyzers check the convention in the build:
an operation that the spec does not describe, a key built from strings or an empty catch fails it.

`Open` is stateless. `CounterNeuron` is the durable state example: its frozen alias and explicit `[Field(0)]` identify stored data,
while `Save` commits a new state and its `Counted` signal together. The persistence test restarts TestOS and checks the saved count.
`AppJsonContext` owns the source-generated JSON metadata. Add every new operation input, neuron state, signal, private contract
parameter/result and stream item there. The opt-in `DigitalBrainCompiledApp` property enables the package-owned invocation proxy factory.

Build a compiled artifact with `intochat pack AppName/AppName.csproj -o AppName.intochat-app` using the locally built CLI tool.
The artifact contains managed app dependencies and assets, with framework assemblies declared as exact shared versions. It contains no source or PDBs.
Private ports in an app constructor require explicit owner-approved bindings from the host, scoped to the installed app's brain;
the starter's `Open` does not need such a binding. No Orleans APIs are needed to author or test the app.
