# Platform capacity, runtime provisioning, and shipping Customer Researcher to production

Status: approved by the user on 2026-10-01. Amended by the ring-law spec
(2026-10-01-kernel-sdk-platform-ring-law-design.md): Capacity lands as a Platform facet standing
up with `AddDigitalBrain()`, not as a composable `CapacityModule`; references to composing it
beside Secrets and Integrations read accordingly.

## The principle this spec ratifies

Capacity is a Platform concern; the brain and apps never speak it. Apps ask for neurons
(`IPostgresTable`); which physical server answers is decided platform-side, per scope. A module's
deployment options describe capacity, never content: no database named after an app, no seed
owned by a product. Provisioning is not a kernel concept — it is any automation whose end state
is a capacity source the resolver can see.

Capacity joins Secrets and Integrations as the third platform module family member:
`DigitalBrain.Sdk.Capacity` is the contract ring edge modules consume, `DigitalBrain.Platform.Capacity`
is the `CapacityModule` hosts compose. The platform module owns what is the same for every
resource kind — resolution order, the provisioner registry, the refusal, and later quotas and a
capacity status surface. Edge modules own what only they can know — the credential shape and what
provisioning physically does. The AI module already lives by the rule informally (local
Ollama/Foundry vs Azure endpoints behind identical `ILlm` contracts); Postgres is the reference
consumer of the new module, and AI migrates onto it later as the validating second consumer. The
generic layer stays deliberately thin — resource kind plus opaque credential payload — until that
second consumer exists.

## Goal and constraints

Ship IntoChat with the out-of-box Customer Researcher to production against a manually created
Azure Database for PostgreSQL, while local `aspire run` demonstrates real runtime provisioning
(Aspire's Docker Postgres with persistent storage, new databases created on demand). In
production, a runtime provisioning request answers "Sorry, runtime provisioning is not accessible
at the moment." — the refusal comes from the absence of a registered provisioner, never from an
environment conditional.

No changes to `IPostgresTable`, its signals, app packages, or any script-visible surface.
Connection strings and admin credentials stay in the platform ring; scripts never see them.
Existing table behavior — hashed physical names, owner stamping from the kernel caller,
DDL-on-first-Define under the advisory lock, compatibility-checked redefinition — is preserved
exactly.

## The Capacity platform module

`DigitalBrain.Sdk.Capacity` declares, over opaque payloads keyed by a resource-kind string:

- `ICapacity.Resolve(kind, scope)` — answers a resolved source descriptor (origin + credential
  payload) for a scope (brain id, app id). Resolution order, each step optional by registration:
  1. the brain's integration account for that kind (future BYO; the slot exists now, no
     implementation in this spec);
  2. the platform-configured source for that kind — production today;
  3. a registered `ICapacityProvisioner` for that kind, which creates capacity on demand — local
     today.
- `ICapacityProvisioner` — registered per kind by an edge module's hosting when the environment
  supports it; absence of a registration is the production behavior.
- `CapacityUnavailable` — the typed failure every kind shares, carrying the message "Sorry,
  runtime provisioning is not accessible at the moment."

`DigitalBrain.Platform.Capacity` ships `CapacityModule` implementing the resolution policy and
the registry; IntoChat composes it beside `SecretsModule` and `IntegrationsModule`. Quotas per
brain and a capacity status surface are future work that lands here, not in edge modules.

## Postgres as the reference consumer

`DigitalBrain.Modules.Postgres` registers kind `postgres`: its platform source is the
`ConnectionStrings:postgres` payload, and it owns turning a resolved descriptor into a cached
`NpgsqlDataSource`. The platform module never learns Npgsql, the same way Integrations never
learns Gmail's OAuth details.

`PostgresTableProvider` stops holding the single keyed data source and resolves through
`ICapacity`. The resolved origin is pinned per table at first `Define`, stored in grain state
beside `Owner`; later-registered capacity never migrates an existing table. Data movement, if
ever wanted, is an explicit app-level act. `CapacityUnavailable` surfaces through the table
neuron's normal failure path.

## Local runtime provisioning

In run mode, `WithPostgres()` keeps the Aspire Postgres container with its persistent volume and
additionally passes the server's admin connection to the runtime as platform configuration. The
Postgres module registers a `DockerPostgresProvisioner` for kind `postgres` with the Capacity
registry: on demand it runs `CREATE DATABASE` over the admin
connection — one database per brain, named from a hash of the brain id — and caches a data source
for it. Per-app granularity, when needed, is schemas inside the brain's database, not new servers.
Persistence is inherited from the server volume. The provisioner registers only when the admin
connection is configured, which only run-mode hosting does.

## Production

In publish mode the same `WithPostgres()` emits no container; it emits the secret parameter
`postgres-connection` (existing `WithConnection` plumbing), resolved from Key Vault and mounted
as a `secretRef` by the existing deployment machinery. No deployment project is needed: the
publish-mode `postgres-connection` secret parameter is a `parameter.v0`, which `ManifestValues`
resolves from stack configuration and the kernel deployment writes to Key Vault as a mounted
`secretRef`. The Azure server and database are created
manually; its connection string is placed in Key Vault by the operator. No provisioner is
registered, so resolution ends at step 2 for shipped apps and at `CapacityUnavailable` for
provisioning requests.

Future provisioning modes — a connector that creates a server in the user's Azure subscription,
or a user-supplied connection string — both terminate by producing a capacity source (an
integration account); the resolver is already their consumer and does not change.

## Host cleanup

`AppHost.cs` drops `DatabaseName = "customer-research"` for a neutral name (`digitalbrain`); with
hashed physical names the database name carries no app meaning, and this removes the last
app-shaped fact from the host's deployment composition. The ClickHouse `WithSeed("leads")`
violation is noted but out of scope here.

## Ordering and verification

Five commit groups, each building and passing its affected project tests:

1. `DigitalBrain.Sdk.Capacity` and `DigitalBrain.Platform.Capacity` with the resolution policy,
   the registry, and `CapacityUnavailable`; `CapacityModule` composed into IntoChat.
2. Postgres consumes `ICapacity` for its platform source, with per-table origin pinning.
   Behavior-identical; Postgres module unit suite green.
3. `DockerPostgresProvisioner`, admin-connection run-mode wiring, and the refusal surfacing
   through the table neuron.
4. Hosting run/publish split: container plus admin connection in run mode, `postgres-connection`
   secret parameter in publish mode.
5. `DigitalBrain.Modules.Postgres.Deployment` and the AppHost database rename.

Resolution policy tests live in Platform.Capacity (precedence: account beats platform beats
provisioner; refusal when nothing is registered). Postgres-owned tests cover pinning across a
capacity change, descriptor-to-data-source caching, and provisioner creation idempotency.
Run `dotnet test` per project, never the solution. Smoke with `aspire run` from
IntoChat/AppHost until all resources are Healthy, exercising the Customer Researcher table path.
Deployment is verified by publishing with the Key Vault secret present and confirming the
supplier check passes and the researcher ships through the startup publisher's verify gate.

## Acceptance

- Locally, a fresh brain's table provisions its own database in the Docker server on first
  Define, and the data survives a restart.
- In publish-shaped configuration with no provisioner, a provisioning request answers exactly
  "Sorry, runtime provisioning is not accessible at the moment."
- Production deployment passes the supplier check with only a Key Vault secret; Customer
  Researcher publishes at startup and stores research rows in the manually created database.
- `AppHost.cs` contains no app-named infrastructure, and composes `CapacityModule` beside
  Secrets and Integrations.
- The platform module contains no Npgsql or other resource-specific types; Postgres contains no
  resolution-order or refusal logic.
- No script-visible contract, signal, wire shape, or app package changed.
