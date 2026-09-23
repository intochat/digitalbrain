# DigitalBrain

Typed, durable agents built on [Orleans](https://learn.microsoft.com/dotnet/orleans/) and orchestrated with
[Aspire](https://aspire.dev). Behavior is expressed as *neurons* — grains with typed state and live signal
subscriptions — and grouped into *modules* that declare their own options, HTTP surfaces and infrastructure.

| Area | Location |
|---|---|
| Runtime, composition and SDK | `src/Modules/DigitalBrain/` |
| Capability modules (AI, Flutter, Google, Supabase, …) | `src/Modules/<Group>/` |
| Test harness packages | `src/Testing/` |
| IntoChat application | `src/Applications/IntoChat/` |

## Building

```bash
dotnet restore DigitalBrain.slnx
dotnet build DigitalBrain.slnx -p:CodeGraphRefresh=false
```

## Testing

Two kinds, described in [src/Testing/README.md](src/Testing/README.md):

```bash
dotnet test --project src/Modules/Time/Tests/Unit/DigitalBrain.Modules.Time.Tests.Unit.csproj
dotnet test --project src/Modules/Google/Flutter/Tests/E2E/DigitalBrain.Modules.Flutter.Tests.E2E.csproj
```

End-to-end tests need a container runtime for their disposable storage, and browser scenarios need
Chromium (`pwsh <test-output>/playwright.ps1 install chromium`).

## Packages

Published from this repository under `0.1.0-alpha`, targeting `net11.0`. The test harness ships as
`DigitalBrain.Testing`, `DigitalBrain.Testing.Unit` and `DigitalBrain.Testing.E2E`.

## License

MIT.
