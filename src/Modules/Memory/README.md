# Durable memory

`IMemory.Remember`, `Forget`, `PurgeNamespace`, `Recall`, and `RebuildIndex` use the Orleans `Default` grain-storage provider. Text, tags, protected-payload references, and embedding vectors are persisted in 64-entry page neurons. Hash collisions allocate overflow pages. Recall reads these canonical pages and computes cosine similarity; it does not depend on Qdrant being available or current.

Qdrant is optional. When its connection is configured, it provides a rebuildable index for the canonical memory neurons. Writes retain pending projection work in page state. `RebuildIndex(namespace)` rebuilds the projection and returns `Available`, `Indexed`, and `Pending`; a nonzero `Pending` means index work must be retried. A purge immediately attempts removal from the optional index and reports failure while retaining durable retry state. Canonical memory is already removed when that error is returned.

Entry bounds: 16,384 text characters, 512-character keys/namespaces, 8,192 finite vector dimensions, 64 tags (256-character names, 1,024-character values).
