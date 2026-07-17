# Contributing

DigitalBrain is being built kernel-first. Contributions should make the invariant set smaller and clearer, not add parallel runtimes.

## Development loop

```powershell
dotnet build Brain.slnx
dotnet test --logger "console;verbosity=minimal"
aspire run
```

Open the Aspire dashboard and select `brain-docs` to work on this documentation with VitePress hot reload.

## Documentation loop

```powershell
cd website
npm test
npm run dev
```

Before submitting documentation changes:

```powershell
npm run build
```

## Architecture rules

- Commands are typed calls.
- Synapse facts announce durable truth.
- Topology is explicit and governed.
- External effects use one kernel-owned rail.
- Modules do not depend on concrete kernel internals.
- Secrets do not enter journals or projections.
- Plain values remain plain values.

## Proposing a module

A module proposal should name its neuron boundaries, typed contracts, fact schemas, required grants, effect kinds, provider idempotency behavior, UI projections, and Aspire resources.

Start with the [module anatomy](/guide/modules) and record unresolved choices in [architecture decisions](/reference/decisions).
