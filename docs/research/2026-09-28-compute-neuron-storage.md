# Compute neuron persistence implementation

`ComputeModule` now registers `NeuronLedgerStore`, `NeuronMeterStore`, and `NeuronUsageStore` regardless of whether a PostgreSQL connection is configured. Normal application writes use Orleans `Default` storage. The host selects the existing Azure Blob/Azurite provider; these stores do not open application files or SQL connections for new writes.

## Ownership and commit behavior

* Ledger records are scoped to an account head, keyed by the existing account/idempotency-key pair. Wallet charges and platform costs retain their existing kinds and totals. Wallet and cost-ledger actors share this persistent record owner, so duplicate concurrent writers insert one entry.
* Meter records retain the existing `(IntentId, MeterId, Step)` uniqueness constraint. An intent batch publishes a single head update. The existing global meter-read contract uses one catalog head; it can become a write bottleneck at high throughput.
* Usage records are scoped to account/workspace. Updating a receipt preserves its original sort key and rejects older revisions. Existing timestamp cursors remain scoped and compatible.
* Each head stores only an immutable root reference. An unused migration-completed flag remains on the persisted state so older grains still deserialize. Copy-on-write radix-tree branches contain at most 16 references; leaves contain at most 64 records and normally at most 128 KiB of JSON. Individual payloads are capped at 1 MiB, IDs at 4096 characters, sort keys at 256 characters, and batches at 1000 records. Larger batches fail explicitly; they are not silently split into partially committed transactions.
* Immutable parts commit before the head. A child/storage failure leaves the published root unchanged. Retries inspect the existing idempotency key, and the non-reentrant head actor serializes publication. Failed attempts can leave unreferenced immutable parts; retention/garbage collection is a separate operational task.

The tree bounds persisted grain state. Compatibility APIs for ledger and meter `ReadAsync` still return complete histories and can allocate large result arrays. Usage paging scans immutable leaves while retaining only a bounded result page; this is correct but its work grows with history size. A dedicated ordered index would improve large-history latency without changing ownership.

## Verification

Compute history is grain-only. The SQL and file import path, including `DigitalBrain:Compute:ImportLegacy`, has been removed. Old databases and usage files are not read.
