# DigitalBrain.Testing.E2E

Runs selected modules — or an application AppHost — with the brain in a **real, separate process**
orchestrated by Aspire, with disposable storage and real HTTP. A browser starts only when a module
runs a frontend, so the same package covers HTTP-only and full browser scenarios.

```csharp
// HTTP only: no frontend process, no browser.
await using var brain = await E2ETest.Create()
    .WithModule<FlutterModule>(flutter => flutter.BackendOnly())
    .StartAsync(ct);
using var response = await brain.HttpClient.PostAsJsonAsync("/workspaces/workspace-a/ui/buttons/go/click", new { }, ct);

// Frontend: E2E opens a page automatically.
await using var app = await E2ETest.Create()
    .WithModule<FlutterModule>(flutter => flutter.RunWebApp())
    .StartAsync(ct);
await Assertions.Expect(app.Page.GetByText("Expected content")).ToBeVisibleAsync();
```

Requires a container runtime. Browser scenarios additionally need Chromium
(`pwsh <test-output>/playwright.ps1 install chromium`).

Credentials belong in `WithExecution(new() { PrivateConfiguration = ... })`, which transports them
through an ACL-restricted temporary file; public module options reject them.

## Consuming from NuGet

This package ships `buildTransitive` targets that stamp the Aspire DCP and dashboard paths onto
your test assembly, resolved from your own NuGet package root for your SDK's runtime identifier.
Without them `Create` fails with "No application host assembly was found".

Two rough edges in `0.1.0-alpha`, both known:

- The targets declare the `Aspire.Hosting.Orchestration.*` and `Aspire.Dashboard.Sdk.*` packages,
  but package-supplied targets are only imported after the first restore writes
  `obj/*.nuget.g.targets`. A clean clone therefore needs `dotnet restore` to run twice, or those
  two packages referenced directly.
- The Aspire version falls back to a literal in the targets file. If you use a different
  `Aspire.Hosting.Testing`, set `AspireOrchestrationVersion` to match.

Nothing here is required for `DigitalBrain.Testing.Unit`, which starts no external process.
