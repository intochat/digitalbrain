# The ring law: Kernel, Sdk, Platform, Modules

Status: approved by the user on 2026-10-01.

## The principle this spec ratifies

DigitalBrain is four rings, each with one job, with dependencies pointing only inward:

- **Kernel is physics.** `INeuron`, `IBrain`/`IDigitalBrain`, `Signal` with its stamped
  `Publisher`, subscriptions, caller stamping, and the extension points (the call-filter
  pipeline). The kernel guarantees provenance — every signal carries an unforgeable "from where"
  and every call an unforgeable "who" — and holds no policy, no features, no opinions. It stays
  as small as possible.
- **Sdk is the vocabulary of needs.** Contracts for the capabilities modules require but must
  never implement: secrets, integration accounts, capacity, identity questions, metering. A
  module author writes against Sdk without knowing or choosing an implementation.
- **Platform is the one privileged implementation of the Sdk.** The credential ring: it custodies
  what scripts and modules must never touch (vault keys, connection strings, grant decisions,
  meters) and plugs policy into the kernel's extension points. There is exactly one Platform per
  deployment. `AddDigitalBrain()` stands up Kernel plus Platform, always; the Platform is not
  composable, not optional, and not made of modules. Sdk is the contract face of Platform — one
  boundary, two sides.
- **A module is anything that ships neurons** — contracts apps compose: Gmail, Postgres, Flutter,
  AI, and equally Apps (`IPackage`), Registry (`IRegistry`), Time (`ITimer`). Modules depend on
  Kernel contracts and Sdk, never on Platform's implementation, and on each other only through
  contracts. Apps (packages, behaviors) sit on top and speak only module contracts.

The sorting test for any capability: *does it ship neurons apps compose, or capabilities modules
consume?* Neurons → module. Capabilities → Sdk contract + Platform facet.

"Which modules are mandatory" was the wrong question and is dissolved: Platform facets are
present by construction, and mandatory-ness among real modules is expressed as ordinary contract
dependencies checked at composition, never as a decreed list inside a hosting call.

## Platform facets

The facets, each an Sdk contract surface with its Platform implementation:

- **Secrets** — vault, key wrapping, ownership.
- **Integrations** — integration definitions, capabilities, per-brain external accounts.
- **Capacity** — resolution policy (brain account → platform source → provisioner →
  `CapacityUnavailable`), the provisioner registry, future quotas and status surface. The
  companion capacity spec's `CapacityModule` is superseded: Capacity is born a facet, not a
  module. Which sources and provisioners exist is Platform configuration per environment, never
  composition.
- **Identity** — worked through below as the canonical example.
- **Metering** — `IMeterSink`, the ledger, and the allowance call-filter stage move here out of
  Compute. User-visible wallet/usage neurons are not Platform; they remain module/product
  surface over Platform-held facts.

Today Secrets, Integrations, Identity and Compute are packaged as `IModule`s that hosts must
remember to compose. That is the packaging error this spec corrects: a credential ring you can
forget to compose is not a ring. `SecretsModule`, `IntegrationsModule`, `IdentityModule` and the
platform half of `ComputeModule` stop being modules; their services and filter stages register
unconditionally when the brain is added.

## Identity, the canonical example

Identity shears cleanly into the rings and proves the model:

- **Kernel keeps identity transport.** `CallerContextStamper` and the stamped `PrincipalId` are
  the twin of `Signal.Publisher`: provenance of calls beside provenance of signals. Physics;
  already kernel; unchanged.
- **Sdk gets identity questions.** What modules legitimately ask: the current principal, owner
  stamping (what Gmail's OAuth callback does with secrets), brain membership, grant existence.
  Files' project reference to Identity's implementation becomes a reference to this contract
  surface.
- **Platform holds identity answers and custody.** The directory, the grant store,
  `GrantCallFilterStage`, membership decisions — and authentication itself. The cookie/OIDC
  setup hand-written in IntoChat's `Program.cs` (cookie options, SameSite, 401/403 overrides) is
  misplaced host code the model predicts: establishing who a human is against an external
  identity provider is credential-ring work. Platform is the trust connector — the Flutter shell
  connects the human's actions to the brain; Platform's identity facet connects the human's
  trustworthiness to it.
- **There is no Identity module.** No behavior ever composes an identity neuron; apps benefit
  from identity (stamped calls, owned tables, enforced membership) without speaking it.
  Membership management remains a thin HTTP surface over Platform facts under `BrainRoutes.Group`
  (today's `IdentityEndpoints`), not a module.

If a person ever becomes addressable in the signal graph ("Vlad came online"), that is a future
People module shipping person-neurons on top of Platform identity — a connector whose external
system is humans-as-entities — not identity leaking out of Platform.

## The module law and today's violations

Modules speak to each other only through contracts, and to Platform only through Sdk. Current
violations, to be broken as part of or ahead of this work:

- Registry → Qdrant implementation (a neuron-shipping module hard-depending on a connector's
  implementation; vector search becomes an optional capability with a contract-level seam, as
  its AI-embedding fallback already is).
- Files → Identity implementation (becomes Sdk.Identity).
- Postgres ↔ Supabase implementation references (both directions).
- Flutter → Apps implementation; Assistant → Apps, CSharp, Compute, AI, Flutter implementations.
- Contract oddities: Files.Contracts → Flutter.Contracts; Assistant.Contracts → AI.Contracts and
  Flutter.Contracts.

Apps is a module by the sorting test — it ships `IApp`/`IPackage` neurons — but the publish
gate's authority (nothing unverified reaches another brain) rests on Platform-held verification
facts. The feature is a module; the authority is Platform's.

## Consequences for hosts

`AddDigitalBrain()` = Kernel + Platform. A host's composition is then purely its connector
identity: IntoChat declares AI, Postgres, ClickHouse, Supabase, Qdrant, Gmail, Salesforce,
GitHub, Playwright, Flutter, Files, Memory, Time, Aspire, CSharp, Apps, Registry, Specs,
Assistant — every line a module shipping neurons, no Platform facet among them. Host auth code
leaves `Program.cs` for the Platform identity facet. Module requirement checks at app install
reduce to edge modules only, because the Platform floor is guaranteed everywhere.

## Ordering

This spec sequences after the Platform capacity spec's Postgres work, amending it in one place:
Capacity lands as a facet rather than `CapacityModule`. Migration order, each step building and
passing affected project tests:

1. Name the Sdk surfaces: consolidate `DigitalBrain.Sdk.*` (Secrets, Integrations, Capacity,
   Identity, Metering) as the single contract face modules reference.
2. De-modularize the facets: Secrets, Integrations, Identity, and Compute's platform half
   register with the brain unconditionally; their `IModule` classes and `WithModule<>` lines are
   removed from hosts, fixtures, Dockerfile and pubxml module lists.
3. Move IntoChat's auth setup into the Platform identity facet.
4. Break the module-law violations, Registry → Qdrant first.
5. Re-state host compositions as connectors-only and update CLAUDE.md's ring description.

## Acceptance

- `AddDigitalBrain()` alone yields a brain with secrets, integrations, capacity, identity and
  metering active; no host composes a Platform facet, and none can be omitted.
- The kernel's public surface gains nothing: neurons, signals, brain, stamping, filter pipeline.
- No module project references a Platform implementation or another module's implementation.
- IntoChat's `Program.cs` contains no authentication setup.
- Every `WithModule<>` line remaining in any host names a module that ships neurons.
- Grant and allowance call-filter stages are present in every brain, including every test host,
  by construction.
