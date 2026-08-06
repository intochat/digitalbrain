# DigitalBrain

DigitalBrain is a clean-room, durable behavior model. Its Core is intentionally
small: module authors write synapse vocabulary and behavior, while Hosting owns
the durable runtime.

## Current packages

| Package | Responsibility |
| --- | --- |
| `DigitalBrain.Abstractions` | `Synapse`, `INeuron<TSynapse>`, and `NeuronId` |
| `DigitalBrain.Core` | Pure `Neuron` behavior facade, optional turn state, and public journal-read types |
| `DigitalBrain.Access` | Trusted `SynapsePublisher` and `JournalReader` capabilities |
| `DigitalBrain.Hosting` | Explicit composition, serialization, the durable Orleans adapter, journal recording, routing, and post-record delivery |
| `DigitalBrain.Testing` | Real-cluster mechanical test support |
| `DigitalBrain.Mocks` | Small vocabulary fixtures used to exercise mechanics, not product implementations |

The package boundary is strict:

```text
module ──> Abstractions + Core
Access ──> Core
Hosting ──> Abstractions + Core + Access + Orleans
```

Start with [the current Core architecture](CORE-ARCHITECTURE.md) and the
[Core README](src/DigitalBrain.Core/README.md). Earlier research and design
material remains as historical evidence rather than current authority.
