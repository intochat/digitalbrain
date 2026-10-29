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
and point the consumer at it with a NuGet config that also lives outside the repositories:

```bash
dotnet pack DigitalBrain.slnx -c Release -o <feed> -p:VersionSuffix=preview.local.$(date +%y%m%d%H%M%S)
dotnet pack templates/DigitalBrain.Templates/DigitalBrain.Templates.csproj -c Release -o <feed> -p:VersionSuffix=preview.local.<same stamp>
```

In the consumer, restore with `dotnet restore --configfile <outside-config>`, where the config maps `DigitalBrain*`
to `<feed>` and everything else to nuget.org. Never commit a local feed path into any repository.
`scripts/pack-local.ps1` and `scripts/pack-local.sh` pack a fixed `preview.N` into a feed folder the same way.

## Pull requests

- Keep commits focused; `main` is protected by the `ci` gate.
- Do not add packages beyond the seven `DigitalBrain*` ids (an architecture fact enforces this).
- Do not create releases or tags; the maintainers cut releases.
