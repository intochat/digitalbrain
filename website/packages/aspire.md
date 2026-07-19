---
title: DigitalBrain.Aspire
---

# DigitalBrain.Aspire

The consuming side of the Aspire integration: one call that turns an Aspire-referenced brain into a
usable `BrainClient`.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrainClient(owner: "acme");

var app = builder.Build();

app.MapPost("/greet", async (BrainClient brain) =>
{
    await brain.FireAsync("Greeter", "first", new Hello());
    return Results.Accepted();
});
```

`AddDigitalBrainClient(owner)` wires the Orleans client from the connection information the AppHost
supplied and registers a `BrainClient` bound to that owner.

This package depends on `DigitalBrain.Client` and the Orleans client only. It has no model binding and
no provider SDK, which is the whole point of the split: a service that talks to a brain cannot
accidentally acquire the ability to call a model provider directly, and cannot be handed a key.

For composing the brain itself, see [`DigitalBrain.Aspire.Hosting`](/packages/aspire-hosting).
