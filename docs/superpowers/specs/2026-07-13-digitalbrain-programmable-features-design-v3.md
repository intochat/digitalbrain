# DigitalBrain Programmable Features — Architecture Design v3

Date: 2026-07-13

Status: architecture approved section-by-section; assembled for final document review

Supersedes: `2026-07-13-behavior-programming-design.md` and `2026-07-13-behavior-programming-design-v2.md`

## 1. Decision

DigitalBrain will have two deliberately different extension axes:

- An **Integration** is a shipped, infrastructure-aware, compiled NuGet family. It connects DigitalBrain to a provider or platform service and may require rebuilding or restarting affected hosts.
- A **Feature** is source-first C# plus BDD scenarios. It can be authored, built, verified, installed, updated, and rolled back while DigitalBrain keeps running.

The same Feature workflow serves code shipped with DigitalBrain and code authored after deployment. Google is an Integration. Email Summarizer is a Feature using Google's contracts. A runtime-authored summarizer goes through the same build, BDD, approval, release, grant, and installation path as the shipped one.

This is the narrowed form of option C. It preserves dynamic product programming where it matters without pretending provider infrastructure can be safely hot-plugged into a running Orleans application.

The design explicitly rejects:

- a generic dynamic provider or marketplace plug-in platform;
- compiled vertical slices as the only extensibility model, because they fail the programmable-product hypothesis;
- a custom package format or one published NuGet package per Feature;
- arbitrary NuGet restore, hostile-code sandboxing, or dynamically discovered Orleans grain contracts.

## 2. Product vocabulary

| Term | Meaning |
|---|---|
| Integration | Provider or platform connection shipped as contracts plus runtime and hosting facets. |
| Feature source project | Shareable C# implementation, Gherkin scenarios, and BDD test project. |
| Feature Release | Immutable, content-addressed output of a successful build and BDD run. |
| Feature Installation | Owner-scoped configuration, grants, active release, inbox, schedule, state, and execution ledger. |
| Handler | Internal implementation code invoked for one Feature input; not a user-facing concept. |

“Feature” replaces “Behavior” throughout the public product and architecture. Out-of-box and runtime-authored Features differ only in where their source is kept, not in how they are built or executed.

## 3. Architectural invariants

1. Kernel contains no Google- or Salesforce-specific contract, branch, DTO, or SDK reference.
2. Feature code receives dependencies through constructor parameters. There is no service locator, reflection-based capability discovery, or `Capability<T>()` API.
3. FeatureHost holds no provider credentials and cannot directly perform external mutations.
4. Every capability operation is validated server-side against the exact owner, installation, release digest, connection, constraints, and grant revision.
5. External effects continue through the existing signed-plan authority, approval or policy decision, connector verifier, and outcome rail.
6. Out-of-box Features use the public build and installation path. There is no privileged shortcut.
7. Runtime-built code is trusted, owner-reviewed code with constrained authority. This design does not claim process isolation is a hostile-code sandbox.
8. Durable processing is application-level and idempotent. Orleans delivery is not treated as exactly once.
9. Local and dev data may be lost. Production durability, backup, retention, and disaster recovery are intentionally deferred.
10. The platform must end smaller than the current baseline by both production C# and public API surface.

## 4. Integrations

### 4.1 Shape

An Integration has three logical facets:

- **Contracts**: narrow capability interfaces, DTOs, event schemas, stable identifiers, serialization metadata, and safe client metadata.
- **Runtime**: credential ownership, provider SDKs, connector grains or services, watches, webhooks, capability handlers, effect execution, and verification.
- **Hosting**: configuration binding, health checks, Aspire resources and references, and host registration.

The proof combines Runtime and Hosting physically, producing two projects and two distributable NuGet packages per provider:

```text
DigitalBrain.Integrations.Google.Contracts
DigitalBrain.Integrations.Google

DigitalBrain.Integrations.Salesforce.Contracts
DigitalBrain.Integrations.Salesforce
```

Feature projects and FeatureHost reference only Contracts. RuntimeHost and AppHost composition reference the Integration package. Kernel references neither provider package.

Contracts must not reference Orleans, Aspire, provider SDKs, ASP.NET, host configuration, or credentials. Representative contracts are intentionally narrow:

```text
IGmailMessageReader
IGmailMailboxReader
IGmailSendProposer
ISalesforceRecordReader
ISalesforceUpdateProposer
```

Stable capability identifiers are declared explicitly, for example `google.gmail.message.read.v1`. They never derive from CLR method names. A CLR contract may evolve compatibly while the operation identifier remains stable.

### 4.2 Authoring and shipping an Integration

A new Integration has five honest deliverables:

1. contracts;
2. runtime connector and capability handlers;
3. hosting and configuration;
4. verification and health behavior;
5. contract, runtime, and composition tests.

It adds two composition references: RuntimeHost and AppHost. Shipping or upgrading it may rebuild and restart affected hosts. An installed Feature dependency inventory prevents removal of a contract major version while an active installation still depends on it.

This is intentionally similar to an Aspire integration: a reusable package carries the API and the opinionated wiring needed to participate in the distributed application. It is not a runtime marketplace.

## 5. Feature source, build, and release

### 5.1 Source is the shareable unit

A normal Feature directory contains:

```text
EmailSummarizer/
  DigitalBrain.Features.EmailSummarizer.csproj
  EmailSummarizer.cs
  EmailSummarizer.feature
  DigitalBrain.Features.EmailSummarizer.Tests.csproj
  EmailSummarizerSteps.cs
```

Feature implementation dependencies are limited to:

- the approved .NET base-class-library surface;
- `DigitalBrain.Features.Sdk`;
- installed Integration Contracts packages.

The source project, scenarios, and test project are the portable artifact developers exchange or check into a repository. DigitalBrain does not invent a `.dbpkg` file, and each Feature does not need to be published as a NuGet package.

### 5.2 One workflow for shipped and runtime-authored Features

Out-of-box Feature source lives in the repository. Runtime-authored Feature source lives in an owner workspace. Both start from the same project template, use the same build and test commands, and follow this pipeline:

```text
intent
  → proposed Gherkin
  → owner approval
  → generated or edited C#
  → isolated build and BDD verification
  → source and capability diff
  → owner approval of exact digest and grants
  → hot installation in FeatureHost
```

The runtime authoring interaction may begin in Flutter or MCP, but the resulting source has the same structure and can be exported, reviewed, version-controlled, or shipped with DigitalBrain later.

### 5.3 FeatureBuilder

FeatureBuilder is a short-lived executable, not a long-running host and not a product CLI. For each build it receives a bounded source snapshot and an offline, allowlisted package feed; it restores, compiles, executes BDD scenarios, creates a release, reports the result, and exits.

FeatureBuilder has:

- no provider credentials;
- no Orleans membership or grain access;
- no platform-storage connection;
- no unrestricted network access;
- no arbitrary dependency restore;
- an enforced wall-clock deadline.

Its isolation reduces accidental authority and build contamination. The owner still reviews source because .NET process isolation alone does not safely contain malicious code.

### 5.4 Feature Release

A successful build creates an internal, immutable, content-addressed Feature Release containing:

- compiled implementation assembly and required private build outputs;
- a manifest derived from compiled metadata, not handwritten authority claims;
- scenario results;
- a reference to the exact source snapshot;
- the release digest.

Releases live under the Feature source/releases blob area. A release is an internal deployment artifact, not a new public archive format.

FeatureHost loads a release into a collectible `AssemblyLoadContext`. The SDK and Integration Contracts remain in the default load context; only release implementation assemblies enter the collectible context. On update, FeatureHost stages and validates the new release before atomically switching new work to it. The previous release drains and unloads. If unloading fails, the one-replica proof host is recycled. On restart, FeatureHost reloads the active release digests from durable installation state.

Breaking Feature state changes require a new installation. The proof has no state migration mechanism.

## 6. Runtime topology

### 6.1 Processes

The long-lived backend roles are:

| Role | Proof replicas | Responsibility |
|---|---:|---|
| RuntimeHost | 3 local; 2–5 deployed | Orleans silo, capabilities, Integrations, conversations, durable authorities. |
| MCP/UI Edge | 1 | MCP tools and the live UI transport edge. |
| FeatureHost | 1 | Orleans client, release loading, bounded Feature execution. |

FeatureBuilder is a transient fourth executable role. Normal local steady state therefore has five backend processes: three RuntimeHost replicas, one MCP/UI Edge, and one FeatureHost. An active build temporarily adds one FeatureBuilder process.

### 6.2 Durable ownership

Only two new grain types are required:

| Grain | Key | Owns |
|---|---|---|
| `FeatureHubGrain` | `BrainOwnerId` | Feature source/release metadata, installations, subscriptions, grants, dependency inventory, audit references, and event fan-out cursor/outbox. |
| `FeatureInstallationGrain` | owner + installation | Configuration, active release, bounded inbox, schedule/reminder state, Feature state, leases/fences, retries, completion/idempotency ledger, and committed operation intents. |

There is no separate dispatcher grain. The hub durably selects recipients and advances a fan-out cursor. Each installation owns its own delivery, ordering, and execution state.

## 7. Inputs, execution, and durability

### 7.1 Integration events

Integrations publish a stable envelope containing:

- event ID and schema ID, such as `gmail.message.received.v1`;
- `BrainOwnerId`;
- correlation and causation IDs;
- occurrence time;
- bounded JSON with identifiers and minimal facts.

Large or sensitive content is not copied into events. A Feature fetches it through an explicitly granted read capability.

`FeatureHubGrain` persists the recipient set and fan-out cursor, then idempotently appends the input to each `FeatureInstallationGrain` inbox. Ordering is FIFO within one installation. There is no ordering promise across installations or Features.

### 7.2 Schedules

Schedules belong to `FeatureInstallationGrain` and use UTC cron expressions. Every occurrence gets a deterministic input ID. After downtime, missed occurrences coalesce into at most one catch-up input rather than replaying an unbounded history.

### 7.3 Run and commit protocol

FeatureHost claims one inbox item with a time-bounded lease and fencing token, then invokes the release handler. Capability reads and model calls happen immediately and may repeat after failure. Observable writes do not happen during handler execution. Instead, these become buffered operation intents:

- Feature state changes;
- surface changes;
- emitted events;
- Memory writes;
- external mutation proposals.

On success, one `FeatureRunCommit` write to `FeatureInstallationGrain` atomically records:

- new Feature state;
- completed input and acknowledgment;
- the buffered intents;
- the completion/idempotency ledger entry.

The platform does not claim exactly-once handler execution. It provides durable inbox processing, fenced claims, idempotent commit, and idempotent intent application.

The full operation idempotency key is:

```text
FeatureInstallationId + InputId + author-supplied LogicalOperationKey
```

The author-supplied logical key distinguishes multiple intentional calls to the same capability in one run and remains stable across retries.

### 7.4 Proof limits and failure behavior

| Limit | Value |
|---|---:|
| Active installations per owner | 100 |
| Inbox items per installation | 1,000 |
| Feature state | 64 KiB |
| Buffered intents per run | 32 |
| Handler deadline | 60 seconds |
| Capability reads per run | 20 |
| Model calls per run | 4 |
| Failed attempts before park/pause | 5 |

A full inbox pauses the installation and raises a visible alert; it does not silently drop inputs. A handler timeout or crash releases work only after its claim expires. Stale claims cannot commit because their fencing token is rejected. After five failed attempts, the input is parked and the installation pauses for owner action. Every transition carries a trace and correlation chain.

## 8. Capability dispatcher and the retained INO rail

RuntimeHost owns one internal capability endpoint. FeatureHost calls it remotely; retained INO code calls the same dispatcher in-process. Its request envelope contains:

- owner, actor, installation, and release digest;
- input ID and logical operation key;
- capability ID and version;
- bounded JSON payload;
- provider connection ID when applicable;
- deadline and trace context.

RuntimeHost validates the installation and grant before dispatch. Integration Runtime packages register handlers with the internal `ICapabilityDispatcher`. Operations are classified as:

- **Query**: immediate read; safe to repeat.
- **InternalWrite**: buffered intent applied after Feature commit.
- **ExternalEffect**: buffered proposal routed to the effect authority after Feature commit.

External effects preserve the existing `InoEffectPlanAuthority`, signed plan, approval or policy evidence, connector-side verifier, worker lease/fence/reminder/outbox machinery, and provider grains. The Feature does not wait for human approval. Approval and application happen asynchronously and produce decision and outcome events that may trigger subsequent Feature runs.

Conversation state, the conversation/model workflow, operation-worker correctness mechanisms, effect authority, and provider connectors stay behind adapters. `AgentFrameworkWorkflowRunner` becomes a bounded conversation/model workflow rather than a provider switchboard.

The following generic or closed gateway surfaces are deleted after their callers move to the dispatcher:

- `PlanInoToolGateway` and its variants;
- `IInoToolGateway`;
- `IInoOperationCapability`;
- provider branches in `AgentFrameworkWorkflowRunner` and `InoEffectPlanNeuron`.

## 9. Identity, grants, and security

The internal identity model becomes:

| ID | Purpose |
|---|---|
| `BrainOwnerId` | Durable data and authority partition, derived from identity issuer plus subject. |
| `ActorId` | Human, Feature installation, or system actor initiating an operation. |
| `ProviderConnectionId` | A specific external account or organization connection. |
| `SessionId` | Temporary interaction session. |

The current `TenantId` and `WorkspaceId` are removed from internal contracts, grain keys, and Flutter. Local and dev data is disposable, so no compatibility layer or migration is built.

A grant binds:

```text
BrainOwnerId
FeatureInstallationId
ReleaseDigest
CapabilityId and version
ProviderConnectionId when applicable
Constraints
GrantRevision
```

Installation approval displays the exact source/release digest, requested capabilities, selected provider connection, and constraints. Missing grants reject the operation. A Feature cannot add capabilities to itself. New external-mutation Features begin in propose-only mode; an owner may later promote an exact, observed operation shape through policy.

RuntimeHost enforces grants for every operation, so revocation or installation pause takes effect immediately without waiting for FeatureHost to reload. OAuth tokens remain inside Integration Runtime. Feature code sees only opaque provider identifiers and safe labels.

Audit records contain owner, actor, installation, release digest, input ID, logical operation key, capability, provider connection, grant revision, decision, outcome, and correlation IDs. They exclude credentials, message bodies, Memory text, tags, and other capability payloads.

## 10. Memory proof

Memory is an internal platform capability registered through the same dispatcher, not an external Integration and not a grain.

The proof uses one Azure Table named `memoryfacts`:

```text
PartitionKey = BrainOwnerId
RowKey       = FactId
Text
Tags
SourceActor
CreatedAt
UpdatedAt
ETag
```

Feature-facing contracts are `IMemoryRecall` and `IMemoryRemember`. Remember produces a buffered InternalWrite intent with a deterministic fact ID. Recall returns at most 20 facts ranked deterministically by:

1. exact tag matches;
2. case-insensitive token overlap;
3. recency.

This is lexical recall, not semantic search. The proof has no embeddings, vector index, vector store, or `MemoryGrain`.

Limits are 2,000 facts per owner, 2 KiB of text per fact, and 16 tags per fact. Capacity exhaustion returns `CapacityReached`; there is no silent eviction. Owner operations include inspect, export, correct, and forget. Correct uses an ETag-guarded replacement. Forget physically deletes the row. Audit records the operation but not fact text or tags.

The current embedding resource is not a dependency of Memory. Its later retention or deletion is decided solely by whether another retained product caller uses it.

Azurite's Docker volume supplies ordinary local/dev persistence. Losing or wiping it is acceptable. This architecture makes no production backup, high-availability, retention, or recovery promise.

## 11. Storage model

One Azurite storage account exposes seven logical resources:

| Resource | Kind | Owner |
|---|---|---|
| `clustering` | Table | Orleans membership |
| `grainstate` | Blob | General grain state, including Feature grains |
| `conversationstate` | Blob | Conversation state |
| `sessionstate` | Blob | Session state |
| `surfacefeedstate` | Blob | UI surface feed |
| Feature source/releases | Blob | Source snapshots and immutable releases |
| `memoryfacts` | Table | Memory facts |

The journal resource is deleted. From the current six-resource model, the design removes one and adds two, for a net increase of one. Feature state stays in `grainstate`; there is no per-Feature storage resource.

## 12. Target project graph

### 12.1 Platform and support projects

The target platform contains 17 production/support projects:

```text
deploy/
  DigitalBrain.Deploy

hosts/
  DigitalBrain.AppHost
  DigitalBrain.RuntimeHost
  DigitalBrain.FeatureHost
  DigitalBrain.FeatureBuilder
  DigitalBrain.ServiceDefaults

src/
  DigitalBrain.Kernel
  DigitalBrain.Kernel.Contracts
  DigitalBrain.Mcp
  DigitalBrain.Ui.Contracts
  DigitalBrain.Ui.Runtime
  DigitalBrain.Features.Sdk
  DigitalBrain.Features.Testing

integrations/
  DigitalBrain.Integrations.Google.Contracts
  DigitalBrain.Integrations.Google
  DigitalBrain.Integrations.Salesforce.Contracts
  DigitalBrain.Integrations.Salesforce
```

`DigitalBrain.AppHost` absorbs `DigitalBrain.Aspire`. After generic-runtime deletion, surviving `DigitalBrain.Core` and `DigitalBrain.Kernel.Abstractions` types merge into `DigitalBrain.Kernel.Contracts`. UI Contracts and Runtime remain separate because both participate in the live gRPC/RFW rail.

### 12.2 Platform test projects

Five platform test projects own distinct test speeds and dependency scopes:

```text
DigitalBrain.UnitTests
DigitalBrain.IntegrationContractTests
DigitalBrain.OrleansTests
DigitalBrain.AppHostTests
DigitalBrain.E2ETests
```

The first shipped Email Summarizer adds one Feature implementation project and one BDD test project. The proof repository therefore has 24 projects: 22 platform projects plus two for the first shipped Feature. A runtime-created Feature adds no repository project and requires no DigitalBrain restart. A shipped Feature normally adds its two isolated source projects. A new Integration adds two projects and two composition references.

## 13. Deletion and preservation ledger

### 13.1 Delete

- Generic Neuron/Synapse runtime, reflection dispatch, checkpoint branching, schema registry, pack execution, and journaling.
- The journal packages, configuration, tests, and storage resource.
- Generic TestKit and TestKit.Tests built around the deleted runtime.
- Closed INO tool gateways and provider switch branches listed in section 8.
- Provider contracts currently embedded in Kernel abstractions after their Integration Contracts replacements exist.
- `DigitalBrain.Aspire` after AppHost absorbs its surviving code.
- `DigitalBrain.Core` and `DigitalBrain.Kernel.Abstractions` after their surviving generic contracts merge.
- Superseded architecture documents and `BrainProgramming.md` after useful context is harvested.
- Dead Flutter `brain_painter.dart` and `comet.dart`, currently 1,076 lines combined.
- Existing comments in a separate mechanical pass, counted independently from production-code reduction.

### 13.2 Preserve

- Flutter's live `/chat` route, `RuntimeShell`, authentication, chat, feed, and RFW `SurfaceView` path.
- Shared palette and graph-layout types still used by RFW; move them before deleting their dead visualization owners.
- Conversation state machine and bounded model workflow.
- Operation-worker leases, fencing, reminders, outbox, and retry correctness.
- `InoEffectPlanAuthority`, signed effect plans, approval evidence, connector verification, and provider grains.
- Pulumi deployment and current observability foundations.

Deletion is based on final caller proof. A move or project merge does not count as deleted code.

## 14. Size and public-API gates

Measured baseline at design review:

| Measure | Current | Required target |
|---|---:|---:|
| Production C# lines | 24,897 | at most 23,897 |
| Public types | 483 | at most 400 |
| Public methods | 2,577 | at most 2,100 |
| Public properties | 1,182 | at most 1,000 |
| Public fields | 644 | at most 500 |

The implementation must delete at least 5,500 production C# lines and add at most 4,500, for a net reduction of at least 1,000. Tests, comments, documentation, generated files, and Flutter are reported separately and cannot make the production C# gate pass. Project movement and renaming are neutral.

The API gate is deliberately stricter than line count: moving the generic runtime behind new namespaces without removing its public concepts is a failure.

## 15. Verification strategy

The current exact root baseline is:

```text
dotnet test --logger "console;verbosity=minimal"
408 passed, 0 failed, 0 skipped, approximately 34.3 seconds
```

CLAUDE.md's root-only test rule should be revised for this work. Project-specific commands are allowed and preferred during red-green cycles; `--filter` remains prohibited. Before completion, the exact root command is mandatory.

The development loop is:

```text
smallest owning project red
  → minimal change
  → same project green
  → affected suites
  → exact root suite
```

Target budgets:

| Suite or operation | Budget |
|---|---:|
| UnitTests | under 5 seconds |
| IntegrationContractTests | under 10 seconds |
| OrleansTests | under 60 seconds |
| AppHostTests | under 90 seconds |
| E2ETests | under 5 minutes |
| Flutter `flutter test` | under 90 seconds |
| Exact root .NET suite | under 60 seconds |
| FeatureBuilder offline restore | under 10 seconds |
| Feature compile plus BDD | under 30 seconds warm, 60-second hard ceiling |
| Release generation | under 5 seconds |

`DigitalBrain.Features.Testing` supplies a frozen clock, seeded identifiers, recording capability fakes, loud model-response misses, shared Reqnroll steps, and a generated duplicate-input scenario. Undefined, pending, ambiguous, or unmatched steps fail the build. Every out-of-box Feature is verified through the same FeatureBuilder and installation API used at runtime.

Architecture tests enforce dependency directions, the Integration Contracts allowlist, absence of deleted namespaces, and absence of provider types from Kernel.

## 16. End-to-end scenario audit

### Ship or upgrade an Integration

```text
contracts + runtime/hosting + tests
  → publish Integration packages
  → add/update RuntimeHost and AppHost composition references
  → rebuild/restart affected hosts
  → capability handlers and health become available
```

Removal of a contract major version is blocked while an active Feature depends on it.

### Ship an out-of-box Feature

```text
repository Feature source + BDD
  → FeatureBuilder
  → immutable release
  → owner reviews digest and grants
  → install through normal API
  → FeatureHost hot-loads
```

### Program a Feature while DigitalBrain runs

```text
owner intent
  → proposed Gherkin approval
  → source generation/edit
  → isolated build + BDD
  → source/capability diff approval
  → grant exact release
  → hot install without host restart
```

### Summarize a received Gmail message

```text
Google Integration event
  → owner hub fan-out
  → installation inbox
  → FeatureHost claim
  → granted Gmail read + model call
  → commit surface/event intents
  → idempotent application
```

Duplicate delivery or a repeated handler read cannot duplicate the committed outputs for the same logical operation key.

### Gmail to Salesforce external effect

```text
Gmail event
  → Feature reads message and Salesforce context
  → commit Salesforce mutation proposal intent
  → signed effect plan
  → owner/policy decision
  → operation worker applies
  → connector verifies
  → outcome event
  → optional follow-up Feature run
```

FeatureHost never receives Salesforce credentials and does not block while approval is pending.

### Schedule, downtime, and duplicates

```text
UTC cron + persisted next occurrence
  → deterministic scheduled input
  → installation FIFO
  → fenced run and idempotent commit
```

After downtime, missed occurrences coalesce into one catch-up input. Duplicate reminders and deliveries resolve to the same input and completion records.

### Update, restart, and rollback

```text
new verified release staged
  → new work switches atomically
  → old work drains
  → old load context unloads or host recycles
```

Restart reloads active digests. Rollback switches to a retained compatible release. A state-breaking release requires a new installation.

### Remember, recall, correct, and forget

```text
remember intent committed
  → deterministic Azure Table upsert
  → lexical recall by tag/token/recency
  → owner corrects with ETag or physically forgets
```

Memory content never appears in audit payloads.

### Revoke or lose authorization

```text
owner revokes grant or pauses installation
  → hub revision changes
  → next RuntimeHost operation validation rejects immediately
  → run parks or fails visibly
```

Cached FeatureHost state cannot bypass validation.

### Fan-out and backpressure

```text
one Integration event
  → hub persists bounded recipient set + cursor
  → idempotent append per installation
  → each installation drains independently
```

One slow Feature does not block another. An installation reaching 1,000 queued inputs pauses and alerts rather than losing data.

## 17. Non-goals for the proof

- Dynamic provider installation or upgrade without restarting affected hosts.
- A hostile third-party code marketplace or secure sandbox.
- Arbitrary runtime NuGet restore or internet package resolution.
- Generic plug-in discovery, dynamic Orleans application parts, or runtime grain loading.
- Feature state-schema migration.
- Semantic/vector Memory.
- Multi-owner sharing, marketplace distribution policy, or tenant administration.
- Production storage durability, backup, high availability, retention, or disaster recovery.
- An implementation plan in this architecture-shaping task.

## 18. Acceptance gates

The architecture is implemented only when all of the following are demonstrably true:

1. A repository Email Summarizer and a runtime-authored Feature use the same source shape, build endpoint, BDD rules, release format, approval surface, grants, and installation API.
2. A Feature update takes effect without restarting RuntimeHost, MCP/UI Edge, or FeatureHost under the normal unload path.
3. Kernel contains no Google or Salesforce dependency or branch.
4. FeatureHost contains no provider credential and cannot bypass RuntimeHost grant validation or the external-effect rail.
5. Duplicate events, retries, stale leases, restart, rollback, revocation, inbox overflow, and failed unload have explicit passing tests.
6. Memory meets its deterministic lexical behavior and bounded Azure Table limits without an embedding dependency.
7. The exact root suite and suite-specific performance budgets pass.
8. Production code and public API are below all gates in section 14.
9. The deleted generic runtime, gateway surfaces, journal, dead Flutter visualization, and superseded projects have no remaining callers or registrations.

This document defines the target architecture and proof boundaries. It intentionally does not prescribe implementation sequencing.
