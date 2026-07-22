---
title: DigitalBrain.Aspire.Hosting
---

# DigitalBrain.Aspire.Hosting

Core AppHost composition for a brain:

```csharp
var brain = builder.AddBrain("brain")
    .WithDevelopmentStores();

builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain);

builder.AddProject<Projects.Api>("api")
    .WithReference(brain.AsClient());
```

`AddModule<TModule>()` selects a module exactly once and gives its hosting package a scoped
configuration callback. `WithReference(brain)` projects the selected module manifest and
module-owned resources to a silo. `WithReference(brain.AsClient())` projects only the Orleans client
connection.

Domain-specific resources do not belong here. AI configuration lives in
[`DigitalBrain.Modules.AI.Aspire.Hosting`](/packages/ai-aspire-hosting).
