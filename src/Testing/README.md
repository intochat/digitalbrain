# Testing

`DigitalBrain.Testing` provides the shared `BrainSimulation`: an in-process Orleans
cluster with DigitalBrain runtime registration, journal waits, optional file-backed
persistence, restart support, and journal fault injection.

Each module owns a `Tests/` project. IntoChat owns its application tests in
`src/Applications/IntoChat/Tests`. These projects import `Module.Tests.props`, which
provides xUnit v3, Microsoft.Testing.Platform, and the shared testing framework.

The runtime project contains a small simulation lifecycle test. The other projects
are intentionally empty starting points; the old suites and production fake
providers have not been restored. Modules added to a simulation use their normal
configuration and providers unless the test explicitly supplies dependencies through
`ConfigureSilo`.

Run the suite from the repository root:

```powershell
dotnet test --solution DigitalBrain.slnx --ignore-exit-code 8
```

Exit code 8 means a project contains no tests. Ignoring it allows the empty projects
to coexist with the runtime smoke test; test failures still fail the command.
