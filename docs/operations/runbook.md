# IntoChat operations runbook (C17)

Audience: the platform operator running the hosted product deployment. Scope: one silo per
deployment (T6), Azure Container Apps + Azure Blob grain storage + PostgreSQL ledger + Qdrant
vectors. Nothing here requires the developer profile; the Windows developer executor is absent in
hosted deployments.

## Deployment shape

- **Product profile only.** The container image bakes the product module list
  (`DigitalBrain__Modules__0..8`: AI, Memory, ClickHouse, Supabase, Time, Gmail, Salesforce,
  GitHub, Flutter) and sets `IntoChat__Hosted__Enabled=true`. Coding, Behavior, Roslyn, DotNet and
  Aspire are not in the image, so the Windows developer executor cannot run.
- **Managed identity + Key Vault (config only).** Set `AZURE_CLIENT_ID` (user-assigned managed
  identity) and `DigitalBrain__KeyVault__Uri`. The runtime resolves secrets by reference; no secret
  is baked into the image. The `IntoChat:Hosted:*` keys are forwarded by the AppHost hosted profile.
- **Telemetry.** OTLP goes to `ops/otel/collector.yaml`, which tail-samples and forwards to the
  persistent backend (`OTEL_BACKEND_ENDPOINT`). Receipts are durable records; they are never derived
  from sampled traces.
- **Publish/roll forward.** `.github/workflows/deploy.yml` publishes the image and rolls the
  container app revision, gating on the new revision name before traffic.

## SLO and alerts

- **SLO:** 99.5 % monthly availability for `/agent` and window reads, measured from the durable
  receipts and the collector's success metrics.
- **RPO 24h, RTO 4h.**
- Alerts (page the operator):
  - availability burn rate > 2× over 1h or > 1× over 6h;
  - `/agent` p95 latency > 10s for 10 min;
  - error-rate on `RUN_ERROR` / `AGENT_FAILED` receipts > 2 % for 15 min;
  - Compute reconciliation drift > 1 % (P2.2) or any actual > approved limit (P3.1);
  - backup job failed two nights in a row;
  - collector queue near capacity or backend exporter failing.

## Incident response

1. **Triage from receipts first.** Every intent has a durable receipt keyed by intent id. Use the
   in-app **Report a problem** entry (it attaches the intent id) or query receipts by intent id.
2. **Correlate traces.** Find the trace by `intochat.intent.id` in the telemetry backend. Tail
   sampling keeps every error and slow trace, so the intent's trace is present.
3. **Classify.** Platform fault, provider outage, model retry inside success, cancellation / revoked
   grant / limit, or third-party app fault (Principle 8). Charging follows the class.
4. **Contain.** If a single capability is at fault, disable the connection or app install; if the
   silo is unhealthy, roll back (below).
5. **Close out.** Record the incident, the affected intent ids, the charge class and the fix.

## Backup and restore

Nightly jobs (02:00 UTC, `ops/backup/nightly-backup.yaml`) back up:

| Target | Source | Snapshot |
|---|---|---|
| `grain-storage` | Azure Blob `digitalbrain-v2-state` | `grain-state.txt` listing |
| `ledger` | PostgreSQL Compute ledger | `ledger.dump` |
| `qdrant` | Qdrant collection | `qdrant.snapshot` |

Restore into a fresh stack:

```sh
ops/backup/restore.sh --snapshot /backups/2026-09-22 --target all
```

Validate without touching production:

```sh
ops/backup/restore.sh --snapshot /backups/2026-09-22 --dry-run
```

**Quarterly restore drill.** Stand up a fresh stack, run the restore, then confirm `/health` is
healthy and a seeded J1 intent reads back from restored grain state. The drill is exercised by the
harness-gated `RestoreDrillFacts` (`DIGITALBRAIN_E2E_RESTORE_DRILL=1`).

## Upgrade and rollback

1. **Upgrade.** Publish a release; `deploy.yml` pushes the image and updates the container app.
   Wait for the new revision to take traffic, then verify `/health` and one J1 intent.
2. **Rollback.** Point the container app at the previous image tag and wait for that revision to
   take traffic. Because grain storage is external and additive, no state migration is needed for a
   rollback inside the same migration generation.
3. **Migration rule (T1).** Before the first design partner: clean break under a new storage
   container/namespace name, recorded in an ADR (`docs/product/decisions/0003-stored-state-migration.md`).
   After: every persisted-type or grain-key change ships with a versioned migration and a
   restore-tested backup.

## Deletion

Deleting a workspace or account runs the deletion hook: it purges the workspace's vectors from the
Memory module's Qdrant collection and queues its backup objects for purge. A denied read looks the
same as a missing one. See `WorkspaceDeletion` and the `DELETE /workspaces/{workspaceId}` endpoint.

## Support entry point

The in-app **Report a problem** action posts to `POST /workspaces/{workspaceId}/reports` with the
intent id it was raised from. Support joins that intent id to the receipt, trace and statement line.
