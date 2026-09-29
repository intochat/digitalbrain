# Registry

Registry does three things: lists neuron types, searches those types by vector similarity, and keeps a persistent directory of observed neuron instances.

```csharp
var registry = brain.Get<IRegistry>(RegistryModule.Key);
var types = await registry.Types();
var matches = await registry.Search("wake me tomorrow", cancellationToken: ct);
var active = await registry.Instances(typeId: "timer", activeOnly: true);
var history = await registry.Instances(skip: 0, take: 100);
```

Kernel's silo startup task publishes `ModuleLoaded` signals for selected modules. Its host-local `RuntimeSignals` source retains that module snapshot for late readers. Registry discovers each selected module's public `INeuron` contracts by Orleans alias; it includes descriptions and method signatures, but never invokes them. A changed module snapshot refreshes metadata and the vector index on the next query.

`Neuron` automatically publishes `NeuronActivated` and `NeuronDeactivated` after its lifecycle hooks, even when a derived override does not call base. A bounded live subscription forwards those observations to the persistent Registry neuron at `RegistryModule.Key`. Ordinary activation never waits for Registry, storage, or embeddings. Every silo forwards its own observations to the shared registry; type metadata reflects the selected modules on the silo serving the query.

History retains one row per observed neuron identity: contract aliases, grain key, first/last observation, latest activation ID, and `LastKnownActive`. Deactivation of an older activation does not deactivate a newer one. Reactivation updates the existing row. The registry excludes its own storage neuron to avoid reactivating itself to record its deactivation.

This is observed history, not a guaranteed log of creation or authoritative liveness. Signals can be lost when a subscriber is absent, slow, or unable to write; crashes can leave the last-known active flag set. Graceful shutdown can also stop the observer before all deactivations arrive. No heartbeat or durable delivery mechanism is implied.

Vector search requires `QdrantModule` and a configured embedding provider. Type listing and history work without either. Search returns types, not method IDs, and provider failures propagate to the caller; there is no keyword fallback. Index snapshots include type metadata and vector contents so different compositions and vector spaces do not mix. Old vector snapshots are retained and excluded by scope filtering.

The former capability catalog, app capability sources, unmet-intent board and HTTP endpoint, JSON invoker, and generated agent tools have been removed. Apps owns its manifests; callers own execution; AI uses explicitly registered tools.
