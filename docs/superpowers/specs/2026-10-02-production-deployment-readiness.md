# Production deployment readiness: IntoChat with Customer Researcher

Status: gap analysis for user review, 2026-10-02. Companion to
2026-10-01-postgres-capacity-and-provisioning-design.md (capacity/Postgres) and
2026-10-02-app-requirements-and-lifecycle-design.md (requirements/lifecycle). This document
lists everything currently missing or undecided between "merged to master" and "Customer
Researcher runs in production". Items are graded: **Blocker** (deploy fails or product is
broken without it), **Decision** (choose before building), **Runbook** (operator work, no code).

## The target topology

Four Azure pieces:

1. **Brain container** — the IntoChat runtime, deployed by the existing kit (Container App,
   Key Vault secretRefs via managed identity).
2. **Azure Database for PostgreSQL** — created manually; reaches the runtime as the secret
   stack parameter `postgres-connection` → `ConnectionStrings__postgres`. No provisioner
   registers in publish mode, so runtime provisioning answers its refusal by construction.
3. **Sandbox session pool** — Container Apps dynamic sessions, fully described by
   `CSharpDeployment.cs`: dedicated 443-egress-only subnet, 1 CPU/2Gi custom container,
   one session per owner, Session Executor role for the runtime, run-token key. The runtime
   switches from the local Docker sandbox to `SessionPoolRunner` purely on the presence of
   `DigitalBrain:CSharp:SessionPoolEndpoint`.
4. **Sandbox image** — `Dockerfile.production` baking `src/Modules` source and pre-built
   Contracts into `/brain` so `#:project /brain/src/Modules/...` directives resolve.

In production the sandbox is not only the publish gate: **every behavior of every app executes
in it**. The researcher's `research.cs` loop is a session. No pool, no product.

## Blockers

### B1. No pipeline builds or pushes the sandbox image
`sandboxImage` is a required deployment setting pointing at an image nobody currently builds.
Needed: a CI/release step that builds `Dockerfile.production` from the repo root, tags it with
the release version, pushes to ACR, and feeds the tag into the deployment. **Lockstep rule:**
the sandbox image and the brain image must come from the same commit — the contracts baked
into `/brain` must match the contracts the brain composed, or scripts compile against stale
types and fail at the neuron edge.

### B2. `SourceRoot` is null on the deployed brain host — authoring silently degrades
The brain container has no repo checkout, so `CSharpOptions.SourceRoot` (found by locating
`DigitalBrain.slnx`) resolves to null. Consequences: the contract catalog emits no `#:project`
lines, so the Author/Builder agents write scripts without contract references — runtime
self-programming, the product thesis, breaks quietly while `CanRun` stays true and shipped
apps keep working. Fix options (pick one):
- bake the same `/brain` contracts layout into the brain image and set `SourceRoot=/brain`;
- make the catalog serve directives from the composed contracts assemblies without a source
  tree (ties into D1).
Either way: a startup health signal when authoring is degraded, never silence.

### B3. `RequireRunnable` / startup shipping under a null `SourceRoot` is unverified
`ShippedAppPublisher` runs at first boot: commit → `RequireRunnable` → verify in the pool →
publish. Whether `RequireRunnable` depends on `SourceRoot` has not been checked. If it does,
first boot refuses every shipped package. Verify before the first deploy; add a fact.

### B4. Real Key Vault behind the Secrets facet
`PlatformHosting` registers `TryAddSingleton<IKeyVault, FakeKeyVault>` and, off Windows,
`KeyVaultKeyWrapper`. The deployment exports `DigitalBrain__KeyVault__Uri` and the runtime
identity. Confirm the production composition actually overrides `FakeKeyVault` with the real
vault-backed implementation (and fails loudly if the URI is missing), because a brain holding
user integration credentials in a fake vault is not a deployable state. Add a composition fact
that publish-shaped configuration never resolves `FakeKeyVault`.

### B5. Postgres database permissions match the lifecycle semantics
The runtime's database user must be able to `CREATE TABLE`/`DROP TABLE` in the target schema:
first `Define` creates tables on demand, and uninstall teardown drops them. When creating the
manual server, provision a dedicated database and a least-privilege role (DDL inside that
database only, no server-level rights). Document the exact grants in the runbook. SSL is
already enforced by `PostgresConnectionSettings` defaults.

## Decisions

### D1. Contracts distribution: source-baked `/brain` vs NuGet packages
Today scripts compile module contracts from source inside the sandbox (`#:project` → csproj →
build). This works but couples the image to the repo layout and makes B1's lockstep rule
load-bearing. The alternative the codebase is already half-prepared for (`IsPackable=true` on
modules, a "Validate NuGet packages" CI step): publish `DigitalBrain.Client`, `DigitalBrain.Sdk`
and every `*.Contracts` package to a feed, and have scripts use `#:package
DigitalBrain.Modules.X.Contracts@<version>` directives.

- **Pros:** versioned contracts (apps can pin; upgrades become explicit), a much smaller
  sandbox image (SDK + warm NuGet cache, no source tree), B2 partially dissolves (the catalog
  can emit `#:package` lines from composed assembly versions, no source tree needed on the
  brain host), and shared/forked packages become portable across deployments by construction.
- **Cons:** a feed to run (ACR artifact feed or nuget.org), publish automation per release,
  the sandbox's 443-only egress must reach the feed (or the image carries an offline cache),
  and every existing `#:project` line — including the shipped apps and the authoring
  templates — migrates in one coordinated change. Verification and requirement parsing
  (`AppRequirements` reads `#:project`) must learn `#:package` too.

**Recommendation:** ship v1 with the source-baked image (B1/B2 as written) — it is the path
that exists; open the NuGet migration as its own issue for v2, since it changes the app
packaging contract and deserves its own spec. Do not mix it into the first deploy.

### D2. First-boot verification budget and the ShipOnStartup switch
First boot verifies five packages sequentially through real pool sessions (one warm instance).
Decide the health-probe and revision-switch policy around that: either accept a multi-minute
cold start (raise the Container App startup probe window), or deploy with
`DigitalBrain:Apps:ShipOnStartup=false` and trigger shipping explicitly after the revision is
healthy. Either is fine; pick one and encode it in the deployment, not in tribal memory.

### D3. Dev/test module divergence in the container lists
The product profile (Dockerfile/pubxml, guarded by `ContainerModuleListsAgreeAndResolve`)
must stay the single source of truth for what ships. Any future module addition that the
researcher or assistant needs must land there in the same PR — the guard fact enforces
agreement, not completeness. Revisit manifest generation from the csproj if the list churns.

## Runbook items (operator work per environment)

- **Secrets into the stack/Key Vault:** `postgres-connection` (B5's role), the master key,
  `openai-api-key`, Gmail client id/secret, Salesforce consumer key/secret, GitHub app
  credentials, Supabase connection (`SupabaseModule` uses `WithConnection("supabase")`),
  Tavily key if web search ships enabled. Integration catalog seeds under
  `DigitalBrain:Integrations:{id}:{Field}` for every `IntegrationDefinition` the composed
  modules declare.
- **Sandbox settings:** `sandboxImage` (from B1), `sessionExecutorRoleId` (the ContainerApps
  Session Executor role definition id for the subscription), optional `sandboxSubnet`
  override; ACR pull role for the pool's identity.
- **DataProtection container:** the runtime creates `intochat-protection-v1` itself (the kit
  does not run Aspire container provisioning); confirm the storage identity can create
  containers, or pre-create it.
- **Networking:** sessions must reach `DigitalBrain__CSharp__EdgeUrl` over HTTPS (the brain's
  public URL today — revisit if the brain ever goes internal-only, because the sandbox subnet
  denies non-443 and the pool environment is separate); AI/ClickHouse/Qdrant per their
  existing deployment modules.
- **Smoke after deploy:** all resources healthy → five shipped packages reach Published →
  install Customer Researcher in a fresh brain → research against a real site → row lands in
  the manual Postgres database → uninstall → table dropped. Then the deliberate refusals:
  runtime provisioning answers "Sorry, runtime provisioning is not accessible at the moment.";
  installing a package requiring an uncomposed module names the module.

## Explicitly out of scope for the first deploy

NuGet contract packages (D1 v2), BYO user connection strings and Azure-subscription
provisioning (capacity spec futures), per-brain storage quotas, multi-region anything.
