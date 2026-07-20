---
title: DigitalBrain.Kernel
---

# DigitalBrain.Kernel

The domain-neutral neuron runtime. Reference it from a silo that hosts neurons.

```csharp
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDigitalBrainJournalStorage(builder.Configuration));
```

`AddDigitalBrain()` is generated in the consuming silo compilation. It configures the core runtime,
validates AppHost-selected modules against the generated catalog, and activates only selected
modules. `AddDigitalBrainJournalStorage` refuses to start without the durable `journal` connection
used by production hosts.

Kernel owns journals, synapse delivery, placement, authorization, dedupe, broadcast wiring, and
telemetry. It contains no AI SDK, prompt API, provider name, UI contract, or integration auth.
