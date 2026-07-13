# DigitalBrain Behavior Programming — Design v2

Date: 2026-07-13 · Status: approved section-by-section in interactive review; supersedes `2026-07-13-behavior-programming-design.md` and `2026-07-13-behavior-programming-architecture-convergence.md`, which are deleted on acceptance of this document.

Product-intent anchors confirmed by the owner during review:

- Deployment exists (Pulumi → Azure Container Apps) but **all data is disposable**: no migrations, no schema compatibility, no dual writes, ever.
- **Single principal** for the next 12 months. Scope remains a field in identity records; no tenant machinery exists.
- **Flutter is the full product UI**; MCP stays the agent edge; only provably dead client code is deleted.
- **Memory ships for real at proof scope** (memorize/recall only); Telegram is a paper stress test proving the extension budget.
- **Self-extension is human-initiated only** in v1.

## 1. Thesis

DigitalBrain becomes programmable through **Behaviors**: durable, versioned, human-approved C# automations that react to typed events (Synapses) and schedules, read the world through narrow typed capabilities, and change the world only through the one existing effect gate. The platform stays small by construction: the Kernel knows no provider; the Behavior SDK knows only platform primitives; every provider — external (Gmail, Salesforce, Telegram) or internal (Memory) — arrives as a ~200-line Orleans-free contract package plus an adapter, touching **zero central files**. Three new grain types carry the whole rail. Everything the generic Neuron/Synapse runtime once promised is either carried by this smaller shape or deleted.

The system is smaller after adding Telegram-on-paper and Memory-for-real than the v1 design was before them: fewer grains (3 vs 7), fewer packages (−5 NuGet, −8 Flutter), fewer storage resources (−1), no CLI process, no migration machinery, no tenant machinery.

## 2. Requirements deleted or deferred

| # | v1 requirement | Disposition |
|---|---|---|
| R1 | Workspace/tenant machinery: policy ceilings, admin grants, sharing gates, marketplace, per-tenant hosts | Deleted. Scope field retained in identity records only. |
| R2 | Data migration and compatibility policy (converters, retention exports, SDK semver windows) | Deleted. Data is disposable; schema changes wipe and re-seed. |
| R3 | `ProposalDecided` synapse, rich timezone UI, filter language beyond field equality, App grouping | Deferred; explicit non-goals. |
| R4 | Behavior-emitted "create/update behavior" proposals | Deferred. Self-extension is human-initiated only. |
| R5 | Deleting `Ui.Runtime`/`Ui.Contracts`; aggressive Flutter feature pruning | Reversed. Both carry the live gRPC surface rail; Flutter is the product UI. Consolidated, not deleted. |
| R6 | The 9-phase convergence plan as roadmap | Discarded. Surviving decisions live here; the document is deleted. |
| R7 | `tools/DigitalBrain.Brain` CLI (14+ commands) as v1 deliverable | Deferred. Management = runtime API + Flutter + MCP tools; only the workbench compile/verify step needs a process, and it is a short-lived spawned process, not a CLI product. |

## 3. Deletion ledger

Evidence base: CodeGraph sweep of the working tree at review time. Every cluster deletes code, tests, registrations, package references, and resources together; no wrappers, no flags, no deprecation periods.

| Cluster | Contents | Evidence | Size |
|---|---|---|---|
| D1 Legacy generic runtime | `Neuron`, `SynapseStream`, `SynapseDispatch`, `LlmAttribute`, `NeuronStateProtectors` (Kernel.Abstractions); `LlmNeuron`, `LlmResponderNeuron`, `AddKernelSecurity`, `CheckpointProtector`, `KernelTaskSynapses`, `NeuronJournals`, `PrototypeJournals` (Kernel); `Synapse`, `Signals`, `IHandle`, `INeuronStateProtector`, checkpoint types (Core); ~10 test files exercising only this stack | Zero product callers; Orleans assembly-scan registration only | ~3.5–4.5k lines |
| D2 Journaling residue | `Microsoft.Orleans.Journaling*` alpha packages, `UseJsonJournalFormat`, `EncryptedSynapseJsonConverter`, `journal` blob resource, `OrleansJournalClusterFixture`, journal spikes | Journaling API test-only; package alpha-only upstream through 10.2.2; `journal` blob backs only the D1 synapse journal (final caller proof at deletion) | 2 packages, 1 resource, ~0.5k lines |
| D3 Old TestKit | `tests/DigitalBrain.TestKit`, `tests/DigitalBrain.TestKit.Tests` | Built on D1 (`ProbeNeuron : Neuron`, `IDurableList` wiring) | 2 projects, ~300 lines |
| D4 Redis | Dead branch `Mcp/Program.cs:116-119`; packages `Microsoft.Orleans.Clustering.Redis`, `Aspire.Hosting.Redis`, `Aspire.StackExchange.Redis` | No Redis resource in the AppHost model; branch unreachable | 3 packages |
| D5 Flutter provably dead | `widgets/canvas_3d.dart` (orphan); `widgetbook.dart` (owner-approved delete); perf SDK `app/packages/digital_brain_sdk_flutter` + its `main.dart` wiring (server RPCs confirmed absent); pubspec deps with zero lib importers: `graphic`, `markdraw`, `shared_preferences`; verify then drop `desktop_drop`, `file_picker`, `cross_file`, `media_kit_video`, `media_kit_libs_video`, `youtube_player_iframe` | Import tracing from `main.dart`/router | ~1k+ lines, up to 9 deps |
| D6 Empty husk | `src/DigitalBrain.Pack.Contracts/` | Empty, untracked, not in `Brain.slnx` | 1 directory |
| D7 Comments | 122/264 C# files, 70/152 Dart files, 6 XML, both props, 5 csproj, 8 YAML | CLAUDE.md zero-comment rule | ~1.2k lines |
| D8 Superseded docs | `docs/BrainProgramming.md`; v1 design; convergence plan | Harvested/superseded by this document | ~2.5k lines |
| D9 Project merges | `Core` → `Kernel.Abstractions`; `Ui.Contracts` + `Ui.Runtime` → `DigitalBrain.Ui`; `Salesforce.Tests` → `Tests`; `DigitalBrain.Aspire` → `AppHost` | No boundary earns independence: no independent consumers, always co-deployed | −4 projects |

Retained against v1-plan intent, with evidence: RFW server-driven UI rail and globe/graph widgets (RFW-gated, product UI); `Ui` DTOs (live gRPC wire to Flutter); `PlanInoToolGateway` (live: registered under `DigitalBrain:Tools:Enabled=true`, consumed by `InoOperationWorkerGrain` — its retirement happens only when typed operations replace its last caller, as ordinary slice work); Pulumi deploy (live, deploys kernel + mcp).

## 4. Alternatives evaluated

### Alt 1 — Integration-specific Kernel APIs (v1 as approved)

`ctx.Gmail`/`ctx.Salesforce` compiled into the SDK; 7 rail grains; 4 new projects + CLI. Best autocomplete discoverability. Rejected: every provider bumps the SDK every package depends on; `IBrainContext` and Kernel.Abstractions become the god surfaces; 7 grains create 4 dual-write seams (registry↔subscriptions, registry↔schedules, inbox↔state, policy↔gate evidence) that v1's own Amendment A already started conceding; adding Telegram edits ~6–10 central files.

### Alt 2 — Typed provider contract packages + consolidated authorities (CHOSEN)

Base SDK = platform primitives only; providers = contract package + adapter; one validated resolution point `ctx.Capability<T>()`; 3 rail grains; 3 processes; no CLI. Detailed throughout this document.

### Alt 3 — Universal resource/command/event bus

Generic `QueryAsync(ResourceQuery)`/`ExecuteAsync(Command)` + schema-registered events. Rejected: it recreates the generic runtime this repo has now deleted twice, with the same documented rot (string identities, runtime schema failures, reflection reconstruction); authorization ids become data (spoofable, re-validated everywhere); compile-time guarantees vanish; and its generality serves a single-principal system not at all.

Quantified comparison:

| Dimension | Alt 1 | Alt 2 (chosen) | Alt 3 |
|---|---|---|---|
| New projects | 4 + CLI | 3 core + ~200-line contracts per provider | 3 |
| New grain types | 7 | 3 | 3–4 + schema registry |
| Dual-write seams | 4 | 0 | pervasive |
| Telegram: central files | 6–10 | 0 | 0, plus runtime schema risk |
| SDK stability | breaks per provider | stable | stable but stringly typed |
| Compile-time safety | full | full | lost |
| God-object risk | `IBrainContext`/Abstractions | contained | the bus |

## 5. Final component and project graph

```
src/  DigitalBrain.Kernel.Abstractions     absorbs surviving Core types
      DigitalBrain.Kernel                  + BehaviorRegistryGrain, SynapseDispatcherGrain,
                                             BehaviorInstallationGrain, MemoryGrain
      DigitalBrain.Mcp                     Edge: MCP tools + gRPC UI (role unchanged)
      DigitalBrain.Ui                      Ui.Contracts + Ui.Runtime merged
      DigitalBrain.Behaviors.Sdk           NEW  platform primitives only, Orleans-free
      DigitalBrain.Behaviors.TestKit       NEW  FakeBrainContext + shared Reqnroll steps
integrations/
      DigitalBrain.Gmail.Contracts         NEW  Orleans-free
      DigitalBrain.Google                  adapter (watch grain, executor, evaluator)
      DigitalBrain.Salesforce.Contracts    NEW  Orleans-free
      DigitalBrain.Salesforce              adapter
      DigitalBrain.Memory.Contracts        NEW  Orleans-free; adapter grain lives in Kernel
hosts/
      DigitalBrain.RuntimeHost             silo + OAuth edge
      DigitalBrain.BehaviorHost            NEW  Orleans client; loads packages, budgets, proxies
      DigitalBrain.ServiceDefaults
      DigitalBrain.AppHost                 absorbs DigitalBrain.Aspire
tests/DigitalBrain.Tests                   absorbs Salesforce.Tests; folders, not projects
deploy/DigitalBrain.Deploy                 app/ (Flutter, full product UI)
```

Dependency rules (architecture-tested):

- `Behaviors.Sdk` and every `*.Contracts` reference only approved BCL assemblies. No Orleans, Aspire, ASP.NET, connector SDKs, MCP, Agent Framework, DI, HTTP, filesystem, process, environment.
- Behavior packages reference `Behaviors.Sdk` + contract packages only.
- Kernel never references `Google.Apis.*`, `DeveloperForce.*`, or any adapter.
- Adapters depend inward on Kernel.Abstractions + their contracts; nothing depends outward on adapters except host composition.
- Nothing references D1–D3 namespaces (tombstone test).

Project count stays flat at 17 (six removed/merged, six added). The material decreases are lines (net −4–5k C# plus Flutter/docs/comments), packages (−5 NuGet, −8 Flutter), storage resources (−1), processes vs v1 plan (−1: no CLI), grains vs v1 design (−4).

## 6. Durable authorities and state ownership

| Authority | Key | Owns atomically | Notes |
|---|---|---|---|
| `BehaviorRegistryGrain` | scope | installed versions, active version, derived subscriptions, derived schedules (+ the reminder), capability grants, auto-apply policy records, audit tombstones | Absorbs v1's SubscriptionRegistry, Schedule, ApprovalPolicy. Activate-version+subscriptions+schedules is one write. Policy evidence survives uninstall via tombstones. |
| `SynapseDispatcherGrain` | scope | fan-out ledger: idempotent envelope accept → per-inbox append cursors | Hot write path; deliberately not merged with registry. Reads the registry's subscription projection. |
| `BehaviorInstallationGrain` | installation | FIFO inbox (dedup, lease/fence, retry, park) + behavior KV state + run/idempotency ledger | One state envelope: handler-ran-once, state-written-once, ack-recorded is a single `WriteStateAsync`. |
| `MemoryGrain` | scope | fact rows (text, tags, source ref, extraction metadata, supersession, tombstones) + derived embeddings `(FactId, ModelId)` + active-model pointer | In-memory cosine rebuilt on activation. |
| Existing: `ConversationNeuron`, `SessionNeuron`, `SurfaceFeedNeuron`, `InoEffectPlanNeuron`, `InoOperationWorkerGrain`, outbox dispatcher, connector grains | — | unchanged | The INO path is not redesigned. |

All rail grains use the existing default encrypted grain-storage provider. No new storage container. Inbox/ledger persistence is plain `IPersistentState` with explicit writes (Orleans Journaling confirmed alpha-only upstream; v1's unresolved question 10 closed).

Schedule mechanics: registry persists `nextDueAt` per schedule (Cronos as pure occurrence calculator, UTC + explicit timezone); Orleans reminders are wake-up hints (minute floor, missed ticks skipped); reconciliation emits at most one catch-up `ScheduleFired` with a deterministic id.

## 7. Base SDK vs provider contracts

Base SDK (stable; changes only for genuine new platform primitives):

```csharp
public interface IBehaviorOn<in TSynapse>
{
    ValueTask HandleAsync(TSynapse synapse, IBrainContext brain, CancellationToken ct);
}

public interface IBrainContext
{
    T Capability<T>() where T : class, IBrainCapability;
    IBrainState State { get; }
    IBrainSurface Surface { get; }
    IBrainModel Model { get; }
    IBrainClock Clock { get; }
    IBrainLog Log { get; }
    ValueTask EmitAsync<TSynapse>(TSynapse synapse, CancellationToken ct);
}
```

Plus: `[Behavior]`, `[OnSynapse]`, `[OnSchedule]` attributes, the synapse envelope contract, `MutationIntentResult` (`Applied`/`Proposed`/`Rejected`), and strict source-generated JSON serialization. Deliberately absent and inexpressible: `IGrainFactory`, `HttpClient`, filesystem, process, secrets, configuration, DI.

`ctx.Capability<T>()` is not a service locator: `T` is a compile-time interface from a referenced contract package; manifest derivation records the capability ids `T` carries; install intersects them with owner grants; the host resolves only manifest-declared capabilities and throws a loud typed error otherwise. Unknown `T` fails at pack time, not runtime.

Provider contract package contents (each ~150–300 lines): capability interfaces split read vs mutation-intent; bounded DTOs; typed synapse records; assembly-level capability metadata `[BrainCapability("gmail.message.read", typeof(IGmailRead))]` with risk classification. Capability ids are attribute-declared, never derived from method names (rename-safe security identity — v1 F5 kept).

`ctx.Model` is bounded structured extraction over `IChatClient.GetResponseAsync<T>` (Microsoft.Extensions.AI, stable), strict schema, `additionalProperties: false`, unmapped members rejected, token/call budgets enforced by the host. No Agent Framework types anywhere near the SDK; the conversation path's `AgentFrameworkWorkflowRunner` stays behind its existing seam and is unrelated to behaviors.

## 8. Integration extension contract

Every integration supplies exactly:

1. Contracts package: capability interfaces, DTOs, synapse records, capability metadata + risk classes.
2. Adapter: event-source grain with durable cursor + dedup key; effect executor with outcome verification; grant evaluator; credential/health state (revocation pauses the source and surfaces a feed card).
3. One registration line in trusted host composition + one AppHost line (resource/secret params).
4. Provider contract tests on the shared harness: cursor recovery, duplicate delivery, revoked credentials, rate-limit/retry-after, dedup under crash.
5. Telemetry via shared `ActivitySource` conventions (low-cardinality tags; no message ids, no content).

Universal across providers (proven by Gmail/Telegram symmetry: both are at-least-once, cursor-driven, bounded-retention, wake-up-channel-is-not-truth): the durable cursor pattern, dedup keys, typed synapse mapping, effect executor + intent-journal-before-send. Provider-specific and not abstracted: auth model, rate-limit shape, verification key shape, retention window handling.

Extension budget (normative target, demonstrated by Telegram below): zero central logic changed — the only central touches are two registration lines (host composition + AppHost resource/secret); 2 provider projects ≈ 1k lines; ~15 contract tests; security review scoped to the contracts capability/risk table; first vertical behavior expressible the day contract tests pass.

## 9. Telegram end-to-end stress test (paper)

`DigitalBrain.Telegram.Contracts`: `TelegramMessageReceived`, `TelegramCallbackPressed` synapses (ids + bounded facts, text ≤4096); `ITelegramRead` (chat, bounded recent messages); `ITelegramSend` (`ProposeSendAsync`/`ProposeEditAsync`/`ProposeDeleteAsync`); capability ids `telegram.message.{read,send,edit,delete}`; `send` risk-classed outward-irreversible → propose-mode default.

`DigitalBrain.Telegram` adapter:

- `TelegramSourceGrain` per bot token — Telegram 409s concurrent `getUpdates`, so a single-activation grain is the natural single-poller authority. State: `LastConfirmedUpdateId`. Dedup on `update_id`; each update (tagged union) maps to exactly one synapse; cursor persists after dispatcher accept (at-least-once + inbox dedup). Downtime >24h = Telegram's retention cliff → acknowledged-gap feed card, never silence.
- Webhook mode: Edge endpoint validates `X-Telegram-Bot-Api-Secret-Token`, forwards as wake-up; polling cursor remains truth.
- Effect executor: `sendMessage` has no idempotency key → intent-journal before send; timeout-after-send = `OutcomeUnknown` resolved by the gate's verifier; verification key `(chat_id, message_id)`; edit treats "message is not modified" as success; delete treats "message to delete not found" as success; 429 honors `retry_after` exactly; 401 = token revoked → pause source + feed card.
- Multiple bots = multiple source grains keyed by bot identity; scope rides every envelope and grain key, so cross-account delivery is structurally impossible.

Flow: update → source grain (validate, dedup, map) → dispatcher fan-out (provider-blind) → installation inbox → BehaviorHost claim → `ctx.Capability<ITelegramRead>()` (grant-checked) → `ProposeSendAsync` → existing effect gate (policy record or approval card) → adapter executes + verifies → audit + feed.

Central logic modified: **none** — the dispatcher, inbox, package lifecycle, effect-plan invariants, and Gmail/Salesforce code are untouched; the only central touches are the two registration lines counted in §8. Honest caveat: the first provider (Gmail retrofit) pays for the shared machinery — attribute conventions, manifest scan, contract-test harness; Telegram inherits it. Explicit registration is the chosen extension point — reflection-based discovery is rejected on this repo's own history.

## 10. Long-term memory

Classification: **platform capability provider** — platform-owned `MemoryGrain`, behavior-facing surface through the standard contract-package model (`DigitalBrain.Memory.Contracts`). Not a Kernel primitive (Kernel gains zero memory types), not an external connector (no OAuth, no external verification), not a behavior projection (facts outlive behaviors).

v1 surface, two operations: `MemorizeAsync(text, source, tags?) → FactRef`; `RecallAsync(query, filter?, top) → RecalledFact*` with citations always attached. Facts: bounded text + tags + source ref (conversation/synapse id + timestamp) + extraction metadata + `SupersededByFactId` + tombstone. Embeddings: derived `(FactId, EmbeddingModelId)` rows via `IEmbeddingGenerator` (Ollama `embed` resource); retrieval = structured filter + in-memory cosine top-k behind `VectorStoreCollection` abstractions (GA); at ≤100k facts this is milliseconds and zero new infrastructure.

The ten answers:

1. Memory write = internal durable state transition, always. 2. No per-write approval; policy authorizes categories: `memory.fact.write` low-risk grantable; `memory.fact.forget` (bulk) high-risk → propose-mode. 3. No memory operation uses the effect gate in v1 — forcing reversible internal writes through external-mutation machinery is the distortion this design forbids. 4. Memorize/supersede/correct/forget are each one idempotent `WriteStateAsync` (fact key = hash of normalized text + source). 5. Inspect/correct/export/forget via Flutter memory screen + `ino_interact` (typed grain queries; correct = supersede; export = JSON dump; forget = tombstone). 6. Memory emits no synapses in v1 — recursion structurally impossible; a future `MemoryFactRecorded` inherits envelope depth + declared-emits rules. 7. Contract package, not `IBrainContext` member — internal capabilities ride the same extension model. 8. A vector DB swap changes only the adapter internals behind `VectorStoreCollection`; Kernel, contracts, behaviors untouched. 9. Source refs are ids + timestamps, not content: deleting a source leaves an honest dangling stub; forgetting a fact tombstones fact + embeddings together. 10. Second-database prevention: DTOs cannot express documents; per-scope count/byte caps reject loudly; two operations leave no query language to grow into an ORM.

Stress behaviors verified against the model: preference-confirmed → memorize (idempotent under redelivery); Telegram-contact-preference without write grant → `Proposed` card via grant intersection; recall-before-draft → filtered top-k with citations; forget-this-conversation → filter by source ref, bulk tombstone behind propose-mode; re-embed-with-new-model → background `(FactId, NewModelId)` build + atomic active-model flip, facts untouched.

Deferred: hybrid/BM25, knowledge graph, temporal validity, consolidation, memory synapses, sharing policies — all additive behind the same two operations.

## 11. Safe self-extension lifecycle (human-initiated)

```
chat request → structured intent (unmapped fields rejected)
→ GATE A: owner approves the .feature contract
→ workbench: short-lived isolated process; compiles behavior.cs;
  runs scenarios on FakeBrainContext; no cluster access, no secrets;
  deterministic Roslyn build → package hash
→ red/undefined scenario = no install (missingOrPendingStepsOutcome=Error;
  REQNROLL_DRY_RUN binding validation in CI)
→ GATE B: owner approves the derived capability manifest (diff on update)
→ registry installs propose-mode; activation is a separate explicit act
→ GATE C: per-capability policy promotion with observed evidence
```

Threat mappings: recursive generation impossible (no behavior-reachable capability can invoke the workbench; generation starts only from authenticated owner chat); capability escalation re-triggers GATE B and demotes affected policies; vacuous tests blocked (undefined steps error, zero-skip root gate, generated duplicate-delivery scenario for mutation behaviors, workbench requires green-with-assertions); prompt injection contained (content → strict schema-bound extraction only; grants decided by manifest + gates, never model output; gate binds approval to canonical request hash); permissions-exceed-intent fails the pack (manifest capabilities must trace to approved intent); no auto-activation or policy inheritance by construction (install, activate, promote are three separate durable operations).

## 12. Event vs state vs effect rules

Governing rule: **Synapse iff a user could write "when X happens…" about it; effect gate iff the world outside DigitalBrain changes.**

| A thing is | iff |
|---|---|
| a Synapse | external observation or platform occurrence subscribable by behaviors; explicitly emitted; manifest-declared. Never: RPCs, state changes, telemetry, memory writes, proposal decisions (v1) |
| grain state only | bookkeeping one authority owns (cursors, ledgers, policies, facts, run history) |
| in an inbox | a Synapse matched to an active subscription |
| in the idempotency ledger | behavior-initiated operation whose duplicate must return the recorded outcome (intents, emits, surfaces, memorize) — keyed installation + synapse id + operation + canonical request hash, recorded with the ack |
| through the effect gate | mutates an external system (Telegram send, Salesforce update). Never: memory writes, `ctx.State`, feed cards |
| human-approved | gated + no covering policy, or risk class demands it always |
| auto-applied | gated + durable policy record covering behavior + capability + constraints; evidence cited in the ApprovalRecord |
| in the feed | user-facing surface, approval card, or health alert via the existing Outbox → SurfaceFeed rail |
| a trigger for another behavior | a Synapse whose emit the manifest declares; depth-stamped; MaxDepth rejects loudly |

## 13. Identity, grants, policy, mutation invariants

- `owner grants ⊇ behavior capabilities (GATE B) ⊇ auto-apply policies (GATE C)`. A behavior can never hold a capability its installer lacks.
- Identity flows as a typed record (`principal = behavior:{installation}` within scope); never ambient context.
- Enforcement is server-side: adapters validate read grants; the effect gate validates mutation grants + policies. SDK wrappers and manifest limits are ergonomics, not the wall.
- Secrets never reach behavior code or BehaviorHost; OAuth tokens and bot tokens live in connector grains via the existing config store.
- Every external mutation flows through `InoEffectPlanAuthority` (HMAC plan tokens, immutable revisions, execution proof, idempotent replay, payload scrubbing) — unchanged. The single v2 extension: the approval step consults the registry's policy records before falling back to an approval card. One gate, two approval sources, both leaving durable `ApprovalRecord` evidence.
- Pause/uninstall revokes server-side immediately (registry is the durable authority grains consult).
- Every run writes an audit record: installation + version, synapse id, correlation/causation, capabilities touched, proposals, outcomes.

## 14. Delivery, ordering, retries, deduplication, backpressure, loops

| Concern | Rule |
|---|---|
| Declaration | Subscriptions and emits come only from the manifest at install/update; no runtime subscription. |
| Ordering | FIFO per installation inbox by monotonic sequence; no cross-behavior ordering. |
| Delivery | At-least-once; ack by sequence recorded in the same write as state + ledger; crash mid-run redelivers; ledger returns recorded outcomes on replay. |
| Dedup | Inbox appends dedupe on synapse id; sources dedupe on provider keys (`update_id`, Gmail message id). |
| Retry/poison | Backoff 1m/5m/30m → park + feed card; 5 consecutive failures pause the installation; parked entries replayable after a fix. |
| Backpressure | Inbox caps (1,000 items / 8 MiB default); overwhelmed behavior pauses with notification; the rail never silently drops. |
| Loops | Depth stamped on emit; MaxDepth 8 rejects loudly; emits manifest-declared so cycles are visible at install. |
| Fan-out isolation | Dispatcher appends per-inbox with durable cursors; one failing inbox never blocks siblings. |
| Payload bounds | Synapse payload ≤256 KiB rejected before dispatch; state value 64 KiB / total 1 MiB caps; handler wall time 60 s; 3 model calls / 4,000 output tokens / 3 mutation intents per delivery. |

These defaults are product values to validate under load in the relevant slices, not architectural constants.

## 15. Test architecture and feedback budgets

| Layer | Content | Budget |
|---|---|---|
| 1 Pure transitions + behavior scenarios | registry/dispatcher/installation/memory/cursor state functions; behaviors on `FakeBrainContext` (no Orleans) | ms each; layer < 5 s — daily TDD lives here |
| 2 Provider contract tests | shared harness: cursor recovery, duplicates, revoked credentials, retry-after | < 10 s |
| 3 Grain integration | Orleans TestingHost: persistence, deactivate/reactivate mid-flow, lease/fence, reminders | < 60 s |
| 4 AppHost model tests | assert `appHost.Resources` graph + env projection without starting | < 5 s |
| 5 Full Aspire E2E | exactly two journeys: scheduled read-only → feed table; event → propose → approve → apply → verify | minutes; per-slice |
| 6 Flutter | golden/widget tests for retained journeys + behavior/memory screens | existing budget |
| 7 Deploy smoke | release candidates only | — |

Root gate: `dotnet test --logger "console;verbosity=minimal"` from repo root, no filters, zero skips. Determinism: frozen clock, seeded ids, loud-miss fake model (undeclared `ExtractAsync` throws with a copy-pasteable fix), recorded proposals, deterministic Roslyn builds. `FakeBrainContext` ships in `Behaviors.TestKit` with the shared step library; mutation behaviors receive a generated duplicate-delivery scenario by default. Deleted tests: the D1 legacy suite, TestKit.Tests, journal spikes — they protect implementation this design removes.

## 16. Aspire/Orleans composition

- AppHost: storage (clustering table; `grainstate`, `conversationstate`, `surfacefeedstate`, `sessionstate` blobs — `journal` deleted); `AddOrleans("kernel")` with clustering + default grain storage + reminders; RuntimeHost `.WithReference(orleans)`; Edge and BehaviorHost `.WithReference(orleans.AsClient())`; Ollama `llm` + `embed`; Flutter dev clients; secrets as parameters; `WithHttpHealthCheck` + `WaitFor` ordering (BehaviorHost waits for a healthy silo).
- Hosts use parameterless `UseOrleans()`/`UseOrleansClient()` with keyed Azure clients per Aspire-Orleans integration; the production managed-identity delta stays behind one focused extension until a deployed parity test retires it.
- Replicas: 1 everywhere until a lease test justifies more.
- Grain conventions: explicit `[GrainType]` aliases, scope-hashed keys, encrypted persistent state, pure transition functions + thin shells; additive-only interface evolution while an interface is deployed (data is disposable, so breaking changes are wipe-and-redeploy, not versioning ceremonies).

## 17. Deployment and process isolation

- Pulumi (live) continues deploying kernel + mcp container apps; BehaviorHost joins as a third container in the slice that makes it real: separate identity, no connector or storage secrets beyond the authenticated cluster channel, egress restricted to cluster + OTLP, read-only package mount, CPU/memory limits.
- Isolation truth (documented, not marketed): `AssemblyLoadContext` provides loading and unloadability only — officially not a security boundary. The BehaviorHost process boundary is the containment unit; packages are human-approved trusted code in v1; OS-level restrictions harden the same boundary later without redesign.
- The workbench is a short-lived spawned process with no cluster access and no secrets; compile + scenario run + hash, then exit.

## 18. Quantified targets

| Measure | Direction |
|---|---|
| Gross deletion before new rail code | 8–10k of ~63k tracked code/config lines (13–16%) |
| Net C# after rail lands | −4–5k lines vs today |
| NuGet packages | −5 (Journaling ×2, Redis ×3); no new preview/alpha packages |
| Flutter direct deps | −8 to −9 |
| Storage resources | −1 (`journal`); zero added |
| C# projects | flat at 17 (six removed/merged, six added) |
| New grain types | 3 (+1 memory) vs v1's 7 |
| Processes | 3 (+BehaviorHost; CLI deleted from plan) |
| Comment lines in prohibited types | 0 |
| Migration/dual-write/compat code | 0 lines, permanently |

## 19. Decisions and rejected alternatives

Kept from v1: C# scripts as the substrate (F1); trusted BehaviorHost owning the only behavior-side Orleans client (F2); durable inbox rail, no stream providers as correctness rail (F3 — corrected rationale: durable providers exist upstream, but lack per-subscriber ack/replay/poison/depth semantics); graduated trust (F4); attribute-declared provider-scoped capability ids (F5); one behavior = one package (F6); Synapse/Neuron terminology with the generic runtime deleted (F7); manifest derived from code; effect-gate unification; loud-miss BDD; zero-skip testing.

Changed from v1: `ctx.Gmail`-style SDK members → `ctx.Capability<T>()` + contract packages; 7 grains → 3; separate BehaviorState/Inbox → one installation grain (ack+state+ledger in one write); Schedule/ApprovalPolicy/SubscriptionRegistry → registry-owned; `behaviorstate` storage provider → existing default provider; CLI → deferred (workbench = spawned process; management = API + Flutter + MCP); Memory added as platform capability provider; Ui projects retained and merged instead of deleted; tenant machinery deleted rather than deferred.

Rejected: universal resource/command bus (Alt 3 — repo history, typing, authorization); integration-specific SDK members (Alt 1 — churn, god interface); Orleans Journaling (alpha); Orleans streams/broadcast for delivery; reflection-based provider discovery; method-name-derived capability ids; LLM-as-judge authorization; effect-gating memory writes; a second UI rail; migration/compatibility machinery of any kind.

## 20. Unresolved questions

None block the architecture. Three decisions are deliberately deferred to the slice that owns them: (a) encrypted-envelope practical size ceiling for installation-grain state under load (measure in slice 1; shard by partition key without SDK change if exceeded); (b) workbench process user/permissions per OS (authoring slice); (c) embedding model + dimensions for memory (memory slice; model id is versioned data by design).

## Change-from-v1 ledger

| v1 element | v2 disposition |
|---|---|
| F1–F7 decisions | Retained (F5 mechanism now attribute-metadata in contract packages) |
| 7 behavior-rail grains | Merged to 3 |
| `IBrainContext` provider members | Replaced by `ctx.Capability<T>()` + contract packages |
| `DigitalBrain.Behaviors.Sdk` / `TestKit` / `BehaviorHost` | Retained as planned |
| `tools/DigitalBrain.Brain` CLI | Deleted from plan (workbench process + runtime API + Flutter/MCP management) |
| `behaviorstate` storage provider (Amendment G) | Superseded: default provider, zero new resources |
| Amendment A (registry owns subscriptions) | Absorbed and extended (schedules + policies too) |
| Amendment D (persistent state over Journaling) | Confirmed; Journaling deleted outright |
| Amendment E (configured single scope) | Trivially satisfied by single-principal decision |
| Amendment F (defer ProposalDecided/timezone UI) | Retained |
| Plan §11 migration policy | Deleted (disposable data) |
| Plan Tasks 1.4/1.5 (delete Ui projects, prune Flutter features) | Reversed per evidence + product decision; merged instead |
| Plan §7.6 operational limits | Adopted as defaults to validate (§14) |
| Multi-tenancy / sharing / marketplace sections | Deleted |
| Both v1 documents | Deleted on acceptance of this spec |
