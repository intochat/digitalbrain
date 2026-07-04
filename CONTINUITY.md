# CONTINUITY — NeuroOS best-of-breed consolidation

## 2026-07-04 — MULTIUSER Feed Isolation: ClientId Routing (fail-open shim fully reverted)

Merged to master (brainstorm → spec → 9-task subagent-driven plan, design
`docs/superpowers/specs/2026-07-04-multiuser-feed-isolation-design.md`, plan
`docs/superpowers/plans/2026-07-04-multiuser-feed-isolation-clientid-routing.md`). Closes out the live
production bug mitigated same-day by the fail-open shim in commit `50ed11e` (see the entry below) — this
was the real fix that shim was standing in for.

**Root design decision:** collapsed the client-facing identity to one concept, `clientId` — a token the
client already half-had (it was hardcoded to the literal `"flutter"` everywhere, wired through the login
form's `submitAction` payload but never populated per-connection). `sessionId` becomes purely internal to
`UserSessionNeuron`, resolved on demand via a new `GetSessionByClientIdAsync(clientId)`; the client never
sees, stores, or round-trips a real session id again. This reached its final shape iteratively in
brainstorming — two earlier, more complex proposals (a session-rebinding control-plane message; a separate
`Address()` method alongside `Broadcast()`) were rejected in favor of deleting the hand-rolled subscriber
dictionary entirely: `HomeFeedBus` now publishes every `RfwCard` onto an Orleans stream keyed by
`card.ClientId` (or the existing unaddressed key when null), and each `WatchHomeFeed` gRPC call subscribes
directly to its own personal stream plus the shared system stream — no per-silo relay
(`HomeFeedStreamSubscriber`, deleted), no in-process fan-out loop.

**What shipped, task by task:** proto + `HomeFeedBus` Orleans-native rewrite; `GetSessionByClientIdAsync`;
threading the real connecting `clientId` through `UserSessionNeuron`'s post-login broadcasts (shell,
marketplace, task manager); `GatewayService.Send`'s `InstallFromMarketplace`/`ConfigurationProvided`/
`LogoutRequest`/`InoRequest`/`SalesforceSignals.AuthRequested` branches all resolve real identity from
`clientId` instead of a raw session id the client never has; all four Flutter `WatchHomeFeed` call sites
(`chat_screen.dart`, `forui_app_shell.dart`, `living_canvas_screen.dart`, `experience_host_screen.dart`) now
send a stable per-connection `clientId`. **All three fail-open shims from `50ed11e` are now fully
reverted**: `HomeFeedBus` addressing is unconditional (moot anyway — routing is now per-stream, not a
runtime check), `InoRequest`'s `clientId` resolves to a real session, and `SalesforceSignals.AuthRequested`
rejects with `Unauthenticated` instead of falling back to `"anonymous"`.

**Fixed beyond the original mitigation:** `InoNeuron.ResolveUserIdAsync` was calling `GetSessionAsync`
against the chat screen's own per-widget correlation token — never a real session id — so
Salesforce/Gmail identity resolution triggered from chat always silently fell through to the anonymous
user, even under the fail-open shim. This is now `GetSessionByClientIdAsync`, resolving the real logged-in
user.

Every task in the 9-task plan hit at least one caller the plan's own file list missed (the same class of
gap the S2/S3 plan hit with `ConfigFormSteps.cs`/`TelegramReactiveLoopSteps.cs`) — each found via a full
solution build/test run, not a targeted grep, and fixed mechanically once found. One task (Task 5) replaced
its own brief's regression test after discovering the test targeted a grain the chat-driven Salesforce flow
never actually reaches; the replacement was independently verified as a strengthening, not a weakening, by
tracing the full production call path. Full solution `dotnet test Brain.slnx`: 0 failures throughout (410
tests, 6 pre-existing skips, unchanged baseline).

**Live-verified**, not just unit-tested — the same gap that let the original bug reach production. Ran the
AppHost via `aspire run`, drove the real kernel gRPC endpoint directly (the Flutter client here runs as a
native Windows desktop app with no available GUI-automation surface, so this project's `verify` skill
targets the gRPC boundary instead): two real logged-in users on distinct `clientId` connections never saw
each other's shell/marketplace/task-manager cards; a chat-driven Salesforce credential-form request reached
only the requesting connection; logout correctly re-showed the login form on the same connection; a direct
Salesforce button-click request with no session was rejected `Unauthenticated`. No exceptions in kernel logs
beyond expected client-disconnect noise and one pre-existing, unrelated `SystemStatusNeuron` self-awareness
warning.

## 2026-07-04 — MULTIUSER Stage S2 (Identity Spine) + Stage S3 (Salesforce Per-User)

Merged to master (subagent-driven, 9 tasks, plan
`docs/superpowers/plans/2026-07-04-multiuser-s2-s3-identity-and-salesforce-per-user.md`). Builds on Stage
S1 (below): turned `UserSessionNeuron`'s existing `UserId`/`sessionId` identity into something the rest of
the kernel actually uses. S2 added the `NeuronScope`/`PackConfigScopes` identity spine in
`DigitalBrain.Core`, addressed `RfwCard`s to a `SessionId` so `HomeFeedBus` only fans session-addressed
cards to their matching subscriber, and made the gateway resolve `sessionId → UserSessionState` once
server-side instead of trusting client-supplied `buyerId`/`scope`/`sessionId` payload fields. S3 moved
Salesforce auth/CRM grains from the `"salesforce-auth-main"` global singleton to per-`{userId}` grains,
split pack-config storage into a shared `PackConfigScopes.App` ("default") scope for connected-app config
and per-user `PackConfigScopes.ForUser(userId)` ("user:{userId}") scopes for tokens, and routed the OAuth
callback (a cold, unauthenticated HTTP GET) to the right per-user grain via a plaintext
`{userId}:{nonce}` state prefix (the owner-approved minimal alternative to D-MU2's encrypted state,
deferred to S4).

Two real plan gaps were found and fixed during implementation, not just asserted: (1) the plan's
arbitrary-buyer-scope anti-pattern fix for `ConfigFormSteps.cs` was missed for
`DigitalBrain.Tests/Steps/TelegramReactiveLoopSteps.cs`, which has the identical pattern — caught by an
implementer via a full-suite regression run and git-stash bisection, then fixed identically in an
authorized scope-widened follow-up commit; (2) the final acceptance test's
`Two_Users_Interleaved_OAuth_Flows_Do_Not_Cross_Contaminate` had vacuous token-equality assertions
(`aliceTokens`/`bobTokens == "fake-access-token"`) that would pass identically even if both users'
writes collapsed into one shared scope, since `FakeSalesforceTokenHandler` always returns the same
constant token string — fixed with a genuine presence/absence check between Alice's and Bob's
`CompleteOAuthAsync` calls instead of relying on value equality.

A final whole-branch review (most capable model) found one Important finding and several trivial Minor
findings, all closed out in one cleanup pass. Important: `SalesforceApiClientFactory`/
`SalesforceClientFactory.GetMergedScopedValuesAsync` merge the shared App scope as the base for every
user's credential view, so a pre-existing installation with a completed OAuth flow under the old
singleton model would have a leftover token in that shared scope leak into every other user's merged
view until they complete their own OAuth — explicitly confirmed not applicable to this installation (a
dev/prototype with no prior real Salesforce OAuth completions) and documented as a greenfield-only
assumption in the plan's new "Known Limitations" section rather than building migration/purge code. Also
in this pass: an explicit early-reject in `SalesforceAuthNeuron.CompleteOAuthAsync` for a callback routed
to a grain with zero pending OAuth state (proven via TDD to matter — the old fallthrough would actually
*succeed* the token exchange against the test double's fake HTTP handler, since it doesn't validate the
PKCE `code_verifier`), plus dead-code/comment cleanup (`SalesforceClientFactory.CreateApiClientAsync`,
a redundant `UserSessionNeuron.IsValidUsernameCharset` guard, and stale comments in `HomeFeedBus.cs`/
`GatewayService.cs`). Full solution `dotnet test` green throughout (0 failures).

**Live bug found and mitigated same day (commit `50ed11e`):** the user reported "get my salesforce
profile" hung on a spinner with no login button in the real running app immediately after this plan
shipped. Root cause (via systematic debugging, not guessing): the Flutter client opens one long-lived
`WatchHomeFeed` stream at startup, before login, and never learns/forwards the real login `sessionId` —
so S2.3/S2.4's session-addressing silently dropped every post-login card (shell, chat replies, credential
forms) for every real client connection. Separately, `chat_screen.dart` reuses the `"sessionId"` JSON key
for a client-generated per-widget UI correlation token unrelated to authentication, which S2.5's
`InoRequest` resolution broke by trying to treat it as a real session. Both are genuine conflations this
plan's own design didn't anticipate, not implementation bugs — the plan's "sessionId" model assumed a
real auth session everywhere that name appeared, but the pre-existing chat UI had already overloaded it
as a routing token. Mitigated with a fail-open compatibility shim rather than a full redesign (user's
explicit choice, to unblock immediately): `HomeFeedBus.FanLocal` only filters once a subscriber has
registered a real session (none do yet, so it fails open exactly like pre-S2 behavior); `InoRequest`'s
sessionId passes through unchanged instead of being resolved; Salesforce `AuthRequested` falls back to
"anonymous" instead of hard-rejecting. Restores today's single-user functionality; defeats S2's P6a
per-session isolation goal until a real fix lands (documented in the plan's "Known Limitations"). Applied
live via `mcp__aspire__execute_resource_command` `rebuild` on all 3 kernel replicas — this is the
sanctioned way to push a source change into a running Aspire environment without killing processes the
user is actively testing against. Full solution tests green throughout (340/340 minus 6 pre-existing
skips in `DigitalBrain.Tests`, 23/23 in `DigitalBrain.Salesforce.Tests`).

**Follow-up not yet scoped:** the real fix needs either Flutter-side work (capture the real session after
login, reconnect `WatchHomeFeed` with it) or a server-side addressing signal that doesn't collide with
the pre-existing chat-correlation-token convention. Until then, P6a (per-session feed isolation) is not
actually enforced in the running app.

## 2026-07-04 — MULTIUSER Stage S1: Salesforce OAuth callback grain-routing

Merged to master (subagent-driven, plan `docs/superpowers/plans/2026-07-04-salesforce-oauth-callback-grain-routing.md`).
Fixes P1 from `docs/CONTINUATION-MULTIUSER-IDENTITY.md`: the `/salesforce-callback` minimal-API endpoint
in `Program.cs` used to read/write `IPackConfigStore` and exchange the authorization code directly,
bypassing `SalesforceAuthNeuron` entirely. With 3 Kernel replicas behind Aspire's proxy (confirmed live —
this AppHost runs `kernel-hfzjyduu`/`kernel-sqpftzde`/`kernel-uaxsruac`), a callback landing on a
different replica than the one that started the flow hit an empty, replica-local pending-state store
and failed 100% reproducibly.

Shipped in 4 tasks: (1) an optional `HttpMessageHandler` seam on
`SalesforceClientFactory.ExchangeAuthorizationCodeAsync` + `FakeSalesforceTokenHandler` test double, so
the token exchange is fakeable without real network I/O or Windows HTTP.sys ACL friction; (2)
`ISalesforceAuthNeuron.CompleteOAuthAsync` — the grain now owns pending-state validation and the token
exchange, with `SalesforceOAuthCallback`/`SalesforceOAuthCallbackResult` added to
`DigitalBrain.Core/Synapse.cs` (`[GenerateSerializer]` + explicit sequential `[Id(n)]`, matching the
non-negotiable convention for new cross-grain-boundary types); (3) `Program.cs`'s endpoint reduced to
pure parse-and-route (82 lines → 18), with the old direct-store-IO path deleted, not commented out; (4)
`SalesforceOAuthCrossSiloTests` — starts the OAuth flow via one Orleans TestingHost silo's `IGrainFactory`
and completes it via a different silo's, asserting `GetSiloIdentityAsync()` is identical both times (the
property that makes the fix real, independently traced by the task reviewer rather than taken on faith).

Grain keys stay `"salesforce-auth-main"` (no per-user routing yet — that's MULTIUSER S3). No journaled
OAuth lifecycle synapses added (deferred to S4's shared `OAuthFlowNeuron`, per invariant I3: tokens/PKCE
material never enter a journal). Full solution: `dotnet build Brain.slnx` clean, `dotnet test Brain.slnx`
0 failures across every project. Smoke-tested against the real 3-replica AppHost via `aspire run`:
`GET /salesforce-callback?error=access_denied&error_description=smoke-test` returned the expected HTTP
400 "Salesforce login failed" page with no exceptions logged.

Not done: MULTIUSER S2-S5 (identity spine, per-user keying, Google on the shared flow, chat layer) and
the whole-repo cleanup waves in `docs/CONTINUATION-CLEANUP-SIMPLIFICATION.md` — per that doc's own D-CL6
sequencing (S1 → cleanup D1-D4 → S2-S5), those are separate follow-on plans.

## 2026-07-03 — X post → Bitcoin price → Telegram demo

Merged to master (`spec/x-bitcoin-telegram-demo`, fast-forward). Proves the cross-channel capstone demo
first flagged at the end of the prior session's "Next" note (X/Twitter integration + Bitcoin price +
Telegram) — reusing existing infrastructure end-to-end rather than building new closed-loop machinery.
Reached here after an initial, larger "self-improvement loop" spec (system edits its own source tree,
open-ended discovery, fully autonomous merge+redeploy) was scoped down on user feedback ("too
complicated... let's oversimplify... show MVP to my team asap"); that spec/branch (`spec/self-
improvement-loop`, 1 commit) is parked, not merged, in case that direction is revisited later.

Shipped: `IMarketDataApiClient`/`CoinGeckoApiClient` (real CoinGecko HTTP wrapper, mirrors
`DigitalBrain.Google`'s `I*ApiClient` pattern), `MarketDataNeuron` (Kernel-side grain reacting to
`Signal("CheckBitcoinPrice")`, mirrors `LlmResponderNeuron`), a `simulate_x_post` MCP tool (thin wrapper
over `IIngressNeuron.IngestAsync`), a hand-authored `XBitcoinTelegramDemoNeuron` pack seeded in
`MarketplaceSeeds.cs` alongside `TelegramResponderPackCode`/`KeywordWatcherPackCode`, and an end-to-end
Reqnroll scenario (`XBitcoinTelegramDemo.feature`) proving the full real chain — MCP tool → ingress
broadcast → embodied pack → `MarketDataNeuron` (only the HTTP call faked) → embodied pack → Telegram
egress bus — over a real Orleans `TestCluster`, same style as `TelegramExperience.feature`.

Two real cross-task findings surfaced only at review, both fixed:
- **Task 3** needed `IIngressNeuron`'s interface (not its implementation) relocated from
  `DigitalBrain.Kernel.Gateway` into `DigitalBrain.Core`, and `NeuronTestBase.Grain<T>()` widened
  `protected`→`public` — both were compile-hard requirements of extracting a shared `TestGrainFactory`
  test double for `DigitalBrain.Mcp.Tools` (which references only `Core`), independently verified via
  `.csproj`/project-reference inspection by the task reviewer, not just taken on the implementer's word.
- **Final whole-branch review caught a real Critical bug the five task-level reviews missed**:
  `MarketDataNeuron` was never added to `Program.cs`'s existing startup-warmup block (the one that
  explicitly activates `ILlmResponderNeuron` because "broadcasts only reach already-activated grains").
  Every test passed anyway because the Reqnroll steps force-activate the grain directly in the `Given`
  step, masking that the live demo would silently break the moment it was shown to the team. Worth
  flagging precisely because the controller's own guidance to the Task 2/5 reviewers had wrongly framed
  this as "a pre-existing, accepted `LlmResponderNeuron` gap" — never verified before being asserted as
  fact in review-scoping instructions. It wasn't: that gap is closed at `Program.cs:254`. Fixed in a single
  follow-up commit (`Program.cs` warmup line + corrected comment + two unrelated Minors: `InvariantCulture`
  on the CoinGecko price formatting, `sealed` on `MarketDataNeuron`), re-reviewed clean.

Real X/Twitter API integration and live-generation of the automation via `run_code_foundry` (which already
has its own separate Reqnroll coverage, `CodeFoundry.feature`) were explicit non-goals for this pass.

## Prior work summary (2026-06-24 through 2026-07-02, compacted 2026-07-02)

Full detail for everything below lives in git history (`git log`), not here — this is a recap so a
fresh session knows what's already built without re-discovering it. See `docs/SYSTEM_DESIGN.md` for
current architecture and `docs/PRODUCT_VISION.md` for what's being built and for whom.

- **2026-06-24 to 06-27 — Best-of-breed consolidation.** Ported the proven pattern from prior reference
  trees (`final`, `IAW`, `digitalbrain`, `v3`, `v4`) into this repo, typed C# only. Landed: causation/
  lineage on synapses, the pack embodiment keystone (`IPackBehavior` → compiled via Roslyn into a
  collectible `AssemblyLoadContext` → running grain, no restart), ECDSA pack signing/trust, MCP tools,
  typed SDK neurons (Git/Shell/FileSystem/DotNet/NuGet), checkpoint dedup + branching, the code-review
  neuron, hybrid-scorer memory (`ContextNeuron`), and real-money economics (Stripe + ECDSA licenses).
- **2026-06-26 to 06-27 — UI backbone + hardening.** `UiSurface`/`RfwCard`/`HomeFeedBus` + bidirectional
  gRPC `UiGateway`; renamed `Silo` → `Kernel` throughout; generic `Task*` protocol (deleted
  `KernelTask*` naming from Core); Bucket A security hardening (secure-default unsigned-pack rejection,
  MCP read/mutation split by transport, pluggable checkpoint key provider, rolling-update rollback).
- **2026-06-27 to 06-30 — Fully neuron-driven UI.** Deleted every hardcoded Dart chrome/nav/string; the
  entire shell (menus, buttons, headers, dividers) is authored by neurons via a small `NeuronUiKit`
  vocabulary and rendered by a thin client. Experiences (multi-hop guided flows, e.g. the travel
  planner) got a dedicated full-screen host route, proven by a real browser E2E. The typed `ui:` kit
  authoring loop ("Hello World on rails" — one ~15-line C# file, no kernel restart) shipped as Slice 0,
  then fanned out to a 35-component catalog + gallery (Sub-project B).
- **2026-07-01 to 2026-07-02 — Distribution model + cleanup initiative.** Product definition landed
  (now `docs/PRODUCT_VISION.md`): Bundle = `NeuroPack` + manifest, trusted-publisher v1, single
  Telegram bot with deep-link routing. Authoring-loop acceleration shipped (warm dev-cluster attach,
  `e2e.runsettings`, auto-build stale Flutter web bundle) collapsing the inner test loop from 30-120s
  to an attach. Then a long cleanup run applying Elon's 5-step algorithm strictly: System Neurons bloat
  delete, DbSupport test coverage, marker/alias trim, and four rounds of test-harness boilerplate dedup
  (Groups 1-4, detailed below for the last two rounds) migrating ~19 files off manual
  `TestClusterBuilder`/`IAsyncLifetime` onto a shared `NeuronTestBase` harness.
- **2026-07-02 — Repo cleanup.** Deleted ~55 historical spec/plan docs (their job was guiding
  now-merged work; git history is the durable record) and all stale branches (local + remote, all
  verified 0 commits ahead of master). `docs/` now holds only `PRODUCT_VISION.md`, `SYSTEM_DESIGN.md`,
  and `authoring-a-bundle.md`. Going forward: `docs/specs/<feature>.md` + `docs/plans/<feature>.md` are
  created per branch and **deleted after merge** — CONTINUITY.md's ledger and git log are where the
  history lives, not an ever-growing docs tree.

Known still-open threads from this history (not touched by the cleanup initiative):
- The original 30-step cleanup plan's actual capstone proof — an end-to-end demo of "Telegram input →
  cross-channel logic → `UiSurface` rendered in Flutter" — was never built, despite the underlying
  pieces (`IFlutterUiNeuron`, `FlutterUiNeuron`, `TelegramChatNeuron`, `DataVisualizationNeuron`) all
  existing individually. Zero tests found matching that flow.
- Distribution & Bundles Phase 2 (open publishing, untrusted-code sandbox, exportable bundle file,
  embeddable surface) — deliberately deferred in the v1 product spec, not started.

## 2026-07-02 correction + Group 3 test harness dedup (post-brainstorm round)

**Correction to a prior entry:** a "Slice marker-trim + UnitTest1-inners ... merged" claim was false — that work (commit 246bffa) existed only on the orphaned local branch `spec/marker-trim-unit1-clean`, never actually merged to master, despite the commit message on `d60cbb2` also claiming it landed. Discovered via direct verification (`git merge-base --is-ancestor 246bffa master` → NO; `git show d60cbb2 --stat` → only DbSupport files) at the start of this round. The branch was still fully intact, reviewer-approved, and build/test-clean (28/28) — landed via a straight merge (no re-implementation needed) before starting new work. Lesson: verify ledger claims against `git log`/`git show` before trusting them, especially after any session in a shared working directory.

Also re-verified (fresh research, since the "load-bearing" claim above for Core bloat was itself unverified prose): `CodeFoundrySynapses.cs`, `CompanySkillSynapses.cs`, `Awesome/ReviewSynapses.cs`, `Ui/RfwCard.cs` are all genuinely live (Foundry closed-loop, company-skill orchestration, the reviewer neuron, and RfwCard's 48 references in the UI streaming backbone) — the open `2026-07-02-core-bloat-delete-design.md` spec's premise doesn't hold; the only real dead item found was the orphaned `ISoftwareEngineeringTeam` marker interface (zero usages). That spec should be treated as researched-and-rejected, not implemented as written.

**Group 3 test harness dedup** (branch `spec/group3-test-harness-dedup`, merged to master via fast-forward at 0b4fe9f): finished the test-boilerplate-deduplication initiative's deferred Group 3. Extended `NeuronTestBase`/`TestDigitalBrain` with three additive hooks (`ConfigureClient`, `InitialSilosCount`, `Cluster`) and migrated the 10 remaining manual-`TestClusterBuilder` files (`LicenseAndEntitlementTests`, `RollingUpdateRollbackTests`, `TelegramDeepLinkRoutingTests`, `HomeFeedCrossSiloTests`, `TimelineStreamTests`, `GatewayServiceTests`, `GenericSendTests`, `ExperienceStepDispatchTests`, `WatchSynapsesTests`, `PackConfigPullTests`). Key insight that unblocked the "not mechanical" files from the earlier round: `ConfigureSilo` already runs as an instance-bound delegate (existing `AsyncLocal` bridge in `TestDigitalBrain.cs`), so the `static Shared*Bus` field bridge those files used was never actually necessary — replaced with direct instance-field capture, a net simplification. A real spike (not just an assumption) confirmed layering each file's `ConfigureSilo` on top of the always-applied `NeuronTestSiloConfigurator` is safe. All 6 implementation tasks + final whole-branch review passed with 0 Critical/Important findings; one Minor (redundant `AddSignalEgressStreamSubscriber()` double-registration in 2 files, masked by existing duplicate-tolerance) was found and fixed. Full suite unchanged throughout: 252 passed/6 skipped/0 failed.

New deferred items:
- **Group 4**: 9 files still build ad-hoc `TestClusterBuilder` per-test-method (`Ui/MarketplaceFilterRoundtripTests.cs`, `Trust/TrustedSeedInstallTests.cs`, `Trust/PublishGateTests.cs`, `Kernel/BroadcastReactivityTests.cs`, `Kernel/LlmResponderScopedConfigTests.cs`, `Distribution/CatalogMaterializationTests.cs`, `Distribution/HandlerGrowthTests.cs`, `Distribution/PackBroadcastReactivityTests.cs`, `Kernel/LlmResponderTests.cs`) — a different, larger pattern than Group 3's class-level `IAsyncLifetime`, not yet scoped.
- `UnitTest1.cs` domain split (still one file under a legacy scaffold name, though its manual boilerplate is gone).
- Close out `2026-07-02-core-bloat-delete-design.md` as rejected + delete the one real orphan (`ISoftwareEngineeringTeam`).
- `GatewayGrpcWireTests.cs` (ASP.NET `WebApplicationFactory` pattern, not Orleans `TestCluster`) intentionally out of scope for any Group.

## 2026-07-02 Group 4 test harness dedup (closes the test-harness-dedup initiative)

**Group 4 test harness dedup** (branch `spec/group4-test-harness-dedup`, merged to master): migrated the 9 files Group 3 deferred, closing out the entire Groups 1-4 test-harness-dedup initiative. Fresh research found the deferred "not yet scoped, different, larger pattern" judgment was too pessimistic — every file needed zero new `NeuronTestBase`/`TestDigitalBrain` capability. 5 files migrated directly (0-1 `ConfigureSilo` override, same shape as Group 3's mechanical tier); 4 files split via a **nested class** where facts diverge into genuinely different, mutually-exclusive cluster configs, or where a fact built no cluster at all (pure unit test, kept off `NeuronTestBase` to avoid an unneeded Orleans silo spin-up).

Two real plan defects were found and fixed empirically during implementation (not just asserted — verified by build failure/success):
1. A file that declares a standalone class implementing `ISiloConfigurator` needs `using Orleans.TestingHost;`; a file that only overrides `ConfigureSilo(ISiloBuilder builder)` without naming that type doesn't. The original plan text omitted the import from 2 files (`LlmResponderTests.cs`, `LlmResponderScopedConfigTests.cs`) — confirmed necessary via `CS0246` on removal.
2. A nested class carrying its own `[Fact]` must be `public`, not `private` — xUnit's `xUnit1000` analyzer rejects private test classes. The design's cited `UnitTest1.cs` precedent (`IsolatedReplayTest`/`StrictConfigNeuronTest`) was misread: those are `private` but have no `[Fact]`s of their own — they're manually instantiated helper subclasses (`new IsolatedReplayTest()` inside another fact), not independently-discovered test classes. Confirmed via `error xUnit1000` on `private`, success on `public`.

Both corrections were caught by the implementer subagents themselves stopping and escalating (BLOCKED) rather than guessing, then resolved via a direct controller spike (build + targeted test) before continuing — exactly the "spike genuinely risky assumptions empirically" guardrail working as intended. All 6 tasks + final whole-branch review (on the most capable model) passed with 0 Critical/Important findings; one Minor naming nit (`SignatureVerificationTests` → `SeedSignatureTests`, more precise) was applied directly. Full suite unchanged throughout: 252 passed/6 skipped/0 failed, same 15 facts across the 9 files (redistributed into 4 new nested classes, none added or removed).

`grep -rn "TestClusterBuilder\| : IAsyncLifetime" DigitalBrain.Tests` now returns zero hits outside two genuinely out-of-scope patterns never part of this initiative: `Gateway/GatewayGrpcWireTests.cs` (ASP.NET `WebApplicationFactory`, not Orleans `TestCluster`) and `E2E/DigitalBrainAppHostFixture.cs` (Aspire AppHost fixture) — plus the `DigitalBrain.Tests/Steps/*.cs` Reqnroll BDD step-binding files (`CodeFoundrySteps.cs`, `ConfigFormSteps.cs`, `NeuronSteps.cs`, `TelegramReactiveLoopSteps.cs`), which build `TestClusterBuilder` per-step rather than per-xUnit-class — a structurally different problem (Reqnroll scenario lifecycle, not `IAsyncLifetime`) that was never in the Group 1-4 scope and would need its own design if ever tackled.

Deferred items carried forward:
- `UnitTest1.cs` domain split.
- Close out `2026-07-02-core-bloat-delete-design.md` (now deleted along with the rest of `docs/specs/` — TODO if resumed: re-derive from git history) as rejected + delete the one real orphan (`ISoftwareEngineeringTeam`) — note: verified this round that `ISoftwareEngineeringTeam` is the base interface of the live `ISoftware20Team`/`Software20TeamNeuron`, so "deleting" it means flattening its members directly onto `ISoftware20Team`, not a pure no-op removal.
- Fresh scan for other dumb duplication/dead code beyond what prior rounds found.
- The Reqnroll `Steps/*.cs` `TestClusterBuilder` pattern noted above — a real, never-previously-flagged candidate for a future round, distinct in shape from Groups 1-4.

## 2026-07-02 repo cleanup (branches + docs)

Per explicit user direction to unify/simplify before starting new feature work: deleted all stale branches (8 local + 5 remote, all verified 0 commits ahead of master — `spec/authoring-loop-acceleration` + 3 slices, `spec/db-support-tests`, `spec/fix-pre-existing-test-failures`, `spec/telegram-llm-experience`, `spec/test-boilerplate-deduplication`, `origin/spec/group3-test-harness-dedup`, `origin/spec/neuron-test-harness-consolidation`). Deleted ~55 historical spec/plan docs across `docs/plans/`, `docs/specs/`, `docs/superpowers/{specs,plans}/`, plus 2 misplaced top-level docs and a stray untracked garbage file at the repo root. Promoted `docs/specs/2026-07-01-distribution-and-bundles.md` (a product spec, not an implementation plan) to `docs/PRODUCT_VISION.md`. `docs/` now holds exactly 3 files: `PRODUCT_VISION.md`, `SYSTEM_DESIGN.md`, `authoring-a-bundle.md`. Fixed cross-references in `SYSTEM_DESIGN.md`, `README.md`, `authoring-a-bundle.md`. Compacted this file (see "Prior work summary" above). Established convention going forward: `docs/specs/`/`docs/plans/` are per-branch scratch, deleted after merge.

Next: a couple of core end-to-end use cases, proven with real tests, following the Musk 5-step algorithm strictly — see `E:\digitalbraintech\core-requirements\Musk approach.txt`. First candidate discussed: install an X/Twitter integration pack + a new ino that reacts to a specific author's posts and sends the current Bitcoin price to a Telegram chat — a concrete cross-channel proof, in the spirit of the original (never-built) "Telegram → viz → UiSurface" capstone demo.
