# DigitalBrain Behavior Programming v3 Refinement Prompt

You are the final pre-implementation architecture examiner for DigitalBrain.

This is the last architecture-shaping round before implementation planning. Be adversarial. Do not polish Design v2. Falsify its claims, delete unearned components, resolve every hand-waved transport and persistence boundary, and produce the smallest architecture that can actually be implemented test-first.

Read and follow:

- `AGENTS.md`
- `CLAUDE.md`
- `docs/superpowers/specs/2026-07-13-behavior-programming-design-v2.md`
- the current project graph, registrations, tests, AppHost, deployment code, and recent commits

Treat Design v2 as a candidate, not a constraint. Preserve product intent and proven safety invariants, but freely replace its components, project graph, terminology, and extension mechanism.

Do not implement code. Do not scaffold projects. Do not write an implementation plan. The deliverable is an approved Design v3.

## Product intent

DigitalBrain should:

- support durable, human-approved C# Behaviors;
- react to schedules and typed events;
- expose narrow typed capabilities;
- send external mutations through one durable effect authority;
- support Gmail and Salesforce immediately;
- support long-term memory at a deliberately small proof scope;
- prove Telegram can be added without redesigning the platform;
- remain single-principal for the next 12 months;
- use Flutter as the full product UI and MCP as an agent edge;
- permit hard deletion and disposable runtime state during convergence;
- support human-initiated Behavior generation, never autonomous self-modification;
- enable fast deterministic TDD.

Everything else is challengeable.

## Method

Follow the repository's five steps in order: question requirements, delete, simplify, accelerate, automate last.

Use CodeGraph and repository evidence before retaining a subsystem. Use Context7 for current Aspire, Orleans, Microsoft.Extensions.AI, Agent Framework, MCP, Reqnroll, OpenTelemetry, and vector/memory APIs. Use dotnet-inspect for exact .NET and NuGet API surfaces. Run `aspire doctor` and inspect the AppHost model. Do not start implementation.

Work interactively:

1. Present a contradiction and uncertainty ledger.
2. Ask one blocking product question at a time.
3. Present two or three complete architecture alternatives.
4. Recommend one with quantified trade-offs.
5. Present the revised design section by section for approval.
6. Write Design v3 only after approval.
7. Self-review it and stop for user review.

Every architecture claim needs a concrete call path, state owner, dependency edge, failure rule, security boundary, or test. Otherwise delete it.

## Mandatory issues to resolve

### Typed capability transport

Design v2 combines these claims without specifying the mechanism:

- Behaviors call `ctx.Capability<T>()`.
- Provider contracts are Orleans-free.
- BehaviorHost and Kernel know no provider.
- Adapters live in provider projects.
- Reflection discovery and a universal command bus are rejected.
- Unknown capabilities fail at pack time.

Trace a call through:

```text
Behavior assembly
-> IBrainContext
-> BehaviorHost
-> Orleans client
-> provider adapter
-> effect authority or provider read path
-> connector
-> typed response
```

For each step identify the project, interface, runtime implementation, wire contract, serializer, registration, authorization, operation identity, idempotency boundary, version rule, and error returned to Behavior code.

Prove this for Gmail read, Salesforce proposed update, Memory memorize, and Telegram send on paper. Research Orleans serialization/source-generation constraints for dynamically loaded contract assemblies.

Compare:

1. `ctx.Capability<T>()` with generated/provider proxies.
2. Typed operation descriptors plus provider-friendly extension methods.
3. Constructor- or handler-declared typed capability dependencies.

Keep `ctx.Capability<T>()` only if its cross-process implementation is concrete and smaller than the alternatives.

### Provider boundary contradictions

Resolve these:

- Kernel claims to know no provider, but `MemoryGrain` lives in Kernel.
- Providers claim zero central changes, but each needs host/AppHost registration.
- The extension contract assumes every provider has an event source, cursor, executor, credentials, and verifier, although Gmail, Salesforce, Telegram, and Memory differ.

Replace the universal provider shape with optional typed facets if appropriate:

- event source;
- read capability;
- external effect executor;
- internal durable operation;
- credential authority;
- cursor/deduplication;
- outcome verifier;
- health projection;
- management UI.

Use Gmail, Salesforce, and Memory to justify abstractions. Telegram validates them; it must not invent them.

Define an honest extension budget separating runtime logic, composition registration, AppHost resources, deployment/secrets, UI, tests, and security review.

### Grain model and correctness language

Compare:

1. Design v2's registry, dispatcher, and installation grains.
2. A single-principal `BehaviorHubGrain` plus per-installation grains.
3. Installation-owned schedules/subscriptions/inboxes with a minimal router.

For each model show state ownership, writes per Synapse, crash recovery, fan-out, schedule recovery, activation, pause/uninstall, hot-grain risk, tests, and future sharding.

Correct misleading claims:

- execution is at least once, never handler-ran-once;
- multi-grain fan-out is not globally atomic;
- one `WriteStateAsync` cannot cover other grains or external systems;
- providers without idempotency may produce `OutcomeUnknown`.

State exactly what may repeat and what must be idempotent.

### Policy ownership

Challenge whether auto-apply policy belongs in BehaviorRegistry or in the existing effect authority. Prefer reuse over turning Registry into a god grain.

Trace grant, promotion, revocation, policy revision during apply, and uninstall while preserving evidence.

### Existing INO path

Do not treat this path as sacred:

```text
ConversationNeuron
-> InoOperationWorkerGrain
-> AgentFrameworkWorkflowRunner
-> PlanInoToolGateway
-> InoEffectPlanAuthority
-> connector grains
```

Use CodeGraph and tests to determine whether typed operations let us delete `PlanInoToolGateway`, reduce Agent Framework usage, remove duplicate routing/grant/idempotency layers, or share one capability transport between conversations and Behaviors.

Preserve durable approval evidence, effect-plan authority, lease/fence protection, and connector verification unless a smaller equivalent is proven. Everything around those invariants is challengeable.

### Processes and endpoint ownership

List every executable artifact, including short-lived ones, with endpoints, credentials, Orleans role, package access, trust boundary, and reason it cannot be merged.

Compare:

1. RuntimeHost + MCP edge + BehaviorHost.
2. RuntimeHost owning MCP/gRPC/OAuth + BehaviorHost.
3. Runtime silo + combined external Edge + BehaviorHost.

AppHost is not a production process. A spawned workbench is still an executable/project and must be counted.

### Workbench ownership

Design v2 deletes the CLI but still needs compilation, Reqnroll, capability analysis, packaging, and hashing.

Compare:

1. A dedicated short-lived BehaviorWorkbench executable.
2. Local-only authoring and upload of built packages.
3. Workbench mode inside BehaviorHost.
4. Deferring natural-language generation until manual C# packages work.

Specify who launches it, where it runs, allowed filesystem/NuGet access, limits, artifact cleanup, and whether it belongs in the first slice. Prefer deferral if it does not serve the proof slice.

### Package storage and loading

Own the complete lifecycle:

- source, assembly, manifest, tests, and active version storage;
- writer and reader processes;
- whether binaries may live in grain state;
- reuse versus addition of a blob container;
- how BehaviorHost retrieves packages without general storage credentials;
- hash verification;
- restart, load/unload, rollback, and cleanup;
- whether signing is needed for one authenticated owner.

Do not optimize resource count by leaving package storage undefined.

### Memory proof scope

Rebuild the Memory design. A single grain with up to 100k facts and embeddings, rebuilt on activation, is not credible without measured size and latency.

Start with the smallest real behavior:

- remember a bounded confirmed fact;
- recall a few facts with provenance;
- inspect and correct;
- physically forget;
- stay idempotent under duplicate delivery.

Compare:

1. No embeddings initially: exact/tag/source lookup.
2. A small measured embedding index capped at hundreds or a few thousand facts.
3. Dedicated vector/document persistence.

Resolve:

- disposable data versus long-term memory;
- tombstones versus physical forgetting;
- facts outliving Behaviors;
- two public operations versus promised correction/export/forget;
- missing write grants producing rejection versus proposal;
- bulk forget approval without an internal-operation approval rail.

Separate Behavior capabilities, owner management commands, internal maintenance, external effects, and irreversible internal deletion. Physical forget removes content and embeddings; any audit record retains no forgotten content.

Challenge `VectorStoreCollection`, `IEmbeddingGenerator`, Ollama embed resource, 100k facts, background re-embedding, vector-store swap abstractions, memory events, and a dedicated Memory screen. Keep only what proof scope needs.

### Single-principal identity

Decide whether v1 needs a constant brain ID, one opaque `BrainId`, a principal ID, or the existing scope model. Do not retain tenant/workspace machinery under the name `scope`.

Separate owner/brain identity from provider-account identity such as Gmail account, Salesforce org, or Telegram bot.

### Projects and deletion metrics

Re-evaluate every merge/addition:

- `Core -> Kernel.Abstractions`;
- `Ui.Contracts + Ui.Runtime`;
- `DigitalBrain.Aspire -> AppHost`;
- `Salesforce.Tests -> DigitalBrain.Tests`;
- Behaviors SDK and TestKit;
- one contract project per provider;
- Memory implementation ownership;
- the missing workbench project.

Fewer projects are better only when dependency direction and feedback speed improve.

Report deletion separately for production code, tests, comments, generated code, docs, projects, dependencies, resources, and processes. Comments and deleted specs do not count toward production-code reduction.

Set targets for net production code, project count, direct packages, public API members, durable authorities, deployed processes, resources, and test duration.

### Fast TDD loop

Specify actual commands and budgets for:

- pure transitions and Behavior scenarios;
- provider contract tests;
- Orleans persistence tests;
- AppHost model tests;
- full Aspire E2E;
- Flutter tests;
- root completion gate.

Reconcile fast iteration with the repository rule that root `dotnet test --logger "console;verbosity=minimal"` runs every test without filters. If the rule blocks a fast inner loop, propose a CLAUDE.md change for explicit approval.

Keep full-stack journeys minimal:

1. scheduled read-only Behavior to feed;
2. external event to typed read to proposed mutation to approve/apply/verify;
3. memorize/recall only if Memory is in the first slice.

## Final architecture strategies

Compare at least:

1. Repair Design v2 as written.
2. Single-principal consolidation with BehaviorHub + installations and an explicit typed operation transport.
3. Slice-first architecture that extracts extension seams only from working Gmail, Salesforce, and minimal Memory paths.

Quantify projects, executables, processes, grain types, state envelopes, coordination seams, resources, provider touch points, tests, deleted/added production code, and cost of Gmail, Salesforce, Memory, and Telegram.

Recommend one or a precise hybrid.

## Implementation-readiness traces

The final design must trace:

1. Build, verify, store, activate, load, and execute a Behavior.
2. Schedule to feed table.
3. Gmail event to inbox.
4. Provider read.
5. Salesforce proposal and approved verified apply.
6. Duplicate delivery after host crash.
7. State write followed by crash before ack.
8. Behavior-emitted Synapse.
9. Memorize, recall, and physical forget.
10. Telegram registration and send on paper.

Each trace names process, project/type, grain/API, state write, idempotency key, authorization, retry boundary, and outcome. “The adapter handles it” is not sufficient.

## Deliverable

After interactive approval, write:

`docs/superpowers/specs/2026-07-13-behavior-programming-design-v3.md`

Include:

1. Architecture thesis.
2. V2 contradiction-resolution ledger.
3. Deleted/deferred requirements.
4. Final alternatives and decision.
5. Process/deployment graph.
6. Project/dependency graph.
7. Durable authorities.
8. Capability transport.
9. Provider extension facets.
10. Package lifecycle and storage.
11. Event/state/operation/effect rules.
12. At-least-once failure semantics.
13. Existing INO deletions and retained invariants.
14. Minimal Memory model.
15. Telegram proof.
16. Identity/grants/policy.
17. TDD commands and budgets.
18. Honest deletion targets.
19. Exact first vertical slice.
20. Non-goals.
21. V2 retained/changed/deleted ledger.

No implementation-blocking decision may remain deferred to implementation.

Design v3 is ready for planning only when capability transport, package storage, workbench ownership, process boundaries, Memory persistence, at-least-once semantics, policy ownership, INO cleanup, test commands, and honest deletion metrics are concrete and internally consistent.
