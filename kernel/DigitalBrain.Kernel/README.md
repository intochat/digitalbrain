# DigitalBrain.Kernel

The privileged DigitalBrain kernel runtime for Orleans silos.

- `Neuron` — the durable grain base class on official Orleans journaling. Durable state owns DigitalBrain memory, external-operation intent and outcome, and the notification outbox.
- `BrainOwnerIncomingCallFilter` — server-side owner authorization for every neuron call.
- `Quadrant` — the startup-discovered, `Type`-keyed catalog of installed capability interfaces and their implementations; invalid topologies stop silo startup.
- Durable reminders wake outbox recovery; Orleans streams deliver committed notifications and are never the source of truth.

This package is intended only for kernel hosts that receive privileged storage and provider configuration. Ordinary applications use `DigitalBrain.Client`.

Register the production kernel from a host that references a configured
`DigitalBrainResource`:

```csharp
using DigitalBrain.Kernel;

var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainKernel("brain");
```

The registration consumes the projected Orleans cluster identity, Azure Table
clustering and reminders, Azure Blob grain and journal storage, Azure Queue
streams, distinct outbox storage, and typed AI model settings. Startup fails
when any privileged projection is missing or malformed. It never selects
localhost clustering, in-memory reminders, or volatile journal storage.
