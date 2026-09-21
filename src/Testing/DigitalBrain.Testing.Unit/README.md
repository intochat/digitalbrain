# DigitalBrain.Testing.Unit

Runs selected modules with the brain **in the test process** on an in-process Orleans cluster with
memory storage. No container runtime, no HTTP, no browser.

```csharp
await using var brain = await UnitTest.Create()
    .WithModule<TimeModule>()
    .WithReminders()
    .StartAsync(ct);
var timer = brain.Get<ITimer>("tea");
```

`ConfigureSilo` and `ConfigureClient` install local callbacks and controlled providers.
`DeactivateAsync` exercises activation lifetime and `RestartSiloAsync` restarts the in-process silo.

For transport, process isolation or rendering, use `DigitalBrain.Testing.E2E`.
