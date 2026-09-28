# Compute neuron persistence implementation

`ComputeModule` now registers `NeuronLedgerStore`, `NeuronMeterStore`, and `NeuronUsageStore` regardless of whether a PostgreSQL connection is configured. Normal application writes use Orleans `Default` storage. The host selects the existing Azure Blob/Azurite provider; these stores do not open application files or SQL connections for new writes.

## Ownership and commit behavior

* Ledger records are scoped to an account head, keyed by the existing account/idempotency-key pair. Wallet charges and platform costs retain their existing kinds and totals. Wallet and cost-ledger actors share this persistent record owner, so duplicate concurrent writers insert one entry.
* Meter records retain the existing `(IntentId, MeterId, Step)` uniqueness constraint. An intent batch publishes a single head update. The existing global meter-read contract uses one catalog head; it can become a write bottleneck at high throughput.
* Usage records are scoped to account/workspace. Updating a receipt preserves its original sort key and rejects older revisions. Existing timestamp cursors remain scoped and compatible.
* Each head stores only an immutable root reference and migration-completed flag. Copy-on-write radix-tree branches contain at most 16 references; leaves contain at most 64 records and normally at most 128 KiB of JSON. Individual payloads are capped at 1 MiB, IDs at 4096 characters, sort keys at 256 characters, and batches at 1000 records. Larger batches fail explicitly; they are not silently split into partially committed transactions.
* Immutable parts commit before the head. A child/storage failure leaves the published root unchanged. Retries inspect the existing idempotency key, and the non-reentrant head actor serializes publication. Failed attempts can leave unreferenced immutable parts; retention/garbage collection is a separate operational task.

The tree bounds persisted grain state. Compatibility APIs for ledger and meter `ReadAsync` still return complete histories and can allocate large result arrays. Usage paging scans immutable leaves while retaining only a bounded result page; this is correct but its work grows with history size. A dedicated ordered index would improve large-history latency without changing ownership.

## Legacy migration configuration

Set `DigitalBrain:Compute:ImportLegacy=true` explicitly to import old data as each account/workspace is first accessed. Keep legacy writers stopped during migration. By default the flag is false and legacy sources are not read.

* With `DigitalBrain:Compute:ConnectionString` or connection string `compute`, migration reads existing PostgreSQL ledger, meter and usage tables. Adapters run in read-only mode: no inserts, schema initialization, or schema alteration. They expect the existing application schema, including usage `revision`.
* Without a connection string, migration reads the configured `DigitalBrain:Compute:UsageDirectory`, or the previous `%LOCALAPPDATA%/DigitalBrain/compute-usage/<content-root-hash>` default. The legacy file adapter is read-only. Legacy in-memory wallet/meter contents from a terminated process cannot be recovered.
* Import preserves IDs, ledger amounts, meter fields, usage timestamps and usage revisions. It never deletes or updates source data. It reuses normal neuron deduplication and commits an import-complete marker only after every source page is read and persisted. Interrupted imports retry idempotently. A source read failure blocks dependent writes; it does not reset the account balance or substitute empty data.
* Imported scopes can subsequently operate with the old host/database unavailable. The flag may remain enabled; completed scopes bypass their legacy source. Disabling it before all intended scopes have been imported hides those scopes' old records until migration is re-enabled. There is no automatic enumeration of every historical account in this change.
* Verify all required accounts/workspaces and counts before retiring the legacy database or files. No actual user data was read or migrated during implementation.

## Verification

Release Compute unit suite: 40 passed, 1 skipped. The skipped test is `LedgerFacts.PostgresLedgerEnforcesIdempotencyWhenADatabaseIsAvailable`, which requires `DIGITALBRAIN_COMPUTE_TEST_POSTGRES`. New tests cover bounded immutable records, failure before root publication, grain deactivation, batch deduplication, stale receipt revisions, pagination/scope isolation, legacy import with source removed after completion, blocked writes when a legacy ledger is unavailable, and read-only file protection. These are test-host checks; a production restart against the retained Azurite volume remains a deployment verification step.
