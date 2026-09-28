# Neuron persistence implementation and rollout

Implemented after approval on 2026-09-28. The earlier settings work is commit `402467575`. This migration has not been applied to the running user application.

## Ownership

| Data | Durable authority | Client / legacy behavior |
| --- | --- | --- |
| Shell projects, settings, drafts, transcripts, artifact/editor data | Account + principal scoped Orleans head; immutable records and bounded hierarchical pages in Default Blob storage | Flutter is an in-memory projection. Startup does not read legacy device preferences. |
| Images and saved copies | Scoped paged asset neurons and immutable `intochat-assets-v1` payload blobs | Host folders are explicit import sources. Missing legacy snapshots migrate by verified digest on access. |
| Image save operations | Persistent prepare/upload/commit operation neuron and existing document receipts | No new filesystem journal, lock or required host output copy. |
| Compute ledger, meters, usage | Bounded immutable record tree plus atomic neuron root | No production file/in-memory/SQL writes. Optional legacy SQL/file readers migrate once per scope. |
| Memory text, tags, payloads, embeddings | Bounded page neurons with overflow | Canonical cosine search works without Qdrant. Qdrant is an optional projection/import source. |
| Cookie and owner-key protection | Shared Blob key ring `intochat-protection-v1`, stable application name `IntoChat.v1` | No new per-machine key files. Historical sessions may require login again. Raw Basic passwords are no longer written to browser storage. |

Azure Blob payloads and the bootstrap key ring intentionally do not serialize through a business grain. Metadata, operations and business state do. Azurite's backing volume remains necessary.

## Save and recovery behavior

`GET /shell/state` returns `{revision,snapshot}`. PUT commands include `expectedRevision`, `operationId`, and a version-1 snapshot. Immutable child records are uploaded before the head is published, so failed writes do not expose half a snapshot. Stale revisions return 409. Every persisted part has a size limit; large strings, object fields and arrays are partitioned. Orphan immutable parts are retained; no destructive garbage collector is introduced.

`POST /shell/import` checks persistent import receipts before revision checks. The optional migration adapter derives the receipt ID from the exact legacy bytes and storage key and verifies the server response with another GET. It is not wired into normal application startup. Different project IDs merge; conflicting copies of a project are rejected, preserving both sources. The UI can explicitly use the server version after confirmation without deleting the old local copy. Unscoped/offline legacy snapshots are not silently assigned to an authenticated account.

Network/save failures keep work open and visibly unsaved. Closing an offline client can lose unacknowledged edits because there is deliberately no durable local fallback. This is shown in the recovery flow.

## Existing installation rollout

1. Keep the existing Azurite volume and `digitalbrain-v2-state` container. `Orleans:ServiceId` is stable (`intochat` by default), independent of random development ClusterId. Existing explicit ServiceId/ClusterId configuration takes precedence. Blob grain names are unchanged by this correction. Existing random-ID reminders must be re-registered by their module.
2. Start this version under the original Windows identity before retiring that profile. After the silo starts, `SecretKeyMigration` inventories persisted vault blob names and activates each vault. Activation unwraps the old DPAPI/fake-vault key, rewraps it using the shared protection ring, and durably acknowledges it. Failure prevents successful startup; logs report a count, never secret values. No old source is deleted.
3. Hosted deployments must supply `IntoChat:DataProtection:Certificate` (base64 PFX) and optional `CertificatePassword` through secret configuration. Past certificates can be supplied in `PreviousCertificates` entries `{Pfx,Password}` for rotation. Development uses an unencrypted Azurite key ring, explicitly avoiding machine-bound DPAPI. Production rejects that development ring: migrate/re-encrypt it before promoting the same storage account. Do not copy plaintext development key material into production and assume configuring a certificate retroactively encrypts it.
4. Normal Flutter startup reads only server state. An empty server creates one default Personal workspace. Legacy device preferences are not imported automatically, so deleting server storage does not resurrect old local workspaces. Any legacy import must be deliberately invoked through the migration adapter/API. Already imported projects remain server state until explicitly reset.
5. **Before using existing Compute balances/history, enable `DigitalBrain:Compute:ImportLegacy=true`.** AppHost uses an explicitly configured `DigitalBrain:Compute:ConnectionString` / connection string `compute` first; otherwise an explicit `UsageDirectory` selects the old file source. Without either override it attaches the historical `compute-postgres` volume as a read-only migration source. Default new installations do not start a Compute SQL database. Visit/read the relevant accounts and workspace histories to complete their imports; keep the flag/source available until all required scopes have been verified. See [Compute details](2026-09-28-compute-neuron-storage.md). Live SQL import was not executed in this task.
6. Import each existing Memory owner/namespace using the cursor loop documented in [Memory README](../../src/Modules/Memory/README.md). Then optionally rebuild the Qdrant projection. Import preserves newer canonical entries and tombstones and never deletes the old Qdrant source. This scoped migration was tested against a fake legacy source, not the running user's Qdrant collection.
7. Open legacy image documents while the old asset directory is still available; verified copies become durable Blob assets. New saves are durable workspace assets. Do not remove old asset directories until the required documents have been verified.

## Validation

- Flutter shell: 101 tests passed; analyzer clean.
- Compute: 40 unit tests passed; one external PostgreSQL test skipped because no test connection was provided.
- Memory: 9 unit tests passed, including overflow, reactivation, legacy import and index failure recovery.
- IntoChat full suite: 124 unit tests passed; 4 pre-existing repository-fixture failures remain (missing operational docs/scripts and ADR files). After the final certificate changes, all 6 targeted shell/protection tests passed, including certificate rotation and production plaintext-ring rejection.
- Kernel secret tests: 6 passed, including rewrap retry and rollback on persistence failure.
- Real isolated Azurite HTTP integration: cross-client restoration, import replay and stale-write rejection passed.
- Fresh browser without workspace local storage, real image-neuron integration, and the browser image save/reopen workflow passed.
- Server resource restart check is not verified: Aspire restarted the process, but Orleans kept contacting the departed silo and did not regain health within the test window. The dedicated restart regression is explicitly skipped pending membership-recovery investigation; cross-client persistence remains independently tested.

The running user's application was not restarted or cut over. Tests used disposable fixtures. Full host relocation and migration of real legacy SQL/Qdrant data remain deployment checks; they are not implied by passing unit tests.

## First-import timeout correction

A real existing workspace (six projects, approximately 1 MB) exposed a first-import timeout after the initial migration. The server remained healthy and eventually persisted revision 1, but the client had already stopped waiting. A synthetic 1 MB nested transcript reproduced 6,021 sequential record writes; the earlier fresh-workspace tests were too small to catch this.

Workspace storage now packs small subtrees and transcript pages into records capped at 64 KiB, writes immutable records in batches of at most 16, and resolves independent reads concurrently with a 16-call limit. Per-read memoization fetches each shared record once. The reader remains compatible with the previous partition format; existing state requires no deletion or reset. Root publication still happens only after all child writes complete.

The regression suite includes a 1 MB import using the same 30-second deadline as the client, import replay, and previous-format reads. If the original slow import has already completed, Retry loading recovers the client using the persisted import receipt.
Validation after this correction: all six shell persistence unit tests and both real-Azurite HTTP tests passed, including the established-workspace import/read/replay within a single 30-second deadline.

## Server reset behavior correction

Automatic legacy preferences import was removed from WorkspaceApp startup. The old Windows preferences file survives clean builds and Docker volume deletion; previously it silently repopulated the empty server. The file is retained but unused by startup. A regression widget test verifies an empty server boots with exactly one Personal workspace and no preferences provider or import request. All 11 persistence tests passed; Flutter analysis was clean. Rebuild the Flutter client to apply this correction; already imported server state is not deleted by the code change.
