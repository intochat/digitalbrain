# GitHub repository and PR review verification

The approved workflow is implemented in the Microsoft module. GitHub MCP retains native
read schemas; typed PR facts serve the deterministic subscription workflow. The SDK owns
bounded HTTP transport, while Microsoft owns HMAC validation, provider identity, durable
receipts, repository projection, CI evidence and review execution.

The actual delivery path is a signed webhook, a persisted receipt, a repository outbox,
Bound subscriptions into the review inbox, an admitted C# decision, two source-bound agent
requests, retained role results and an idempotent publication into DigitalBrain chat.

Notable implementation decisions:

- `StartPullRequestReview` and publication requests acknowledge local durable admission
  quickly. Separate workers perform live GitHub reads, source binding and publication so
  inbox handlers remain available for updates and cancellation.
- A worker starts both distinct reviewer agents before awaiting either. Successful sibling
  results survive a missing-role retry. Head/base/CI changes, behavior replacement, disable
  and revocation fence late completions.
- The inbox reconciles authoritative repository snapshots and the current admitted behavior.
  Host shutdown preserves work; explicit disable/removal removes its Bound edges.
- A failed subscriber no longer prevents later subscribers receiving a broadcast. Aggregate
  failure still reaches the publisher, keeping its outbox pending for replay.
- Repository metadata, tools, source binding and destinations are checked against the configured
  principal. Installation credentials remain private kernel configuration.
- Chat retains publication tombstones independently of transcript retention. Reusing an ID
  with different content fails; retrying the same content does not append another message.

## Executed checks

| Check | Result | Local log |
|---|---|---|
| Complete simulation suite | 250 passed | `artifacts/github-simulation-all.log` |
| Final targeted GitHub, SDK webhook and chat publication suite | 61 passed, including restart, isolation and telemetry additions after the full simulation run | `artifacts/github-final-integration-tests.log` |
| Scripting suite, including the checked-in GitHub C# example | 33 passed | `artifacts/github-scripting-tests.log` |
| Aspire hosting suite | 61 passed | `artifacts/github-aspire-tests.log` |
| Substrate subscription/runtime suite | 95 passed | `artifacts/github-substrate-tests.log` |
| Flutter kit rendering, icon allowlist and input/action tests | 4 passed | `artifacts/github-flutter-test.log` |

Counts overlap; the targeted run is not an additional 61 independent tests on top of the
complete simulation suite.

The end-to-end fixture proves signed acceptance before processing, actual Bound delivery,
no model before green, both agents entering before either finishes, identical pinned evidence,
and one chat publication despite replay. Additional cases cover wrong principals, semantic
duplicates, partial subscriber failure, stale heads, authoritative red CI, selective retry,
behavior removal and disabled inboxes.

The restart fixture starts two separate simulation silos against the same journal storage.
The second does not re-admit the behavior, re-enable it, bind subscriptions or reseed candidates
and runs. It restores the same behavior/run/evidence identities and runs only code quality;
the completed architecture review remains at one invocation. This tests real host reconstruction
with an in-memory durable-store substitute, not a production storage outage.

Telemetry fixtures verify that the persisted W3C context survives an unrelated worker's
ambient trace and that baggage/payloads/secrets are not copied. Admission, durable receipt
and delayed dispatch share a trace. Review workers add PR number, head/base/CI SHA, run ID
and attempt generation to the existing Aspire/OpenTelemetry source registration.

## Live boundary

The local AppHost was rebuilt and restarted after verification. Aspire reported the kernel,
scripting worker, MCP host and Flutter resource Healthy; `GET http://localhost:5080/health`
returned HTTP 200 with `Healthy`. The non-secret resource snapshot is
`artifacts/github-apphost-health.json`. This confirms application startup, not configured GitHub access.

The local AppHost's secret store has no GitHub binding metadata or GitHub secret parameters.
No repository, installation, required-check identities or public webhook route has been supplied.
No live GitHub delivery, native MCP read or real-model PR review is claimed by these fixtures.
No GitHub comments, reviews, approvals, merges or repository changes were submitted.

Use [the setup guide](../github-pr-review.md) and [the editable C# example](../examples/github-pr-review.csx)
for the first configured repository. Explicit capacity limits and external at-least-once reads/model
work are documented there. Source and runtime changes remain in the current working tree.
