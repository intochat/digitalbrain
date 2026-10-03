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
4. Start only the new version with migration enabled. List every grant-only legacy brain
   under its owning account; legacy grant keys cannot identify the account themselves.

```json
{
  "DigitalBrain": {
    "Identity": {
      "Migration": {
        "Maintenance": true,
        "LegacyBrainAccounts": {
          "owner": "owner",
          "legacy-grant-only-brain": "its-account-id"
        }
      }
    }
  }
}
```

Existing memberships and invitations supply their account/brain pairs automatically.
If one legacy brain name belongs to multiple accounts, migration refuses to guess.
Resolve the old records and ownership mapping before retrying. A principal without an
unambiguous owned default membership also blocks migration.

Each brain import has a durable checkpoint. Repeating it never replaces live membership
or restores revoked grants. Legacy grant stores become sealed, and the directory writes
its final checkpoint only after all imports and seals succeed. Failed startup can be
retried with the same configuration. Normal authorization never falls back to old records.
Legacy records, aliases and field IDs remain available for inspection and backup recovery.

5. After successful startup, verify a migrated login, cross-account denial, and the grant
   list for each mapped scope. Remove the maintenance setting and resume traffic.
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
./eng/Verify-NuGetConsumers.ps1 -Feed $feed
```

The verifier creates an isolated cache, resolves every DigitalBrain package from the supplied
feed, builds five consumer profiles, and checks their dependency boundaries.
