# Contributing

DigitalBrain is being built kernel-first. Contributions should extend the current execution path and keep claims tied to evidence.

## Development loop

From the repository root:

```powershell
dotnet build Brain.slnx
dotnet test --logger "console;verbosity=minimal"
cd hosts/DigitalBrain.AppHost
aspire run
```

Open the Aspire dashboard and select `brain-docs` for documentation hot reload.

## Documentation loop

```powershell
cd website
npm test
npm run build
```

The test suite checks navigation, evidence labels, contributor tutorials, Aspire integration, and the mobile heading guard.

## Architecture rules

- `NeuronGrain` remains the universal execution path.
- `INeuronKind` owns capability-specific behavior.
- Typed module APIs use `INeuronContract` and `NeuronProxy`.
- Synapses record relationships; they do not request work.
- External effects pass through the kernel decision gate.
- Plain values remain plain values.
- Current limits are documented as limits.

## Add a module

Follow [Build your first module](/build/first-module), register it through explicit host composition, and add conformance coverage in `Brain.ConformanceTests`.

If a contribution changes a claim from **Target** to **Implemented**, link that claim to code and tests in the same change.
