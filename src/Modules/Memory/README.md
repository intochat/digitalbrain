# Durable memory

`IMemory.Remember`, `Forget`, `PurgeNamespace`, and `Recall` use the Orleans `Default` grain-storage provider. Text, tags, protected-payload references, and embedding vectors are persisted in 64-entry page neurons. Hash collisions allocate overflow pages. Recall reads these canonical pages and computes cosine similarity; it does not depend on Qdrant being available or current.

Qdrant is optional. When its connection is configured, it supplies a legacy import source and a rebuildable index. Writes retain pending projection work in page state. `RebuildIndex(namespace)` rebuilds the projection and returns `Available`, `Indexed`, and `Pending`; a nonzero `Pending` means index work must be retried. A purge immediately attempts removal from the optional index and reports failure while retaining durable retry state. Canonical memory is already removed when that error is returned.

## Existing Qdrant data

Existing Qdrant-only notes are **not** silently treated as authoritative or deleted. Before relying on a migrated owner/namespace, run the explicit import with the exact pre-migration memory neuron key and namespace:

```csharp
var memory = brain.Get<IMemory>(existingOwnerKey);
string? cursor = null;
do
{
    var result = await memory.ImportLegacy(existingNamespace, cursor, limit: 128);
    cursor = result.NextCursor;
} while (cursor is not null);
var projection = await memory.RebuildIndex(existingNamespace);
```

This is an administrative neuron API, not an automatically run startup migration. Use an authenticated authorized context for that owner/workspace. The source scroll and every returned entry are checked against the exact owner key and namespace. Each page copies the complete text, metadata, payload reference, and dense vector before acknowledgement. Retries skip existing canonical keys; they never overwrite newer notes. Tombstones prevent a forgotten note from being resurrected, and a purged namespace blocks legacy import. The source remains intact throughout import. Preserve source backups and record the owner/namespace/cursor inventory until reconciliation is complete.

Entry bounds: 16,384 text characters, 512-character keys/namespaces, 8,192 finite vector dimensions, 64 tags (256-character names, 1,024-character values). Existing data outside these bounds causes an explicit migration error and remains in Qdrant for deliberate handling. Changing embedding dimensions requires a deliberate re-embedding migration; recall does not silently mix dimensions.

The migration is not executed by tests or deployment. Tests use an isolated fake legacy index and verify paging, retries, source preservation, scope isolation, neuron reactivation, index failure recovery, and overflow pages.
