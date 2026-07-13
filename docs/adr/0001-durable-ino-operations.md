# ADR 0001: Orleans owns durable INO operations

Status: Accepted

Date: 2026-07-12

## Decision

INO accepts a command exactly once through the Orleans conversation grain. Orleans is authoritative for command idempotency, operation lifecycle, journal, leases, approvals, external-effect state, outbox, and durable feed state. Every other component is an adapter around that authority.

## Step 1 — challenged requirements and accountable owners

No personal names were supplied, so ownership is expressed as accountable roles. The request owner is the NeuroOS product owner; the implementation decision owner is the DigitalBrain principal engineer.

| Requirement | Accountable owner | Challenge and decision |
| --- | --- | --- |
| Acceptance, idempotency, journal, lifecycle, leases, approvals, effects, outbox, feed state | Orleans domain-runtime owner | A conversation grain is the sole writer. UI, Agent Framework, gateways, and workers may not keep a second lifecycle state. |
| Model provider boundary, retries, limits, and telemetry | AI integration owner; Platform/SRE owns policy values; Security owns any future safe-retry classifier | Microsoft.Extensions.AI IChatClient is the only model-provider boundary. Generic provider calls have one bounded, no-blind-retry policy. |
| Isolated agent execution and workflow sessions | Workflow integration owner | Agent Framework is used only behind IAgentWorkflowRunner and cannot advance domain state. |
| Topology, discovery, health, and telemetry export | Platform owner | Aspire owns infrastructure topology, not business orchestration or recovery. |
| Receipt delivery and feed rendering | UI transport owner | A short RPC deadline returns a durable receipt; it must not become an execution deadline. |
| Mutation safety and outcome truthfulness | Safety/policy owner | A mutation is typed, least-privileged, approval-gated, and never blindly retried after an uncertain provider outcome. |
| Prompt, token, OAuth, credential, and provider-payload protection | Security owner | Logs and telemetry use safe identifiers and bounded classifications only. |

## Step 2 — deleted paths

The system deletes these competing paths instead of maintaining parallel behavior:

- request-owned synchronous SubmitAction to ExecuteAsync work and all propagation of HTTP/gRPC cancellation after acceptance;
- feed-watch-driven recovery, direct RuntimeSurfaceFeed operation projection, and duplicate snapshot/projection writers;
- unfenced grain mutation methods that could suspend, complete, or enqueue without the current lease fence;
- raw OAuth continuation persistence and direct callback-to-workflow execution;
- lifecycle-only new/delete UI actions that synchronously changed a conversation or feed outside the acceptance transaction; and
- the legacy MCP integration gateway and direct command execution route.

This is a net reduction: transport accepts or observes, workers schedule or execute a claimed operation, and only Orleans advances durable state.

## Step 3 — one authority per concern

| Concern | Sole authority | Durable records / boundary |
| --- | --- | --- |
| Acceptance, idempotency, operation state, lease fences, approval, effect intent/outcome, outbox | Orleans IConversationNeuron | AcceptedCommand, ConversationOperation, ApprovalRecord, EffectRecord, ConversationOutboxEntry |
| Immutable operation-phase projection and feed delivery state | Orleans dispatcher and ISurfaceFeedNeuron | OperationOutboxRecord, ordered outbox Sequence, bounded EventHistory, cursor |
| Home composer initialization (not an operation phase) | ISurfaceFeedNeuron | typed HomeSurfaceBootstrap creates only the signed initial composer binding |
| Model transport policy | Microsoft.Extensions.AI | bounded IChatClient pipeline: four concurrent calls, 90-second queue/call deadline, one attempt when safe submission cannot be proved |
| Agent session execution | IAgentWorkflowRunner | Opaque WorkflowReference only: runner, workflow ID, session ID, optional checkpoint |
| Provider mutation | Registered typed IInoToolGateway implementation | Immutable approved InoToolEffectRequest with provider idempotency key |
| Discovery, health, and OTEL export | Aspire | AppHost/resource configuration only |

Persisted enum values are compatibility data. New phases are appended instead of renumbering existing values: Approved = 10 and ApplyingEffect = 11.

## Acceptance, durable handoff, and execution

~~~mermaid
sequenceDiagram
    participant U as Flutter UI
    participant G as UiGrpcService
    participant C as Conversation grain
    participant W as Operation worker
    participant A as IAgentWorkflowRunner
    participant M as IChatClient
    participant D as Outbox dispatcher
    participant F as Surface feed grain

    U->>G: SubmitAction(signed binding, idempotency key)
    G->>G: authenticate + bounded input/binding validation
    G->>C: BeginOperation
    C->>C: persist AcceptedCommand + Operation(Accepted) + accepted outbox atomically
    C->>W: ScheduleAsync(register worker reminder only)
    C-->>G: durable OperationReceipt
    G-->>U: receipt immediately

    Note over C,W: The conversation reminder waits only for idempotent worker reminder registration. It never waits for the dispatcher.
    W->>D: ScheduleAsync after the durable worker handoff
    W->>C: TryClaimOperation(current revision, owner, lease, fence)
    C-->>W: claimed operation or duplicate/stale result
    W->>A: ExecuteAsync(operation, worker-owned deadline)
    A->>M: model invocation
    M-->>A: typed result + opaque workflow reference
    A-->>W: result
    W->>C: lease-fenced phase transition + immutable outbox entry
    D->>C: read ordered immutable outbox entries
    D->>F: apply OperationOutboxRecord
    F-->>U: authoritative ordered feed event
~~~

`IInoOperationWorkerGrain.ScheduleAsync` is the only interleavable method. It performs only idempotent reminder registration—never a state transition or provider call—so the specific conversation → worker → conversation reminder handoff cannot deadlock. The outbox dispatcher remains serialized.

The acceptance boundary receives no request cancellation token. UiGrpcService may use request cancellation while authenticating and validating, then calls the acceptance rail with CancellationToken.None. A client disconnect after receipt cannot cancel accepted work. Orleans owns the operation lifecycle deadline; the IChatClient boundary owns its shorter provider queue/call deadline.

Every state-changing worker transition carries the current lease fence. Duplicate same-owner claims do not execute twice; a stale worker cannot suspend, approve, retry, complete, or publish after a newer lease exists.
After a model or provider result is observed, a concurrent outbox/feed revision is reconciled by re-reading and persisting that exact result under the original fence. It is never treated as a reason to invoke the model or provider again. Model deadline/failure transitions are terminal Failed, not RetryScheduled; only expired-lease recovery can schedule a replay, and it reuses the persisted workflow/session reference.

## Approval and external-effect lifecycle

~~~mermaid
sequenceDiagram
    participant W as Claimed worker
    participant C as Conversation grain
    participant F as Authoritative feed
    participant U as UI
    participant G as UiGrpcService
    participant T as Typed tool gateway

    W->>C: RequestApproval(effect intent, lease fence)
    C->>C: persist AwaitingApproval + immutable EffectRecord + phase outbox
    C->>F: dispatcher emits approval projection
    F-->>U: signed approval binding from feed state
    U->>G: decision(binding, revision, decision ID)
    G->>C: DecideApproval(actor scope, decision ID)
    C->>C: atomically persist decision + Approved phase + outbox
    W->>C: claim and transition to ApplyingEffect
    W->>T: ExecuteApprovedAsync(effect ID, tool, scope, provider idempotency key)
    T-->>W: success, failure, or outcome unknown
    W->>C: lease-fenced terminal phase + outbox
~~~

Approval decisions are actor-bound and idempotent by decision ID; a conflicting actor or verdict is rejected. Approval, approved, applying, success, failure, and uncertain outcome are distinct phases with distinct immutable outbox records.

After approval, effect ID, tool ID, scope, and provider idempotency key are immutable. If a provider mutation began but cannot be confirmed, the worker records OutcomeUnknown and schedules only explicit verification/reconciliation. It never blindly retries the mutation.
Applying and terminal effect records retain the typed tool and effect IDs, so the durable outbox and trace chain remain correlated through recovery.

ClosedInoToolGateway is the safe default. Typed provider mutation integrations are intentionally deferred until they can satisfy least privilege, durable approval, provider idempotency, and outcome verification.

## OAuth suspension and exact workflow resumption

~~~mermaid
sequenceDiagram
    participant W as Claimed worker
    participant C as Conversation grain
    participant P as Provider credential store
    participant R as Worker reminder
    participant A as IAgentWorkflowRunner

    W->>C: SuspendAuthorization(opaque provider/tool/attempt/flow reference, WorkflowReference, lease fence)
    C->>C: persist AwaitingAuthorization + phase outbox; scrub legacy raw input
    Note over C,P: No OAuth callback body, token, credential, or provider payload is stored in the operation.
    P-->>P: provider callback completes credentials
    R->>P: bounded readiness probe
    R->>C: TryClaimAuthorization(operation ID, attempt ID, owner, lease)
    C-->>R: exactly one durable authorization claim
    R->>A: ExecuteAsync(prior WorkflowReference)
    A-->>R: resumes the same workflow/session, not a second workflow
~~~

Only the Orleans-owned operation-to-WorkflowReference mapping is persisted. The runner accepts a prior reference only when its runner name and operation-derived workflow ID match, then uses the exact session/checkpoint reference. Duplicate callbacks affect readiness only; they never receive an operation lease or execute a workflow.

## Outbox, feed, UI, and correlation invariants

Each operation phase has one OperationOutboxRecord whose event identity derives from operation ID, phase, and operation version. The dispatcher is the only caller of generic ApplyProjectionAsync and projects its stored immutable payload; it does not rebuild a mutable latest conversation snapshot. ConversationOutboxEntry.Sequence orders delivery. If a projection cannot target the current durable conversation presentation, the dispatcher leaves the entry pending rather than marking it delivered and losing it.

The initial home composer is not an operation phase. ISurfaceFeedNeuron owns a typed, identity-bound HomeSurfaceBootstrap transition that creates the first signed send binding and can restore it after a feed rebuild. It cannot advance an operation, and a persisted operation phase always wins over an empty bootstrap. This is the only feed-initialization exception; it is not a second operation projection path.

ISurfaceFeedNeuron retains bounded server-enforced event history and projection payloads. ReadPage uses the durable cursor and returns a reset when the requested cursor falls behind retention. Client input and batch limits are not widened to mask oversized server projections.

UI action proofs are derived only from authoritative feed state. Missing, expired, and stale-revision actions map to a precondition failure; invalid signature, scope, owner, or binding remains permission denied. A lost receipt reconciles from authoritative feed state even when the operation is already terminal.

Durable correlation does not rely on an HTTP trace surviving a reminder or restart. Every worker, workflow, outbox, and dispatcher span carries these safe identifiers, never a prompt, token, OAuth payload, credential, or provider response:

~~~text
requestId -> operationId -> conversation grain key -> workflow ID/session -> tool ID/effect ID
~~~

The original accepted request ID remains on the operation and phase records. A fresh duplicate transport correlation never overwrites that receipt correlation.

## Step 4 — feedback and validation

The fast path is one grain transaction and a receipt. Small changes use affected-project builds, the full root test command when the shared test lane is available, Aspire doctor, and targeted resource logs/traces when an AppHost is running.

| Scenario | Regression coverage |
| --- | --- |
| Reminder handoff and no conversation→worker→conversation deadlock | InoReminderHandoffTests |
| Typed feed bootstrap ownership, scope binding, rebuild-safe signed composer | RuntimeSurfaceFeedTests |
| Bounded provider concurrency/deadline with no blind provider retry; terminal workflow failure | DigitalBrainChatPolicyTests, InoWorkflowFailureTests |
| Durable request/operation/grain/workflow/tool correlation | InoTraceCorrelationTests |
| Exact Agent Framework workflow/session reuse | AgentFrameworkWorkflowRunnerTests |
| Lease fencing, actor-bound approval, OAuth claim/resume, retention/idempotency | EncryptedDomainStateTests |
| Ordered multi-phase effect projections | EffectPhaseProjectionTests |
| Lost receipt, stale action, and authoritative UI feed rendering | Runtime Flutter feed/view tests |

## Step 5 — automation only after proof

A future reconciler may scan only durable eligible states (Accepted, RetryScheduled, expired authorization waits, and expired leases) and schedule the appropriate reminder. It must not execute tools itself, use Task.Run, or create a second projection stream.

## Consequences

A returned receipt means the accepted command, operation, and accepted phase outbox entry are already durable. Recovery works without a live UI, restart preserves durable correlation and the intended workflow reference, and external effects remain truthful even when a provider outcome cannot be confirmed.
