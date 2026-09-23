# 0003 — Stored-state migration

- Status: accepted
- Date: 2026-09-23
- Deciders: plan task P2.4 (autonomous implementation worker)
- Supersedes: none
- Related: plan P2.4 / P2.5 (WS-N, C14); highlevel T1; `docs/product/decisions/README.md`

## Context

The kernel persists neuron state through Orleans grain storage (`Neuron<TState>` over
`IPersistentState<TState>`), and the AppHost configures that storage as Azure Blob under the
`digitalbrain-v2-state` container (`DigitalBrainNames.DefaultGrainStorage`,
`src/Modules/DigitalBrain/Kernel/Aspire/DigitalBrainRuntimeHostingExtensions.cs:29-38`).

Four application stores instead kept their own JSON on local disk through
`DurableDocumentStore<T>` (`src/Modules/Coding/Coding/Drafts/CodeDraftStore.cs`):
`CodeDraftStore`, `BehaviorProgramStore`, `BehaviorCatalogStore` and `BehaviorLogStore`. That is a
second persistence model: single-host, path-bound, absent from backups, and impossible to operate in
a hosted deployment.

T1 asks for a stored-state migration policy before it is needed. No design partner has been onboarded
yet, so the clean-break rule applies: a breaking persisted-state change may adopt a new storage
container/name and need no in-place migration, recorded here.

## Decision

- **One persistence model**: all durable state lives in cluster storage (Orleans grain persistence —
  Azure Blob in hosted deployments, in-memory in unit tests). The four local-disk JSON call sites are
  migrated to `GrainDocumentStore<T>` and `DurableDocumentStore<T>` is deleted. The product profile
  writes no JSON to the local filesystem.
- **Qdrant carries a data volume** (`QdrantHostingOptions.PersistentStorage`, on by default) so vector
  collections survive container restarts.
- **Migration policy (T1):** until the first design partner is onboarded, a breaking change to a
  persisted type or grain key may use a clean break under a new storage container/namespace name,
  recorded in an ADR. After the first design partner: every change to a persisted type or grain key
  ships with a versioned migration and a restore-tested backup, and serialized members change only by
  adding or removing `[Id]`s — never by changing a member's type.

## Rationale

Cluster storage is already provisioned, backed up and monitored for every neuron; reusing it removes a
bespoke durability path and makes the product profile single-model. The clean break is the smaller,
lower-risk move while there is no partner data to preserve.

## Consequences

- Deleted: `DurableDocumentStore<T>` and the `root`-path construction of all four stores.
- Added: `IDocumentStore<T>`, `GrainDocumentStore<T>`, `InMemoryDocumentStore<T>` (test double), the
  `brain-document` and `brain-document-index` grains, `IDocumentGrain`, `IDocumentIndexGrain`.
- `Neuron<TState>.Save` restores the prior state when `WriteStateAsync` fails, so a failed persistence
  write no longer leaves un-persisted state in memory.
- **Persisted-state change / migration:** this is a clean break under the T1 rule above (no design
  partner onboarded). No in-place migration is written; existing local-disk JSON is abandoned, not
  imported. The Qdrant volume is additive.
- The restore-from-backup drill is a hosted-deployment concern owned by P2.5; this ADR fixes the
  policy it exercises.
- If the owner changes the "first design partner" boundary, this policy is re-planned; the clean break
  is void from that point.
