---
title: DigitalBrain.DevTools
---

# DigitalBrain.DevTools

Things that make local development pleasant and would be a liability in production. Every surface here
is guarded by `IHostEnvironment.IsDevelopment()`.

```csharp
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDevelopmentJournalStorage()
    .AddDigitalBrainDevTools(builder.Environment));

app.MapDigitalBrainDevTools(app.Environment);
```

## In-memory journals

`AddDevelopmentJournalStorage()` registers Orleans' `VolatileJournalStorageProvider`. It lives here
rather than in the kernel on purpose: the kernel refuses to start without durable journal storage, and
the escape hatch that makes a first run frictionless should not be reachable from a production wiring
path by accident.

Journals kept this way do not survive a process restart. That is fine for a quickstart and wrong for
anything else.

## Dashboard

`AddDigitalBrainDevTools(environment)` adds the Orleans Dashboard and `MapDigitalBrainDevTools` serves
it at `/dashboard`. Both calls are no-ops outside Development, so leaving them in a shared host does
not expose cluster internals in production.

::: warning Open debt
`Microsoft.Agents.AI.DevUI` is pinned but not wired. A brain has no interactive chat surface yet.
:::
