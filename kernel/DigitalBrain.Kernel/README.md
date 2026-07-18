# DigitalBrain.Kernel

The privileged DigitalBrain kernel runtime for Orleans silos.

- `Neuron` — the durable grain base class on official Orleans journaling. Durable state owns DigitalBrain memory, external-operation intent and outcome, and the notification outbox.
- `BrainOwnerIncomingCallFilter` — server-side owner authorization for every neuron call.
- `Quadrant` — the startup-discovered, `Type`-keyed catalog of installed capability interfaces and their implementations; invalid topologies stop silo startup.
- Durable reminders wake outbox recovery; Orleans streams deliver committed notifications and are never the source of truth.

This package is intended only for kernel hosts that receive privileged storage and provider configuration. Ordinary applications use `DigitalBrain.Client`.
