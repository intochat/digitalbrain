# DigitalBrain.Testing.E2E

Runs selected modules — or an application AppHost — with the brain in a **real, separate process**
orchestrated by Aspire, with disposable storage and real HTTP. A browser starts only when a module
runs a frontend, so the same package covers HTTP-only and full browser scenarios.

```csharp
// HTTP only: no frontend process, no browser.
await using var brain = await E2ETest.Create()
    .WithModule<FlutterModule>(flutter => flutter.BackendOnly())
    .StartAsync(ct);
using var response = await brain.HttpClient.PostAsJsonAsync("/ui/buttons/go/click", new { }, ct);

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
