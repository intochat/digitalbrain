# Scoped identity upgrade

This is a coordinated maintenance upgrade. Do not run old and new identity writers together.

```text
principal ID              -> Principal: password, stable default membership, registration checkpoint
account ID                -> Account: owner, stable brain-creation operation IDs
BrainScope(account, brain) -> BrainAuthority: members, invitations, grants, import checkpoint
```

1. Stop application traffic and every old silo. Back up grain state and encryption keys.
2. Preserve the existing master key. Aspire now names its parameter
   `<brain-name>-digitalbrain-master-key`. Copy the old parameter value to the new
   parameter name; do not generate a replacement for an existing vault. If a legacy
   configured key exists without a scoped key, composition fails with migration instructions.
3. Retain the existing Orleans service ID and backing storage. Aspire resources are now
   prefixed by brain name. Explicitly reuse the existing emulator data volume or point at
   the existing Azure account; changing resource names must not create a fresh data store
   when upgrading an existing deployment.
4. Start a dedicated maintenance host without Platform HTTP endpoints. Normal startup
   only checks compatibility; it never imports legacy state. Stop all other writers first.
   Provide the backup identifier, its complete legacy grant-store inventory, and ownership
   mappings for grant-only brains. Legacy grant keys cannot identify the account themselves.

```json
{
  "DigitalBrain": {
    "Identity": {
      "Migration": {
        "Maintenance": true,
        "SourceSnapshotId": "sha256-of-the-stopped-deployment-backup",
        "LegacyGrantBrainIds": ["owner", "legacy-grant-only-brain"],
        "LegacyBrainAccounts": {
          "owner": "owner",
          "legacy-grant-only-brain": "its-account-id"
        }
      }
    }
  }
}
```

Run the explicit operation from the maintenance host:

```csharp
var directory = grains.GetGrain<IIdentityDirectory>(IdentityGrains.Directory);
var planId = await directory.InspectMigrationAsync(ct); // read-only, complete source preflight
await File.WriteAllTextAsync(planPath, planId, ct);       // retain for retries
await directory.ApplyMigrationAsync(planId, ct);
```

On retry, read the saved plan ID instead of approving a changed plan. Apply recomputes
and compares the source/mapping digest before writing. `SourceSnapshotId` and the inventory
must come from the actual stopped source backup; an operator-provided list is not an
independent proof that storage contains no other legacy grant stores.

Existing memberships and invitations supply their account/brain pairs automatically.
If one legacy brain name belongs to multiple accounts, migration refuses to guess.
Resolve the old records and ownership mapping before retrying. A principal without an
unambiguous owned default membership also blocks migration.

The directory persists the plan ID before storage adoption or imports; each brain import
has a durable checkpoint. Repeating it never replaces live membership or restores revoked grants. Legacy grant stores become sealed, and the directory writes
its final checkpoint only after all imports and seals succeed. An interrupted apply can be
retried with the same plan and configuration. Normal authorization never falls back to old records.
Legacy records, aliases and field IDs remain available for inspection and backup recovery.

5. After apply completes, stop the maintenance host. Start the normal server and verify a
   migrated login, cross-account denial, and the grant list for each mapped scope. Keep the
   maintenance setting disabled on the normal server; resume traffic after verification.
6. Do not roll back only the binaries after accepting new writes. Restore the coordinated
   state/key backup if a rollback is required; the old version cannot read the new authority
   records. Keep the maintenance window open until migration has been verified.

Configured Basic bootstrap credentials own only their default scope: account = configured username, brain = `owner`. They do not grant access to every brain. Existing bootstrap memberships remain subject to revocation.

## Retry semantics

```http
POST /identity/brains
Idempotency-Key: create-research-brain-2026-10
```

The same account and operation key return the same allocated brain. A missing header
creates a new operation. Retry incomplete registration with the same username and password;
the original account and brain IDs survive provisioning failure. Completed registration
still rejects duplicate usernames; use login to recover its session.

One-time grants authorize one attempt. They are consumed durably before authorization
returns; a failed downstream action does not restore the grant. This is not exactly-once
execution of the action.

Browser login requests and token handoffs are bounded, process-local stores. Login follows
issued → begun → claimed; a handoff follows deposited → taken. Taking a handoff is atomic.
After restart, expiration, or a failure after taking tokens, start login again. These stores
do not promise durable OAuth retries.

## Public API changes

```csharp
// Account and brain are always supplied together.
await identity.CanAccessAsync(principalId, accountId, brainId, cancellationToken);
var principal = identity.RequirePrincipal(); // identity only, not an ownership assertion

IConnectionRequests requests; // script surface; alias remains "integrations.accounts"
IConnectionRegistry registry; // privileged surface; grain identity remains "connections"
```

`CallRequest.SemanticTypeIds` and `NeuronActivity.TypeIds` now use arrays. Their field IDs
and Orleans aliases are unchanged; deploy matching contracts with the coordinated upgrade.
Old durable identity snapshots retain their original collection shapes.

Date-time validation returns `DateTimeOffset` and requires an explicit offset (or a UTC
`DateTime`). Unspecified/local `DateTime` inputs are rejected rather than interpreted using
the server's timezone. Existing `x-intochat-*` schema extension keys remain wire-compatible.

## Composition and package policy

```text
module defaults < code options < global module configuration < per-brain configuration
DigitalBrain:Brains:<brain>:Modules:<module>:Options:<property>
```

Aspire resource names are scoped; projected connection names and module IDs remain stable.
Core storage and PostgreSQL composition have a two-brain model regression test.

The packages remain on one synchronized prerelease version train and target `net11.0`.
Do not mix internal package versions. Multi-targeting is deferred until a separately tested
runtime/Orleans compatibility matrix exists; changing a Contracts TFM alone would not make
the runtime support that framework. Strong naming is not enabled; signing would require a
coordinated public-key and friend-assembly change.

```powershell
dotnet pack DigitalBrain.slnx -c Release --no-build -o $feed
```

Packing creates the NuGet artifacts. It does not by itself verify consumers of those packages.

## Deployment identity and release verification

```csharp
var brain = builder.AddDigitalBrain("display-name", serviceId: "existing-service-id",
    dataVolume: "existing-volume", options: new()
    {
        UseAzureStorage = true,
        ClusterId = "current-cluster-id"
    });
```

Service ID identifies the logical deployment; cluster ID identifies cluster membership.
Azure Blob grain names do not include service ID. Platform therefore persists a
`.digitalbrain-deployment.json` binding in the grain-state container and validates both
service ID and an encrypted master-key proof before normal startup. This guards reuse of
an existing store; it does not turn one blob container into separate stores for different
service IDs. Give different deployments separate backing stores.

A legacy store without a binding requires explicit maintenance adoption. Inspect remains
read-only; apply creates the binding after preflight. Existing bound stores reject a changed
service ID or master key in maintenance mode too. The first adoption still requires checking
the supplied key against the deployment's original encrypted data. Changing
cluster ID no longer changes service ID. Resource renames require explicitly retaining
service ID, volume/storage account, and master key. AppHost resource names are scoped;
Orleans provider service keys and projected connection aliases remain stable.

```powershell
dotnet build DigitalBrain.slnx -c Release -p:CodeGraphRefresh=false
dotnet pack DigitalBrain.slnx -c Release --no-build -o artifacts
```

The standalone release scripts and `eng/` sources have been removed. CI runs these ordinary
xUnit integration projects through `dotnet test`:

- `src/DigitalBrain/Platform/tests/DigitalBrain.Platform.Tests.E2E`: read-only inspection,
  process interruption/resume, startup rejection, revocation across retry/restart, original-key
  reuse, wrong-key/service rejection, and snapshot rollback with frozen compatibility fixtures.
- `src/Aspire/tests/DigitalBrain.Aspire.Hosting.Tests.E2E`: pack actual dependencies, restore an
  isolated package-only consumer, compose AppHost/server/client, verify two-brain isolation,
  and rename resources while retaining storage and deployment identity.

Tests own disposable emulator volumes, enforce deadlines, and report diagnostic directories.
They use synthetic credentials and require no production snapshot. Publication remains manual.

Authenticated users select a shared brain with `POST /identity/session/brain` containing
`accountId` and `brainId`. The server checks current membership before issuing a cookie for
that account and brain; sharing never confers owner-only grant permissions.

A rehearsal on a copy of existing deployment state is relevant only when upgrading a
deployment that already has persisted legacy identity data. A fresh installation has no
legacy state to migrate. Preserve the original keys and follow the maintenance procedure
above when an existing deployment does need migration.

Hosted test consumers declare Aspire orchestration/dashboard packages for their build
platform explicitly; NuGet does not restore dependencies injected by package build imports.
