# Foundation migration — partial product

Implemented on `codex/framework-foundation`, from `563c60c120ddfd78801db1e731219b3f7b55a8d2`.
Approved scope: framework foundation and Time pilot. Leave unmerged; do not deploy this partial product.

## What is available

The runtime exposes typed grain access, typed live subscriptions and a small publishing neuron.
It owns subscription registration, renewal, bounded buffering, cancellation and cleanup. Modules own
state, validation and provider dependencies. The reusable testing project starts real Orleans and
uses the production client; it supplies observations, per-behavior readiness, bounded waits,
persistent test storage and grain-specific storage fault controls. Time owns its state and reminders
directly. The same timer behavior runs in tests and in a production C# file app.

State persists. Signals and behaviors do not replay. Save-before-publish deliberately permits lost
live facts across failure. This is a clean contract/state break, without a compatibility facade.

## Deletion audit

Physical C# lines, including blanks, excluding generated `bin`/`obj`; baseline above:

| Area | Before files / lines | After files / lines |
| --- | ---: | ---: |
| Core contracts | 41 / 755 | 3 / 17 |
| Core runtime | 26 / 2,258 | 6 / 240 |
| Time contracts | 11 / 128 | 5 / 53 |
| Time runtime | 6 / 241 | 3 / 146 |

Core runtime plus contracts: 3,013 → 257 lines (91.5% removed). No excluded-source graveyard.
Removed command receipts/envelopes, reaction/replay machinery, JSON signal payloads, graph delivery
plumbing and the separate Time alarm grain from this foundation. Legacy BehaviorRuntime remains
outside the foundation until its consumers migrate. Production reference closure has no Testing
project/package; Time has no BehaviorRuntime or old runtime dependency. Contracts contain no hosting
or test-control APIs. Central package versions used elsewhere remain intact.

## Verification on Windows, 2026-09-19

SDK: `11.0.100-rc.1.26425.128`; Orleans: `10.3.1`.

- Foundation restore: passed.
- Release foundation build: passed, zero warnings/errors.
- Release tests: 37 passed, zero skipped, 7.3 seconds (25 runtime, 12 Time).
- Release file-based app build: passed.
- Foundation whitespace verification: passed.
- CI added with the same checks and a 15-minute deadline. Linux CI has not been run locally.

Tests include refused writes and reload failure, isolated subscriptions, renewal/deactivation,
overflow/cancellation, bounded teardown, ETags/corruption, host restart, stale/repeated timer ticks,
actual Orleans reminders, and the production file app in a separate process. Tests do not claim
hard-crash durability, multi-process storage consistency or reliable event delivery.

The process test uses Orleans `TestCluster` with TCP. `InProcessTestCluster` forces an in-memory
transport in the pinned package, so the shared fast fixture cannot supply an external gateway.
The small TCP fixture is local to the Time process test and uses the real module and client. It
waits for production `SubscriptionReady`, triggers once, checks output and exit, and kills the child
on failure. There is no invented `BrainTestHost.Connection` surface.

## Full-product boundary and next work

`DigitalBrain.slnx` remains the product inventory. Its Release build was run once and failed with
463 errors in unmigrated consumers (zero warnings). Direct failing projects observed: AI.Contracts,
Coding.Contracts, Excel.Contracts, Google.Contracts, Memory.Contracts, Microsoft.Contracts,
Salesforce.Contracts, DigitalBrain.Aspire, DigitalBrain.Aspire.Hosting and DigitalBrain.Behavior.
Many downstream projects cannot compile until those dependencies are migrated; the list is not a
claim that every downstream project was independently tested.

Also still outside the foundation: MCP, ClickHouse, Supabase, Flutter, IntoChat, integration providers
and deployment composition. Existing full-product CI remains unchanged; the new job explicitly says
“Foundation and Time only — partial migration”. The original application does not build on this branch.

Next design scope: Memory with typed operations and module-owned state. Then separate AI from MCP
and remove the old behavior runtime dependency. Migrate other modules one at a time, applying the
same test pattern: real module, provider seams, subscribe before trigger, one action, typed assertions,
state-failure recovery, then delete obsolete contracts and implementation together.

See [test harness usage](../../src/Testing/README.md),
[approved design](../superpowers/specs/2026-09-19-framework-simplification-design.md) and
[implementation plan](../superpowers/plans/2026-09-19-framework-foundation-and-time.md).


## Final review

An independent review of the complete branch found three Important lifetime gaps and no Critical or
Minor findings. Each was reproduced by failing tests and fixed in one pass:

1. Late initial/renewed observer registration: observe the pending operation, remove late membership
   with bounded cleanup, and release the local observer reference promptly.
2. Canceled startup: pass the cancellation token into Orleans deployment and await its completion
   before releasing cluster/storage ownership.
3. Behavior cancellation callbacks: apply the five-second deadline to callbacks and task completion
   together; observe eventual errors and dispose the token source after callbacks finish.

The full Release suite is green after these fixes. No second review was dispatched. A storage test
also now awaits its held-read release instead of asserting immediate RPC completion during client
shutdown. This removes a timing-dependent assertion without weakening the held-read guarantee.

## Execution decisions

- CodeGraph had no index in the isolated checkout. Original repositories were investigated with
  CodeGraph; implementation used source reads. Cost: graph navigation is unavailable in this worktree.
- Storage-failure tests assert OrleansException with the original IOException cause, matching the
  pinned provider behavior. Cost: assertions must follow any future Orleans exception-contract change.
- UseReminders selects memory/file reminder providers under the test host's ownership lease. Module
  registration still owns its requirements. Cost: one explicit fixture option; this does not model
  distributed reminder-storage consistency.
- The process smoke uses a module-local TCP TestCluster, as explained above. Cost: maintaining that
  small transport fixture separately from fast in-process tests. The proposed Connection surface was
  deferred until this first consumer, then omitted because it would have exposed unusable endpoints.
- Keep the branch and native worktree unmerged, per the approved plan. Cost: integration is a later
  decision after the remaining consumers migrate.
- The review set aside full-product compilation/downstream migrations, accepted here because the
  foundation is an explicitly partial stage. Cost: the original solution remains broken on this branch.
- The review set aside old-state compatibility, accepted under the approved fresh-state break.
  Cost: deployed state and old callers cannot be carried forward unchanged.
- The review set aside reliable signal replay, hard-crash durability and distributed file-store
  consistency, accepted as excluded guarantees. Cost: live facts may be lost; test storage does not
  certify a production provider's durability or concurrency semantics.
- The review set aside deployment readiness, accepted because this partial branch stays undeployed.
  Cost: deployment composition still needs migration and validation.

Deferred minor findings: none.
