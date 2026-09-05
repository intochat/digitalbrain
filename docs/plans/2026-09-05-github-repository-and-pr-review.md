# GitHub repository neuron and personal PR review behavior

**Status: implemented and fixture-verified.** Live GitHub setup still needs a selected repository, installation, required CI identities and reachable webhook route. Automatic GitHub review publication remains outside this scope. The original design sketches below are superseded by the [setup guide](../github-pr-review.md), [working C# example](../examples/github-pr-review.csx) and [verification record](../reviews/2026-09-05-github-pr-review-verification.md).

## Intended experience

A GitHub pull request opens. DigitalBrain receives and durably accepts its webhook, routes it to the configured repository neuron, and delivers `PullRequestOpened` along an actual Bound subscription. Your admitted C# behavior evaluates CI for that PR revision. Only when the required CI is green does it start two independent reviewer neurons:

1. Architecture review: module boundaries, responsibilities, dependencies, neuron/synapse semantics, and opportunities to simplify the design.
2. Code-quality review: correctness, concurrency, cancellation, error handling, maintainability, and meaningful test coverage.

Both inspect the same immutable revision and evidence. Their results are retained separately and delivered together into the configured DigitalBrain conversation. The graph shows the repository, the subscribed review inbox, the review worker and both real reviewer instances.

Default pending the user's output preference: show results in DigitalBrain; publishing to GitHub is a later explicit action. No automatic PR comments, approvals, change requests or merges in the initial implementation.

## Decisions

| Concern | Recommended decision |
|---|---|
| Repository abstraction | `DigitalBrain.Microsoft.GitHub.IRepository : IAgent`; `Repository : Agent` |
| Location | `GitHub` folders inside the existing Microsoft contracts, implementation and hosting projects |
| Incoming facts | Typed `PullRequestOpened`, `PullRequestUpdated`, `PullRequestClosed`, `PullRequestChecksChanged` |
| MCP | Discover native read tools; retain their schemas and apply repository/operation policy |
| CI decision | Deterministic C# over a typed PR/check snapshot; never parse an LLM answer as permission to start |
| Script trigger | A compiled, module-local `PullRequestReview` neuron receives the subscription and persists candidates; the existing out-of-process script observes/reconciles that state |
| Parallelism | A small source-bound review worker starts two distinct reviewer neurons using inherited `AgentRequest` / `AgentReply` |
| Durability | Provider webhook receipts, repository projection/outbox, and a review inbox/run ledger on existing durable grain storage |
| Authentication | An installed GitHub App bound to the configured repository and DigitalBrain principal, supporting unattended access |
| Initial CI policy | Explicit required check identities in the custom C# behavior; no empty-list success. Importing branch/ruleset requirements can follow separately |

The typed PR facts and snapshots describe repository state and automation decisions. They are not one invented signal/schema for every GitHub MCP operation. Natural-language repository requests and both reviewers continue to use the existing generic agent contract.

## Why a small amount of foundation work is necessary

The current [HTTP surface](../../src/Kernel/DigitalBrain.Sdk/Http/IHttpSurface.cs) already anticipates webhooks and is mapped before the normal authentication gate. We can extend that seam without adding a second HTTP router.

The current [behavior worker](../../src/Kernel/DigitalBrain.Scripting/Startup/BehaviorScriptWorker.cs) runs admitted C# outside the silo and restores the admitting principal. Scripts have `Brain` and `CancellationToken`; they are not neurons and cannot themselves receive a Bound delivery. Journals are bounded observations, not durable work queues.

Two `Brain.Get<T>().RequestAsync(...)` calls from a script share the serialized owner root. Wrapping them in `Task.WhenAll` would not establish independent parallel model turns. The new worker must use the existing activation-local [source-bound request path](../../src/Kernel/DigitalBrain/Neuron/Neuron.cs), as the chat worker does for specialist continuation.

`BroadcastAsync` awaits its recipients. Subscription handlers must persist and return quickly. Neither the webhook request nor `HandleAsync(PullRequestOpened)` may wait for CI or model completion. Existing `Execution` contracts are chat-specific; this plan does not turn them into a generic workflow engine. Existing timer journals are not assumed to broadcast subscriber notifications.

Also split Microsoft module setup into independent Aspire and GitHub registration paths: its current Aspire early return must not disable GitHub when Aspire is unconfigured.

## 1. Microsoft/GitHub boundary

Proposed locations and principal types:

```text
src/Modules/Microsoft/
  Contracts/GitHub/
    IRepository.cs
    PullRequestOpened.cs
    PullRequestUpdated.cs
    PullRequestClosed.cs
    PullRequestChecksChanged.cs
    PullRequestSnapshot.cs
    Reviews/
      IPullRequestReview.cs
      ReadReviewCandidates.cs
      StartPullRequestReview.cs
      CancelPullRequestReview.cs
      ReviewResult.cs
      IArchitectureReviewer.cs
      ICodeQualityReviewer.cs
  Microsoft/GitHub/
    Repository.cs
    RepositoryBinding.cs
    GitHubConnection.cs
    GitHubWebhookHandler.cs
    GitHubWebhookInbox.cs
    GitHubWebhookDispatcher.cs
    PullRequestSnapshotReader.cs
    Reviews/
      PullRequestReview.cs
      PullRequestReviewWorker.cs
      ArchitectureReviewer.cs
      CodeQualityReviewer.cs
      ReviewEvidence.cs
  Hosting/GitHub/
    GitHubHostingExtensions.cs

src/Kernel/DigitalBrain.Sdk/Webhooks/
  WebhookSurface.cs
  WebhookDefinition.cs
  IWebhookHandler.cs
  WebhookAcceptance.cs
```

These are proposed responsibilities; implementation should combine small private helpers rather than create empty interface/service layers. Preserve the existing Microsoft assembly boundaries.

Use grain type `github-repository` and a principal-partitioned instance containing the configured binding and numeric GitHub repository ID. Names such as `owner/repo` remain presentation/API coordinates, not stable identity. Include GitHub host and installation in the binding so different connections cannot collide. Rename, transfer and installation removal must invalidate or reauthorize the binding.

`Repository` owns its PR projection, incoming delivery deduplication and outgoing domain notifications. It also exposes native MCP reads through `IAgent`. Ino can receive an `ask_repository` delegation bound by application code. A model cannot choose an arbitrary repository, owner or installation.

Register module-owned `NeuronPresentation` metadata and the allowlisted `github` asset through the shared UI kit. Add instance labels so reviewers can be distinguished as Architecture review and Code quality review without provider-specific graph widgets.

## 2. Reusable SDK webhook entry point

Build a generic `WebhookSurface` using `IHttpSurface`: exact POST route, bounded raw body, content-type checks, pluggable verification, durable acceptance result, cancellation/deadlines and safe telemetry. Keep GitHub header names, payload normalization and repository routing in Microsoft/GitHub. A reusable HMAC utility may live in the SDK; do not build a provider framework before there is another consumer.

For GitHub, verify `X-Hub-Signature-256` against the exact request bytes before interpreting JSON, using constant-time comparison. An opaque configured endpoint ID selects the secret/binding; authenticated payload repository/installation IDs must match it. Neither the webhook sender nor supplied owner/principal fields grant DigitalBrain access. [GitHub signature validation](https://docs.github.com/en/webhooks/using-webhooks/validating-webhook-deliveries).

The HTTP request must not enter a potentially busy repository agent or the serialized owner root to run its business logic. It calls a short, internal, provider-owned receipt admission endpoint on a separate durable ingress grain.

Acceptance sequence:

1. Authenticate and normalize the supported delivery within configured limits.
2. Atomically store delivery ID, body digest, trusted binding, normalized event and pending-dispatch state. A repeated ID with different content is rejected; exact redelivery is idempotent.
3. Return 2xx only after persistence. Storage failure or capacity exhaustion returns a failure. Valid irrelevant events/pings are acknowledged without triggering reviews.
4. A recoverable dispatcher delivers the stored fact to `IRepository` under the mapped automation principal.
5. Repository atomically records deduplication, current domain state and pending notifications. A persisted notification dispatcher broadcasts to its current eligible synapses; receivers deduplicate admissions.
6. Mark each stage complete after acknowledgement. Crashes between stages replay safely; subscriber failures cannot erase accepted work.

GitHub requires a prompt response and does not automatically retry failed deliveries. Target acknowledgement comfortably inside its 10-second limit. Include startup and bounded periodic reconciliation of eligible open PRs/check state to recover missed webhook notifications. Document manual GitHub redelivery for failed deliveries; do not claim that an in-memory channel or a journal watch guarantees recovery. [Webhook best practices](https://docs.github.com/en/webhooks/using-webhooks/best-practices-for-using-webhooks), [failed deliveries](https://docs.github.com/en/webhooks/using-webhooks/handling-failed-webhook-deliveries).

Keep receipt/outbox retention and capacity explicit. Persist compact normalized fields rather than arbitrary full payloads; large evidence belongs in bounded artifacts referenced by hash. A fresh webhook delivery ID cannot bypass semantic PR/review deduplication.

## 3. Repository facts and CI correctness

Initial webhook inputs:

- `pull_request`: opened, reopened, synchronize, ready_for_review, converted_to_draft, closed, and base-target edits relevant to the revision.
- `check_run`, `check_suite`, and commit `status`: wake authoritative CI reconciliation. They do not individually declare an entire PR green.
- Installation/repository access changes: disable the affected binding and cancel or block its work.

Normalize these into the small domain signal set above. `Repository` really broadcasts these facts. Merely recording them with `RecordOutgoingAsync` is insufficient for the promised subscription behavior.

A PR snapshot identifies repository, PR number, lifecycle/draft state, current head SHA, base SHA, effective CI commit, check/run attempt identities, source GitHub App identities, observation time and snapshot version. Paginate required evidence completely or report it as incomplete.

Use a narrow deterministic snapshot reader. Prefer native MCP read capabilities where their structured results provide complete evidence; use a GitHub API adapter for any missing authoritative check/status fields. This adapter builds domain state for C#, while agents keep native discovered tool schemas. There is no AI round-trip in the CI predicate.

Recommended first script policy:

- Wait until the PR is open and ready for review.
- Require a nonempty, configured set of check identities; names can be paired with their expected GitHub App to avoid accepting an unrelated producer.
- Every required check/status must exist, be complete and satisfy the script's explicit success policy. Pending, missing, failure, cancellation, timeout, incomplete results or inaccessible evidence never count as green.
- Default to actual success; treat neutral/skipped outcomes only through an explicit custom policy. GitHub's merge rules can accept outcomes that are not equivalent to successful test execution.
- Distinguish head and test-merge commits: use the applicable CI commit while binding it to the current PR head/base pair. Never use green evidence from an older head or merge revision. [GitHub check semantics](https://docs.github.com/en/pull-requests/reference/status-checks), [head versus test-merge checks](https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks).
- Reconcile on check notifications and with bounded backoff while waiting. A failed run may later be rerun successfully; no reviewer starts while it is red.
- A new commit/base change invalidates the previous candidate and any current-result claim. Closed/merged PRs stop waiting. Drafts wait for ready-for-review by default.

Keep CI timeout, required checks, accepted conclusions, prompts and retry choices visible in the user's C# source. Do not hardcode the user's review policy in `Repository`.

## 4. A durable subscription for the C# behavior

Add one module-local `PullRequestReview : Neuron` per repository binding and named behavior. It is a short-running subscriber and durable review ledger, not an LLM. It implements the exact PR/check handlers, plus bounded read/control contracts.

The admitted script binds it to `Repository` with the existing typed `SubscribeToAsync` calls. Its incoming handlers upsert candidates and return. The script runs a watch/reconcile loop using the existing `Brain` global: journal events reduce latency; candidate snapshots and run records are authoritative after restart or journal compaction.

Add only small read-only behavior execution metadata to the script context if needed: name, admitted revision and source hash. This enables stable policy/run identity without a service locator or credentials in the script.

`StartPullRequestReview` is a domain admission command. It carries the expected PR/snapshot revision, script revision, CI-evidence revision, two generic `AgentRequest` values and the fixed result destination. It atomically returns an existing admission or persists one new run. It cannot override repository/principal ownership or bypass revision/freshness checks.

Logical uniqueness is at least `(binding, repository ID, PR number, head/base revision, behavior revision)`. Persist role results, attempts and publication state. Allow bounded retry of an interrupted/missing role, retaining a completed sibling. Model computation can repeat after a crash; committed run/result identity must be idempotent. Do not promise exactly-once external model execution.

Enablement stores an observation boundary so installing a behavior does not silently review every historical open PR. Explicit removal/disable stops new admission and unsubscribes; host restart keeps the admitted behavior recoverable. Mark the inbox disabled as well as removing its Bound edges, because unsubscribe itself is not a permanent routing prohibition. New in-flight late deliveries are ignored while disabled. An explicit later re-enable is a new authorized activation.

## 5. Two real review subagents

Persist admission before launching `PullRequestReviewWorker`. Reuse the proven chat admission/worker/cancellation pattern, keeping its tracking and mutations on the owning scheduler. Do not run owner C# inside a neuron or introduce detached `Task.Run` state mutation.

The worker first verifies current PR/CI revisions and captures immutable evidence: pinned head/base, complete bounded diff/file list, content hashes and permitted additional read scope. If evidence is incomplete, report that limitation and fail the complete-review gate rather than silently dropping files.

Only after the CI gate does it activate fresh principal/run-scoped identities for:

- `IArchitectureReviewer : IAgent` / `ArchitectureReviewer : Agent`.
- `ICodeQualityReviewer : IAgent` / `CodeQualityReviewer : Agent`.

Both receive the script's custom instructions through `AgentRequest`. A shared reviewer tool source may provide read-only, repository- and revision-scoped native reads. Each has its own model context, tools, journal and result; neither delegates back through the single Ino or repository activation. Start both source-bound requests before awaiting either, using `Task.WhenAll` on the worker's own request path.

Do not clone/edit the user's working directory or execute PR-supplied scripts. If local CodeGraph inspection is enabled later in the implementation, use an isolated, pinned repository snapshot and prove its index matches that revision. Repository files, PR text and tool results are evidence, not instructions that can broaden the reviewers' permissions.

On a new head, closure, removed behavior or revoked access, request cooperative cancellation and invalidate the old attempt generation. Late results cannot become the current review. Preserve role failures/timeouts distinctly; do not present a partial review as both completed.

## Illustrative C# shape

This is the intended authoring experience, not code that compiles against today's contracts. `ObserveCandidates` would combine snapshot reconciliation, journal wakeups and cancellation; `RequiredChecksSucceeded` is the user's deterministic policy.

```csharp
var repository = Brain.Get<IRepository>(repositoryInstance);
var reviews = Brain.Get<IPullRequestReview>(reviewBehaviorInstance);

await reviews.SubscribeToAsync<IPullRequestReview, IRepository, PullRequestOpened>(
    repository.Id, CancellationToken);
// Bind the head/check/closed facts in the same typed way.

await foreach (var candidate in ObserveCandidates(reviews, CancellationToken))
{
    if (!candidate.IsOpen || candidate.IsDraft)
        continue;

    if (!RequiredChecksSucceeded(candidate, requiredChecks))
        continue; // Keep waiting; notifications and reconciliation revisit it.

    await reviews.RequestAsync(new StartPullRequestReview(
        candidate.Revision,
        candidate.CiRevision,
        Behavior.Revision,
        Architecture: new AgentRequest("""
            Review the pinned changes for architecture. Focus on module boundaries,
            source-owned subscriptions, cancellation and unnecessary abstractions.
            Report concrete findings with file/line evidence and simplifications.
            """),
        CodeQuality: new AgentRequest("""
            Review the same pinned changes for correctness and code quality.
            Focus on bugs, concurrency, error handling and meaningful tests.
            Report concrete findings with file/line evidence.
            """)), CancellationToken);
}
```

The script controls when and what to review. The durable inbox handles admission/recovery, and the internal worker supplies true parallel agent execution. Result formatting/filtering can also remain user C#: the first example will collect both persisted role results and publish one combined, SHA-labelled result through an idempotent application publication path.

## 6. GitHub connection and hosting

Use a GitHub App installed only on the configured repositories. Read access is needed for PRs, contents, checks and commit statuses; add Actions read only if workflow evidence requires it. Initial review output stays in DigitalBrain, so no PR write/merge scope is necessary.

Bind App/installation/repository access to the admitting DigitalBrain principal and validate it at ingress, MCP preparation/invocation and review admission. Unattended token refresh must not depend on a browser login prompt in an agent turn. [GitHub installation authentication](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/authenticating-as-a-github-app-installation).

Prefer the existing shared HTTP MCP transport with a module-owned installation token provider, subject to a first-phase compatibility test against the selected official GitHub MCP release/endpoint. The official server also documents local STDIO GitHub App authentication; use it as the supported alternative if the selected hosted configuration is unavailable, while retaining the same tool-source boundary. Do not assume tokens hide unpermitted tools: enforce admission and repository/method scope locally. [Official server](https://github.com/github/github-mcp-server), [App authentication](https://github.com/github/github-mcp-server/blob/main/docs/github-app-auth.md), [scope filtering](https://github.com/github/github-mcp-server/blob/main/docs/scope-filtering.md).

Extend Microsoft hosting with proposed `.WithGitHubRepository(...)` configuration and private App/webhook secret parameters. A local desktop alone is not an externally reachable webhook endpoint. Before live rollout, configure an HTTPS ingress or development relay forwarding only the exact webhook route; do not expose the open development kernel/chat API. Keep webhook receipt and processing telemetry correlated without exporting secrets or raw payloads.

## 7. Verification and sequencing

1. **Contracts and connection proof:** independent Microsoft registration; fixed binding and App authentication; native reads; CI snapshot completeness; stable identities and icons.
2. **SDK webhook + durable ingress:** signature/method/body tests; duplicate/conflicting deliveries; bounded acknowledgement while repository/owner root is busy; persisted admission before 2xx.
3. **Repository signals:** typed normalization, out-of-order updates, semantic deduplication, projection/outbox recovery, real Bound delivery and unsubscribe behavior.
4. **Review inbox + script:** current definitions survive restart; pending work survives journal reset; explicit disable/removal stops admissions; CI predicate remains custom C#.
5. **Parallel reviewers:** no model before green; barrier tests prove both reviewers entered before either can finish; same pinned evidence; bounded cancellation, partial results, stale-generation rejection and crash recovery.
6. **End-to-end:** signed PR-open fixture → waiting CI → failed/missing/partial checks do not start → all required checks succeed → two reviewers → persisted combined result in chat. Include checks arriving before PR-open, forks with empty webhook PR association, head/base changes, CI reruns and replay at every acknowledgement boundary. GitHub documents that check webhook associations can be absent for fork pushes; recover using repository/commit lookup rather than assuming the array is populated. [Webhook payloads](https://docs.github.com/en/webhooks/webhook-events-and-payloads).
7. **Live configured repository:** receive a real delivery, verify the pinned CI decision and independent agent traces, then inspect graph and chat. Keep fixture results separate from actual GitHub verification. No GitHub write until the result-publication policy is explicitly chosen.

Graph/telemetry acceptance: repository and two reviewer identities, actual Bound and Learned edges, PR/commit/run correlation, CI wait reason, role-specific completion/failure, and existing sensitive-content opt-in behavior. Test publication replay so a worker restart does not duplicate the final chat result.

## Scope and inputs for rollout

Approval covers implementation of this vertical slice, the reusable SDK webhook transport seam, the narrowly scoped durable GitHub/review state, the custom C# example, UI metadata and verification. It preserves kernel routing semantics and native MCP tool schemas. It does not add a general DAG designer, arbitrary dynamic neuron compilation, merge-queue automation, automatic code changes or a new messaging product.

Live setup needs the target repository, GitHub App installation/configuration, a public webhook route and the required CI identities. These do not block implementation and signed-fixture testing. Review publication defaults to DigitalBrain chat unless the user chooses otherwise. Keep prior uncommitted refactoring intact; no commit or service restart is needed to approve this plan.
