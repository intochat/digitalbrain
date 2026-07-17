# Modules

A module adds a coherent capability to DigitalBrain without changing the kernel.

## Package anatomy

| Package | Contains |
| --- | --- |
| `*.Contracts` | Typed neuron interfaces, commands, results, and fact schemas |
| `*.Runtime` | Neuron implementations and deterministic domain logic |
| `*.Connector` | Provider clients, authentication, webhook verification, and effect execution |
| `*.UI` | Governed projections and native block producers |
| `*.Hosting` | Aspire and dependency-injection registration |

A convenience package may reference the pieces needed for the common installation path.

## Example: long-term memory

A memory module can wrap a library such as `agent-memory-dotnet` without making that dependency part of the kernel:

```text
DigitalBrain.Memory.Contracts
DigitalBrain.Memory.Runtime
DigitalBrain.Memory.Connector.AgentMemory
DigitalBrain.Memory.UI
DigitalBrain.Memory.Hosting
```

The module owns its storage vocabulary, retrieval contracts, connector adaptation, and workspace projections. The kernel sees registered neuron contracts and governed effects.

## Manifest

Every module declares:

- Stable module identifier and semantic version.
- Supported kernel contract range.
- Exported neuron contracts.
- Fact and topology schemas.
- Required grants.
- External effect kinds.
- UI projection kinds.
- Hosting dependencies.

The manifest is build-time metadata. Runtime reflection is not the primary discovery mechanism.

## Trust boundary

First-party modules may run in process. Community modules need an explicit trust policy, compatibility gates, and isolation strategy before arbitrary code is loaded into the kernel silo.

The initial ecosystem can be conservative: contracts and connector processes are extensible first; in-silo runtime loading remains governed.
