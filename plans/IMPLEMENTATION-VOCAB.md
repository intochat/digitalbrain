# Implementation vocabulary (complete-refactoring)

**Authority for code & PRs on this branch:** tip/C# names. No soft dual-speak.

| Term | Meaning | Primary types |
|---|---|---|
| **Neuron** | Logical compute unit; Orleans grain with journal + journal-is-outbox | `INeuron`, `Neuron` |
| **Synapse** | Typed wire **message** (payload), never an edge | `Synapse`, `RequestSynapse<T>` |
| **SynapseAlias** | Orleans `[Alias]` / routing key for a Synapse type | `SynapseAlias` |
| **Connection** | Durable graph **edge** | `SynapseConnection`, Connect/Disconnect |
| **SynapseGraph** | Routing graph of Connections keyed by SynapseAlias | `ISynapseGraph`, `SynapseGraphNeuron` |
| **Emit / Send / Fire** | Emit = fan-out via graph (+ rare `[Broadcast]`); Send/Fire→ = directed | `Neuron` pipeline |
| **Morph** | Connection `Transform` | `ISynapseTransform`, relay grain |
| **Cell** | Neuron subtype, Kind-driven | `ICell`, `CellNeuron` |
| **Kind** | Cell program | `ICellKind` |
| **Behavior** | Neuron for residual I/O (not a Kind) | `BehaviorNeuron` |
| **Integration** | Credentials — not a Connection | token slots / OAuth |

Teaching: Neurons Emit/Send **Synapses** along **Connections** in the **SynapseGraph**.

Doc metaphors (Contract/Edge/ConnectionGraph) are archived in FINAL-ARCHITECTURE / VOCABULARY for history only — **do not** introduce those names into new code or PR titles on this branch.
