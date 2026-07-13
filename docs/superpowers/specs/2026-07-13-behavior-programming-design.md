# DigitalBrain Behavior Programming — Approved Design

Date: 2026-07-13 · Branch: codex/architecture-convergence-zero (HEAD 2b6c4b6) · Status: design approved, implementation plan pending

## 1. Purpose

DigitalBrain becomes programmable: a user request like "every week, show me email counts by sender" or "when an email arrives, propose the matching Salesforce update" becomes a durable, tenant-scoped, versioned, shareable automation called a **Behavior**. The generic kernel provides integrations, execution, safety, identity, scheduling, eventing, observability, and lifecycle; business logic lives in user/company Behaviors, never accumulating inside the kernel.

## 2. Verified current state (evidence base)

Established by a 13-agent adversarially-verified evidence sweep over E:\brain, E:\IAW, E:\digitalbraintech\Projects, and Microsoft Learn Orleans docs (Context7 quota was exhausted; Microsoft Learn was the stated fallback):

- The INO path is real and strong: `ino_interact` → `ConversationNeuron` (durable state machine, inbox idempotency, lease/fence, outbox) → `InoOperationWorkerGrain` → `AgentFrameworkWorkflowRunner` → `PlanInoToolGateway` → `InoEffectPlanAuthority` HMAC plan tokens → `InoEffectPlanNeuron` (immutable revisions, execution proof, idempotent replay, payload scrubbing) → connector grains. Human approval is a durable conversation transition gated by grants.
- Contrary to CLAUDE.md/README, the legacy generic Neuron/Synapse runtime was NOT removed: `Neuron : DurableGrain` base, timeline stream machinery, `LlmNeuron`/`LlmResponderNeuron` are live and silo-registered, dormant only because no stream provider is registered (`KernelCompositionTests.cs:87`).
- Eventing substrate today: Orleans reminders + the durable conversation Outbox → SurfaceFeed → gRPC watch rail. No cron, no trigger registry, no production stream provider, no grain-call filters; identity flows explicitly as a typed record keyed by SHA-256 scope hashes.
- IAW's generated-code path is the documented anti-pattern: LLM-generated console apps run as raw OS processes with full host privileges plus an unauthenticated `IClusterClient`; its approval middleware bypasses by construction for non-numeric grain keys.
- Historical DigitalBrain: the Synapse metadata envelope, typed `IHandle<T>` dispatch, and staged→promoted→retired lifecycle survived every rewrite; the broadcaster god-object, event-per-RPC grain-call filter, global-timeline-with-client-side-filtering, and silently-skipping BDD runner (`NeuronTestRunner.cs:73-76`, verbatim) are the documented failure modes. Cross-era lesson: mechanisms where a miss throws or fails compilation stayed healthy; mechanisms where a miss warns or skips rotted.
- Orleans constraints: memory streams are not durable; no ADO.NET stream provider exists; durable providers are at-least-once without FIFO under failure and need PubSubStore plus a resume protocol; broadcast channels are lossy; reminders are minute-granularity with missed-tick-skipped semantics; interface versioning is additive-only; in-process .NET isolation is not a security boundary.

## 3. Decisions (all approved)

| # | Fork | Decision |
|---|---|---|
| F1 | Behavior substrate | C# scripts are the authoritative user programs (Option B). No behavior IR/DSL. |
| F2 | Trust boundary | B2: scripts run inside a trusted platform BehaviorHost that owns the only Orleans client; scripts receive a typed `IBrainContext`, never a cluster client. Real enforcement is server-side grants. |
| F3 | Event delivery | Durable subscription inbox rail extending the proven Outbox/SurfaceFeed pattern. No Orleans stream providers anywhere. |
| F4 | Standing authority | Graduated trust: behaviors install in propose-mode; users promote observed action shapes to auto-apply policies (durable, constrained, deterministic). |
| F5 | Capability identity | Provider-scoped tool ids (`salesforce.record.update`, `gmail.message.send`) are the single currency for grants, policies, plan tokens, and manifests. Derived-from-method-name ids rejected (security identity must not change on rename). Neutral `crm.*` layer deferred until a second provider of a kind exists. |
| F6 | Package unit | One Behavior = one package (`behavior.cs` + `behavior.feature` + `manifest.json` + tests). App is at most a future grouping label. |
| F7 | Terminology | Synapse = user-visible typed domain event with causation envelope. Neuron = platform naming convention for durable tenant-scoped typed grains. Legacy generic runtime deleted. |

## 4. Architecture

```
Flutter UI ──┐                              ┌── local dev: dotnet test / brain CLI
             │                              │
        MCP edge (Orleans client)      BehaviorHost (Orleans client, PLATFORM code)
        ino_interact · UI gRPC           loads installed Behavior packages
             │                           invokes scripts with IBrainContext
             │                           per-behavior identity + grants
─────────────┼──────────── Orleans cluster (RuntimeHost ×3) ────────────────────
             ▼
  INO path (unchanged): ConversationNeuron → InoOperationWorker
    → AgentWorkflowRunner → PlanInoToolGateway → InoEffectPlanNeuron
    → connector neurons (Gmail / Salesforce)          ▲
                                                      │ behavior mutations enter
  Behavior rail (new):                                │ the SAME effect gate
    SubscriptionRegistryNeuron (tenant) ── who hears what
    SynapseDispatcherNeuron   (tenant) ── fan-out, depth check
    BehaviorInboxNeuron     (behavior) ── durable seq, ack, poison count
    BehaviorRegistryNeuron    (tenant) ── installed behaviors, versions, status
    ApprovalPolicyNeuron      (tenant) ── graduated-trust policy records
    ScheduleNeuron            (tenant) ── reminders → emits ScheduleFired synapse
    BehaviorStateNeuron     (behavior) ── small durable KV for scripts
```

Two unifications keep the system small:

1. **Schedules are synapses.** `ScheduleNeuron` holds Orleans reminders and emits `ScheduleFired` like any connector event. A behavior has exactly one input shape: a synapse arriving in its inbox.
2. **Behavior mutations enter the existing gate.** `ctx.Salesforce.ProposeUpdateAsync(...)` files an effect plan exactly as a conversation does. The only extension: the approval step consults `ApprovalPolicyNeuron` before falling back to a surface-feed approval card. One gate, two approval sources (human click or policy record), both leaving durable `ApprovalRecord` evidence.

## 5. Terminology rules

| Term | Meaning | Rules |
|---|---|---|
| Neuron | Platform convention: durable, tenant-scoped grain with typed interface | `[GrainType]` alias, scope-hashed key, encrypted persistent state, interface in Abstractions. Not a base class. Users never see one. |
| Synapse | Product concept: typed domain event a behavior can subscribe to | Immutable record + envelope (SynapseId, CorrelationId, CausationId, Depth, traceparent). Explicit emission only — never from grain-call filters. Versioned type alias. Payload carries ids + minimal facts, not full documents. MaxDepth kills loops. |
| Behavior | One installed user automation; unit of install/version/share | Package per §6. Tenant-scope-owned, registry status: propose-mode / active / paused / retired. |
| Trigger | What wakes a behavior | Always a subscription: synapse type + optional equality filter. Schedules included via ScheduleFired. |
| Feature/Scenario | The user-readable contract of a behavior | Reqnroll-executable; an unmatched step fails, never skips. |
| Policy | Standing authority for one behavior + one tool id | Durable record: constraints, rate budget, optional expiry. Deterministic evaluation at the plan gate; cited in ApprovalRecord. |

Not concepts: App (future grouping label only), separate "program artifact" (the package is the program), Synapses from commands/telemetry/state-changes/RPCs.

## 6. Behavior package and programming model

A behavior references exactly one dependency: the `DigitalBrain.Behaviors.Sdk` NuGet (interfaces, synapse records, attributes; no Orleans reference). One typed handler per trigger:

```csharp
[Behavior("weekly-email-stats")]
[OnSchedule("0 8 * * MON")]
public sealed class WeeklyEmailStats : IBehaviorOn<ScheduleFired>
{
    public async Task HandleAsync(ScheduleFired synapse, IBrainContext ctx, CancellationToken ct)
    {
        var messages = await ctx.Gmail.ReadIncomingAsync(TimeRange.LastDays(7), ct);
        var rows = messages
            .GroupBy(m => m.SenderEmail)
            .Select(g => new SenderCount(g.Key, g.Count()))
            .OrderByDescending(r => r.Count);
        await ctx.Surface.ShowTableAsync("Weekly email volume by sender", rows, ct);
    }
}
```

```csharp
[Behavior("email-to-crm")]
[OnSynapse(typeof(EmailReceived))]
public sealed class EmailToCrm : IBehaviorOn<EmailReceived>
{
    public async Task HandleAsync(EmailReceived synapse, IBrainContext ctx, CancellationToken ct)
    {
        var message = await ctx.Gmail.ReadMessageAsync(synapse.MessageId, ct);
        var facts = await ctx.Model.ExtractAsync<CrmFacts>(message, ct);
        if (!facts.RelevantToCrm) return;

        var contact = await ctx.Salesforce.FindContactByEmailAsync(message.SenderEmail, ct);
        if (contact is null)
        {
            await ctx.Salesforce.ProposeCreateContactAsync(facts.AsNewContact(), ct);
            return;
        }
        await ctx.Salesforce.ProposeUpdateAsync(contact.Id, facts.AsFieldChanges(), ct);
    }
}
```

`Propose*` files an effect plan and returns `Applied` (policy covered it), `Proposed` (approval card in feed), or `Rejected`. Scripts never block on humans; behaviors that care subscribe to `ProposalDecided` (see unresolved #8).

### IBrainContext

| Member | What | Backing |
|---|---|---|
| `ctx.Gmail`, `ctx.Salesforce` | Typed read capabilities + `Propose*` mutation intents | BehaviorHost wrappers over existing tool grains; calls carry behavior identity; grants checked server-side |
| `ctx.Model` | Bounded LLM extraction/classification `ExtractAsync<T>` | Existing `BoundedNoRetryChatClient` policy; token budget from manifest |
| `ctx.Surface` | Cards/tables to the user's feed | Existing SurfaceFeed rail |
| `ctx.State` | Durable KV scoped to the behavior | `BehaviorStateNeuron`, encrypted persistent state |
| `ctx.Emit(synapse)` | Emit a declared synapse type; Depth+1 stamped | Dispatcher rail |
| `ctx.Clock`, `ctx.Log` | Deterministic time, structured logging | Injectable in tests |

Deliberately absent: `IGrainFactory`, `HttpClient`, filesystem, process, secrets, configuration. The SDK types cannot express them.

Because behaviors depend only on `IBrainContext`, per-behavior tests need no Orleans: the TestKit ships `FakeBrainContext` and scenarios run in milliseconds.

### Manifest — derived from code, never hand-maintained

`brain pack` compiles the script, derives the manifest from attributes + capability usage, and writes it alongside; install re-derives and rejects on mismatch.

```json
{
  "id": "email-to-crm",
  "version": "1.2.0",
  "description": "When an email arrives, extract CRM-relevant facts and propose Salesforce updates",
  "sdk": "1.x",
  "triggers": [{ "synapse": "gmail.message.received.v1" }],
  "capabilities": {
    "reads": ["gmail.message.read", "salesforce.record.read", "salesforce.record.search"],
    "mutations": ["salesforce.record.update", "salesforce.record.create"],
    "model": { "extract": true, "maxTokensPerRun": 4000 }
  },
  "emits": [],
  "state": false,
  "limits": { "maxRunSeconds": 60, "maxProposalsPerRun": 3 },
  "packageHash": "sha256:…"
}
```

### Modifying a behavior

`brain pull <id>` returns the installed source. Edit → `dotnet test` against fakes → `brain install` bumps the version. Capabilities unchanged → policies and state carry over. Capabilities grew → re-approval of the delta; affected auto-apply policies demote to propose-mode until re-promoted. Previous version retained for one-click rollback.

## 7. Lifecycle: natural language → running behavior

```
user request (ino_interact)
  1. clarify (only if ambiguous)
  2. propose the .feature            ── GATE A (human): approve the contract
  3. generate behavior.cs (workbench: isolated process, no cluster access,
     zero-comment analyzer enforced)
  4. compile + run .feature against FakeBrainContext + unit tests
  5. show scenario-level results     ── red/undefined scenario = no install button
  6. install                         ── GATE B (human): approve the permissions
     package hashed → blob; registry entry; subscriptions + schedule created;
     starts in propose-mode
  7. observe: registry shows runs, errors, proposals; causation stamped
  8. promote mutations to auto-apply ── GATE C (human): graduated trust
  9. update = new version through 2–6 with diff view; capability growth
     re-triggers GATE B and demotes affected policies; rollback = one click
```

Runtime failure: failing run → retry with backoff → park entry (poison) + feed card; K consecutive failures auto-pause the behavior. Parked entries replay after a fix.

## 8. Testing / BDD model

| Layer | Runs | Against | Notes |
|---|---|---|---|
| Scenario tests (.feature) | per behavior, ms | FakeBrainContext, no Orleans | Reqnroll; undefined/pending steps are failures |
| Unit tests | per behavior, optional | plain code | complex transforms |
| Contract tests | platform repo | TestCluster: wrappers ↔ real tool grains | prove `ctx.Gmail` and `IGmailReadToolGrain` agree |
| Rail integration | platform repo | TestCluster: dispatcher, inbox, policy gate | duplicate delivery, poison, depth loops |

FakeBrainContext is deterministic: frozen clock, seeded ids, recorded proposals, loud-miss fake model (undeclared `ExtractAsync` throws with a copy-pasteable fix — the ino-era `BddMockMissException` pattern). SDK ships a shared step library bound to the fakes; behaviors may add custom steps. Mutation behaviors get a generated duplicate-delivery scenario by default. Silent step-skipping is banned: the historical `NeuronTestRunner` vacuous-green failure is the explicit anti-goal.

### Worked contract A — weekly email statistics

```gherkin
Feature: Weekly email statistics
  Every Monday at 08:00 I get a table of last week's email volume by sender.

  Scenario: Weekly summary appears in my feed
    Given my mailbox received these emails in the last 7 days
      | sender             | count |
      | anna@acme.com      | 12    |
      | billing@stripe.com | 4     |
    When the weekly schedule fires
    Then my feed shows a table "Weekly email volume by sender"
      | sender             | count |
      | anna@acme.com      | 12    |
      | billing@stripe.com | 4     |

  Scenario: A quiet week still reports
    Given my mailbox received no emails in the last 7 days
    When the weekly schedule fires
    Then my feed shows a table "Weekly email volume by sender" with no rows
```

### Worked contract B — email to Salesforce

```gherkin
Feature: Email to Salesforce
  When an email arrives with CRM-relevant facts, propose the matching Salesforce
  change; apply only after my approval or a standing policy.

  Scenario: New phone number for a known contact
    Given Salesforce has contact "Anna Karlsson" with email "anna@acme.com" and empty phone
    And an email arrives from "anna@acme.com" saying "my new number is +46 70 123 45 67"
    And the analysis extracts phone "+46 70 123 45 67" for the sender
    When the behavior handles the email
    Then a Salesforce update is proposed setting "Phone" of "Anna Karlsson" to "+46 70 123 45 67"
    And nothing is written to Salesforce yet

  Scenario: A newsletter is ignored
    Given an email arrives from "news@marketing.io"
    And the analysis finds nothing CRM-relevant
    When the behavior handles the email
    Then no Salesforce change is proposed

  Scenario: The same email delivered twice proposes only once
    Given an email arrives from "anna@acme.com" with a new phone number
    When the behavior handles the same email twice
    Then exactly one Salesforce update is proposed

  Scenario: An approved proposal is applied and verified
    Given a proposed update setting "Phone" of "Anna Karlsson" to "+46 70 123 45 67"
    When I approve it in my feed
    Then Salesforce contact "Anna Karlsson" has phone "+46 70 123 45 67"
    And the applied change is verified against Salesforce and recorded in the audit log
```

The last scenario is a platform contract surfaced in the behavior's feature: approval/apply happen in the gate; "verified against Salesforce" is where the Task 7 `VerifyUpdateAsync` thread lands (post-apply check + `OutcomeUnknown` resolver).

## 9. Subscription and delivery semantics

| Concern | Rule |
|---|---|
| Declaration | Subscriptions come only from the manifest at install/update. Scripts cannot subscribe at runtime. |
| Tenant scoping | Registry and dispatcher are per-tenant-scope grains; synapses carry scope; cross-tenant delivery structurally impossible. |
| Ordering | Per-inbox FIFO by monotonic sequence. No cross-behavior ordering. |
| Delivery | At-least-once; host acks by sequence after a run; crash mid-run redelivers. |
| Filtering | v1: optional field-equality filters at dispatch; anything smarter is a `return` in the script. |
| Retry/poison | Backoff retries → park + feed card; K consecutive failures pause the behavior. Parked entries replayable. |
| Backpressure | Inboxes capped; overwhelmed behavior pauses with notification; the rail never silently drops. |
| Loops | Depth stamped on emit; MaxDepth rejects loudly. Emits must be manifest-declared, so cycles are statically visible at install. |
| Fan-out isolation | Dispatcher appends per-inbox independently with durable cursor; one failing inbox never blocks siblings. |
| Unsubscribe | Pause/uninstall removes registry entries immediately; inbox retained for the audit window. |
| Observability | Per-behavior registry card: inbox lag, last run, failure count, pending proposals. Tenant-level capped synapse journal. |
| Sources | Connectors own detection: v1 `GmailWatchNeuron` polls on a reminder with a history cursor and emits `EmailReceived`; push channels can replace polling without touching the rail. |

## 10. Security and capability model

```
user grants (session)  ⊇  behavior capabilities (install, GATE B)
                       ⊇  auto-apply policies (promotion, GATE C)
```

- A behavior can never hold a capability its installer lacks.
- Real boundaries: tool grains validate read grants; the effect gate validates mutation grants + policies. Capability wrappers and manifest limits are DX, not the wall.
- `BehaviorRegistryNeuron` is the durable authority grains consult (the `RuntimeSessionAuthority` pattern); pause/uninstall revokes server-side immediately.
- Identity flows explicitly as a typed record (`principal = behavior:{id}` within tenant/workspace scope); never ambient RequestContext.
- Secrets never reach behaviors; OAuth tokens stay inside connector grains via the existing config store.
- In-process containment (run timeout, cancellation injection, model budget) is defense-in-depth only; .NET in-proc isolation is not a security boundary and the design never relies on it. Per-tenant BehaviorHost processes become the OS-level tenant boundary when multi-tenancy is real.
- Every run writes an audit record: behavior id + version, synapse id, correlation/causation, capabilities touched, proposals, outcomes.

## 11. Multi-tenancy, versioning, sharing

- Scopes: personal (principal-scoped) or workspace (company-scoped, admin grant required). Policies inherit scope; workspace policy ceilings cap what members may auto-apply.
- Versions: one active version per behavior per scope; registry retains history + hashes; rollback re-points. `ctx.State` keyed by behavior id, survives upgrades, deleted after uninstall retention window.
- Sharing: export = the source package. Recipients re-run GATES A/B on their tenant; permissions never travel with the package. Future marketplace = signed source packages + catalog; no design change required.

## 12. IAW and historical dispositions (summary)

COPY from IAW: compile-time interface metadata idea (as manifest data, not static abstracts), facet state injection, compact Aspire composition + model tiering, durable per-agent scheduling concept (realized here as ScheduleNeuron on Orleans reminders, not the DurableJobs package), ledger-style audit grain. ADAPT: typed scoped lookup ergonomics, typed stream-marker subscription ergonomics (as `IBehaviorOn<T>`), approval UX scopes. REJECT: generated-code-as-OS-process, Roslyn compile against AppDomain, string-patch sanitization, LLM-as-judge authorization, approval middleware with bypass-by-default, Agent god-class, volatile "durable" storage, string-key identity heuristics.

COPY from historical: Synapse metadata envelope, stamp-on-emit causality, typed `IHandle<T>` shape, loud-miss BDD mock, framework-owned step matching, capped journals. ADAPT: depth+visited loop lessons (carried in-envelope), bounded durable synapse journal, staged→promoted→retired lifecycle, tenant scope in grain identity (typed keys). REJECT: string-dict envelope + reflection reconstruction, broadcaster god-object, event-per-RPC filters, global timeline with client-side filtering, silent-skip Gherkin, prompt-substring mocks, reflection/string DI with bare catch, ambient AsyncLocal context, lossy identity bridges.

## 13. BrainProgramming.md disposition

Carried: single query record + static factory presets; pure operations core + thin grain shell (`ConversationTransitions` pattern); Salesforce-before-Gmail sequencing; fewer-contract-types-shrink-hallucination-surface (now applied to the SDK). Rejected: method-derived tool ids; `static abstract IToolAgent` metadata on grain interfaces; single `ISalesforce` mega-interface as written (drops Verify, collapses mutation-status richness the gate depends on); all references to removed components (`FoundryCompilation`, `PackAlcEmbodier`, `CapabilityGate`, `MarketplaceNeuron`, `IOAuthProviderAdapter`, `OrchestrationCompiler`). The document is deleted after harvest.

## 14. Migration impact on the current branch

Task 7 worktree:
- Thread A (`VerifyUpdateAsync` stack): keep and finish; becomes the gate's post-apply verification and `OutcomeUnknown` resolver; collapse the duplicated inline verify in `ApplyUpdateAsync` to one path.
- Thread B (`IInoOperationCapability`): concept survives as the gate's four-phase capability seam; the `crm.*` id scheme is replaced by provider-scoped ids; the broken test referencing the missing `SalesforceInoOperationCapability` is finished under that scheme.
- `SalesforceReadContracts.cs`: line-ending artifact, normalize.
- `docs/BrainProgramming.md`: delete after harvest.

Delete (finishing what Tasks 1–6 claimed): `Neuron` base + `INeuron`, old `Synapse`/`SynapseStream`/`SynapseDispatch`/`IHandle`, `NeuronJournals`, `LlmNeuron`, `LlmResponderNeuron`, `Signals`/`DbSynapses`, checkpoint/branch protectors, legacy `NeuronScope`, Synapse-derived UI contracts (`DataChartGenerated`), TestKit timeline pieces (`ProbeNeuron`, timeline stream config, `TimelineStreamTests`, `BroadcastReactivityTests`). Fix stale README E2E reference and CLAUDE.md removal claims. Keep the pack config store (per-tenant connector config).

Build (phasing in the implementation plan): Behaviors SDK · Synapse envelope + `EmailReceived`/`ScheduleFired` v1 · SubscriptionRegistry/SynapseDispatcher/BehaviorInbox/BehaviorRegistry/ApprovalPolicy/Schedule/BehaviorState neurons · gate policy hook · BehaviorHost (Aspire resource) · workbench pipeline · `FakeBrainContext` + shared step library · `GmailWatchNeuron` · registry/approval feed cards · brain CLI verbs (pack/test/install/pull).

## 15. Unresolved decisions (deferred; none block the architecture)

1. Schedule timezone semantics (user TZ vs UTC).
2. Subscription filter language beyond v1 equality.
3. `ctx.State` size cap and eviction.
4. Trigger point for per-tenant BehaviorHost processes (single host until multi-tenancy is real).
5. Model-token budget accounting/pricing per behavior.
6. Retention values: inbox cap, audit window, parked-entry TTL, MaxDepth, pause threshold K.
7. Workbench isolation depth (process user, NuGet feed allowlist).
8. `ProposalDecided` synapse in v1 or deferred.
9. Timing of Salesforce read-contract consolidation (live callers; likely after the rail).
10. Inbox persistence primitive: `EncryptedPersistentState` (default, matches INO grains) vs `Orleans.Journaling` `IDurableList` — needs a short spike.

## 16. Non-goals

- No behavior IR/DSL; no interpreted workflow language.
- No arbitrary generated-code execution outside the BehaviorHost model; no raw cluster clients for user code.
- No Orleans stream providers, broadcast channels, or grain-call-filter event synthesis.
- No LLM-as-judge authorization.
- No App packaging unit, marketplace runtime, or provider-neutral capability layer in v1.
- No mutation path that bypasses `InoEffectPlanAuthority`.
