# Contributing

## Build and test

```bash
dotnet format DigitalBrain.slnx --verify-no-changes
dotnet build DigitalBrain.slnx -c Release
dotnet test --solution DigitalBrain.slnx -c Release
```

The Azure integration facts and the framework e2e facts need Azurite. Set `AZURITE_CONNECTION_STRING`, or have Docker
running and the facts start `mcr.microsoft.com/azure-storage/azurite` themselves. Without either they skip locally;
with `CI=true` they fail.

Template smoke (outside the repository, from freshly packed packages):

```bash
dotnet pack DigitalBrain.slnx -c Release -o artifacts/packages
dotnet pack templates/DigitalBrain.Templates/DigitalBrain.Templates.csproj -c Release -o artifacts/packages
bash scripts/ci/template-smoke.sh local /tmp/db-smoke artifacts/packages
```

## Local-feed inner loop

To try a change in a consuming repository before it is published, pack into a flat folder outside every repository
and point the consumer at it through its optional, git-ignored `Directory.Build.local.props`:

```bash
dotnet run scripts/pack-local.cs -- --feed <folder>
```

Each consumer's `Directory.Build.props` imports `Directory.Build.local.props` when present. In the consumer, create
`Directory.Build.local.props` (git-ignored) setting `<RestoreAdditionalProjectSources>` to `<folder>` and, when needed,
overriding a `PackageVersion` with `Update`. Never commit a local feed path into any repository.
Each repository (`digitalbrain`, `modules`, `os`) maintains its own `VERSION` independently.

## Pull requests

- Keep commits focused; `main` is protected by the `ci` gate.
- Do not add packages beyond the seven `DigitalBrain*` ids (an architecture fact enforces this).
- Do not create releases or tags; the maintainers cut releases.
