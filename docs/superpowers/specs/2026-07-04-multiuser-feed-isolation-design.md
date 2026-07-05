# DESIGN — Multi-User Feed Isolation: Client Identity + Orleans-Native Routing

Status: DESIGN APPROVED-PENDING (this session, conversational approval — see "Approval trail" at the
bottom). No implementation yet. Repo: `E:\brain`. Date: 2026-07-04.

Supersedes the open design questions in `docs/archive/CONTINUATION-MULTIUSER-FEED-ISOLATION.md` (§3 A-F). Read
that doc's §1-§2 for the full incident diagnosis and the current fail-open mitigation (commit `50ed11e`) —
not re-derived here. Builds on `docs/archive/CONTINUATION-MULTIUSER-IDENTITY.md` (invariants I1-I5) and
`docs/archive/CONTINUATION-MULTIUSER-S2-S3.md` (S2/S3 implementation this bug shipped in).

---

## 1. What this doc replaces

The continuation doc framed this as six open design questions needing fresh research (§3 A-F: client
session capture, stream re-scoping vs. reconnect, Orleans-native vs. in-process filtering, untangling
`"sessionId"`, mapping concurrent connections, reconciling with S5). Direct verification against the
current tree (not the doc's own flagged-as-unverified claims) closed most of these without a redesign:

- **§3E (concurrent connections) — resolved by reading `app/lib/router.dart`.** `/` (`ChatScreen`),
  `/chat`+`/gallery`+`/marketplace` (`ForuiAppShell` via one `ShellRoute`), `/canvas`, `/experience` are
  mutually exclusive go_router destinations. Only one `WatchHomeFeed` is ever open at a time.
  `SurfaceDemoScreen` is a separate demo entry point (`SurfaceDemoApp`/`MaterialApp`), never concurrent
  with the real app. No design impact.
- **§3B (reconnect vs. re-scope) — wrong question.** Addressing was already enforced in application code
  (`HomeFeedBus.FanLocal`), never in the transport. Nothing about gRPC needs to change.
- **§3C (Orleans-native per-user stream) — real, and the answer changed twice during this session** (see
  §3 and §4 below): the first pass concluded the existing shared stream didn't need to change; a second,
  more aggressive simplification pass (this doc's final design) concluded it should, but for a different
  reason than originally proposed (deleting hand-rolled fan-out code, not "being more Orleans-native" for
  its own sake).
- **§3D (untangle `"sessionId"`) — scoped precisely by grep, not "everywhere."** Confirmed exactly one
  client-side origin (`chat_screen.dart`'s `_sessionId`) flowing through exactly one field
  (`InoRequest.SessionId`) into `InoNeuron`, including one still-live bug beyond the already-mitigated one:
  `InoNeuron.ResolveUserIdAsync` (`InoNeuron.cs:123,147-153`) tries to resolve this correlation token as a
  real session and silently fails today, even under the current fail-open shim.
- **§3F (S5 chat-threading) — unaffected.** Nothing below touches `session-main`'s singleton status or
  introduces grain re-keying. D-MU6 stands as-is.

## 2. Root cause, one paragraph (full diagnosis: continuation doc §1)

The client never has a real login session to send: `WatchHomeFeed` opens once at app startup, before
login, always empty; login is entirely server-driven (the server pushes a form, the client just submits
whatever `synapseType` it declares); nothing in the client ever captures the `sessionId` a successful login
returns. Separately, `chat_screen.dart` had already overloaded the JSON key `"sessionId"` as a
client-generated per-widget UI correlation token, unrelated to authentication, before this multiuser work
existed. S2 assumed `"sessionId"` meant one thing everywhere it appeared; it didn't.

## 3. The identity model this design commits to

**One client-facing identity: `clientId`.** A client generates it once per connection/screen instance and
sends it at `WatchHomeFeed` connect time and on every other outbound request. It is known from the first
packet, before any login, and never changes meaning.

**`sessionId` becomes purely server-internal.** It lives only inside `UserSessionNeuron`'s journal
(`LoginSucceeded`/`UserSessionCreated`, both already recording `ClientId` alongside `SessionId` —
`UserSessionNeuron.cs:69-70`) and is resolved on demand, server-side, via one new method:
`GetSessionByClientIdAsync(clientId)`. The client never receives, stores, or round-trips a raw `sessionId`
again.

This is deliberate, not incidental: this codebase already has a first-class `clientId` concept threaded
through the entire login path (`LoginRequest.ClientId`, the login form's `submitAction` payload pre-baked
with `clientId` by the server — `UiSurfaceRuntime.cs:139,149` — so the client's submit button re-sends it
with **no Flutter code required for that leg**). The bug is that every connection is hardcoded to
`clientId: "flutter"` (`GatewayService.cs:261-262`, `UiSurfaceRuntime.cs:109`'s default) instead of a real
per-connection value — the mechanism already exists, it's wired to a constant. Collapsing to one
client-facing identity removes the possibility of the `"sessionId"`-means-two-things collision recurring,
rather than patching around one instance of it.

## 4. Transport: delete the hand-rolled fan-out, use Orleans streams natively

**Today:** one shared cross-silo Orleans stream (`StreamId.Create("homefeed", Guid.Empty)`) feeds a
per-silo relay (`HomeFeedStreamSubscriber`, a silo lifecycle participant) which fans every card into a
hand-rolled `ConcurrentDictionary<Guid, (string? SessionId, Channel<RfwCard>)>` in `HomeFeedBus`, filtered
by a manual per-subscriber loop-and-compare in `FanLocal`. Three layers of manual fan-out for a job Orleans
streams already do by keying: only subscribers to a given stream key receive what's published to it.

**New shape:**
- The existing unaddressed/system stream (`StreamId.Create("homefeed", Guid.Empty)`) is **unchanged** —
  broadcast-to-everyone cards keep working exactly as today, zero touch.
- A new personal stream per connection, `StreamId.Create("homefeed", clientId)` (string keys are supported
  since Orleans 7, confirmed via Context7 against current Orleans docs — `StreamId` moved from GUID-only to
  `Namespace`/`Key` string-backed identities). Cards addressed to one connection publish here instead of
  being broadcast-and-filtered.
- `GatewayService.WatchHomeFeed` subscribes directly to **both** streams for the lifetime of the gRPC call
  (merging them into the one response stream), using the `IClusterClient` `HomeFeedBus` already holds.
  Orleans's own pub-sub tracks "who's listening" — no dictionary, no manual comparison loop, no per-silo
  relay needed.

**Deleted entirely:** `HomeFeedStreamSubscriber.cs` (the whole file — its only job was relaying the one
shared stream into the now-deleted local dictionary), its DI registration
(`Program.cs:209`'s `AddHomeFeedStreamSubscriber()` call and the same call in
`NeuronTestSiloConfigurator.cs:56`), `HomeFeedBus`'s `_subscribers` dictionary,
`Subscribe()`/`Subscription : IDisposable`, and the per-subscriber filter loop in `FanLocal`. `HomeFeedBus`
shrinks to two methods: `Broadcast(RfwCard card)` (unchanged, system stream) and a new
`Address(RfwCard card, string clientId)` (publish) plus whatever subscribe-side helper
`GatewayService.WatchHomeFeed` needs to merge both streams into its response — both thin wrappers over the
stream provider that is already injected today.

**The existing single-silo/test fallback (`if (_clusterClient is null) { FanLocal(card); return; }`,
`HomeFeedBus.cs:56-61`) is deleted, not preserved.** It has no equivalent once `FanLocal` and the
dictionary are gone. Orleans TestingHost already provisions a real `IClusterClient` with working memory
streams for single-silo test clusters (the existing `HomeFeedCrossSiloTests.cs` already runs against one),
so `HomeFeedBus` can require a real `IClusterClient` unconditionally — confirm no test constructs
`HomeFeedBus` with a null `IClusterClient` outside that TestingHost path before removing the constructor's
default; if one exists, it needs a TestingHost-backed client instead, not a preserved null-path.

**`RfwCard` no longer needs a routing `SessionId` field.** Addressing is now determined by *which stream a
card was published to*, decided once at broadcast time, not by a field every subscriber re-checks on every
card. `Address(card, clientId)` takes the routing target as a call parameter; it does not live on the card.

**Dedup** (`HomeFeedBus`'s content-hash `IsDuplicate` check) is unaffected in purpose, but moves from
"once per subscriber per card" to "once per publish call" — a side simplification, not a behavior change.

## 5. Server-side changes

**Proto (`digitalbrain.proto`):** `WatchHomeFeedRequest` gains `string client_id = 1;`, replacing the
existing `session_id` field (which could never be populated at connect time anyway — that was the actual
defect, not a missing field). Breaking change, acceptable: this is a pre-production/greenfield installation
(confirmed in the S2/S3 plan's "Known Limitations" section — no prior real multi-user deployment exists).

**`GatewayService.WatchHomeFeed`:** reads `request.ClientId`; builds the initial login surface with it
(replacing the `"flutter"` literal at `GatewayService.cs:261-262`); subscribes to the connection's personal
+ system streams instead of calling the old `homeFeedBus.Subscribe()`.

**`UserSessionNeuron`:** `HandleAsync(LoginRequest)`/`HandleAsync(LogoutRequest)` already receive a real
`clientId` (no change to the method signatures) — the only change is that it's no longer always `"flutter"`
once the gateway forwards a real one. `BroadcastProductHomeAsync(user, sessionId)` (`UserSessionNeuron.cs:
76,148`) gains a `clientId` parameter (it's already in scope at its one call site,
`HandleAsync(LoginRequest)`) and threads it into `BuildSignedInShellSurface`,
`MarketplaceUiSurfaces.InstalledBundlesFromPacks`, `MarketplaceUiSurfaces.MarketplaceListFromPacks`, and
`UiSurfaceLiveData.TaskManagerFromTasks` — each of these swaps its `sessionId` addressing parameter for
`clientId`, and the resulting surfaces address via `HomeFeedBus.Address(card, clientId)` instead of the
deleted `RfwCard.SessionId` field. New method: `IUserSessionNeuron.GetSessionByClientIdAsync(clientId)`,
same LINQ-over-journal shape as the existing `GetSessionAsync(sessionId)`, resolving the latest active
(non-expired, non-ended) session for that `clientId` from `UserSessionCreated`/`UserSessionEnded` — no new
journal entries, just a new read over data already recorded.

**`GatewayService.Send` branches** (`InstallFromMarketplace`, `SalesforceSignals.AuthRequested`,
`ConfigurationProvided`, `InoRequest`, `LogoutRequest`) all currently read a `"sessionId"` prop off the
payload and either resolve it (correctly, for real sessions) or pass it through unresolved (the
`InoRequest` branch, `GatewayService.cs:165-174` — today's shim, because that field is actually the chat
correlation token). All of these switch to reading `"clientId"` off the payload and resolving real
identity via `ResolveSessionAsync` → `GetSessionByClientIdAsync(clientId)`. (`LoginRequest` is unaffected
here — it already reads only `clientId`, never `sessionId`, since no session exists yet at login time;
listed separately in §3, not part of this rename.) This is the fix for the still-live bug in §1:
`InoNeuron.ResolveUserIdAsync` (`InoNeuron.cs:123,147-153`) will now resolve a real user for
Salesforce-via-chat, instead of silently failing on a correlation token it can never match.

**Surfaces that currently stamp `Props["sessionId"]`** for later round-tripping (`SalesforceAuthSurfaces.
CredentialForm`, `UserSessionNeuron`'s shell/session-status surfaces, marketplace/task-manager surfaces)
swap to `Props["clientId"]`. The client never needs to see a real `sessionId` value at all — one identity,
never reinterpreted.

**Fail-open shim removal** (commit `50ed11e`, documented in the S2/S3 plan's "Known Limitations"): once
real per-connection addressing works, revert all three pieces — `HomeFeedBus` addressing enforces
unconditionally (moot anyway, since the mechanism changed to per-stream routing), `InoRequest`'s
`clientId` resolves to a real session rather than passing through unresolved, and Salesforce
`AuthRequested` resolves the caller's real user instead of falling back to `"anonymous"`.

## 6. Flutter client changes

- Every `WatchHomeFeed` call site (`forui_app_shell.dart:96`, `chat_screen.dart:102`,
  `living_canvas_screen.dart:239`, `experience_host_screen.dart:63`) generates one stable per-instance
  `clientId` (same generation pattern chat already uses — `Random().nextInt(1 << 31)`, just reaching one
  call earlier) and sends it via the new `client_id` request field.
- `chat_screen.dart`'s `_sessionId` is renamed `_clientId` (the earlier "unify" decision) — one token,
  used both for its own `WatchHomeFeed` subscription and every outbound signal
  (`InoRequest`/upload/Salesforce), replacing every `'sessionId'` JSON key it currently sends
  (`chat_screen.dart:169,257`) with `'clientId'`.
- `chat_screen.dart`'s reply-matching filter (`_onCard`: `if (... || data['sessionId'] != _sessionId)
  return;`, line 136) is **deleted, not renamed.** Once the server only ever delivers a connection's own
  addressed cards on that connection's own personal stream, a client-side re-check of the same field is
  redundant — the routing guarantee already holds by construction.
- No reconnect logic anywhere. No client-side session storage. The client's only new responsibility is
  generating one token at startup and sending it consistently.

## 7. Non-goals / explicitly deleted from scope

- No second Orleans stream for a "bind" control-plane message, no session-rebinding RPC, no generic
  clientId-stamping interceptor layered over every request type — an earlier pass in this session proposed
  all three; all deleted once the existing `clientId`-in-login-flow mechanism was found.
- No Flutter reconnect-after-login flow — deleted per §3B above; never needed.
- No per-user Orleans stream keyed by `userId` — deleted; `clientId` is known earlier (pre-login) and is
  the correct key for a *connection*, which is what needs addressing. `userId`/`sessionId` stay resolved
  on demand, server-side only, via the journal.
- No change to `session-main`'s singleton status, no grain re-keying, no chat-threading (S5) work.
- No client-side multi-tab session sharing (a second browser tab is a second `clientId`, gets its own
  independent connection) — not a stated requirement anywhere in the prior art, not built.

## 8. Test story

Per the continuation doc's mandatory §6 requirement, carried forward unchanged: **any implementation of
this design must drive the real Flutter app end-to-end before being declared done** — a green backend
suite was exactly the false signal that let the original bug reach production.

- **Two-user isolation:** two authenticated `WatchHomeFeed` connections (distinct `clientId`s); a card
  addressed to A's `clientId` never appears on B's stream. Model on the existing
  `HomeFeedCrossSiloTests.cs` shape, adapted for per-clientId stream subscriptions instead of the deleted
  dictionary-based subscribe.
- **Unauthenticated visibility:** a connection that never logs in sees only the system stream (login
  form + unaddressed cards), never another connection's personal stream.
- **Login round-trip:** submitting the server-authored login form (which already embeds the connecting
  `clientId` with no client code change) results in the *same* connection receiving its own
  `BuildSignedInShellSurface`/installed-bundles/task-manager cards on its personal stream.
- **Salesforce-via-chat identity resolution (the still-live bug in §1):** `InoRequest`'s `clientId`
  resolves to a real session via `GetSessionByClientIdAsync`, and `SalesforceSignals.AuthRequested`
  reaches the correct per-user grain — not the `"anonymous"` fallback — once a real session exists for
  that `clientId`.
- **Real Flutter app, end to end:** log in through the actual app, confirm the shell/chat/Salesforce
  credential form all render and route correctly; confirm a second concurrent login (second browser
  instance) never sees the first user's cards.

## 9. Known limitations carried forward, unchanged by this design

The S2/S3 plan's existing "Known Limitations" section (shared-scope token leak for any installation with a
pre-existing OAuth flow completed under the old singleton model) is orthogonal to this design and remains
accurate as written — confirmed not applicable to this installation.

## 10. Approval trail

Reached through iterative brainstorming in this session, not a single up-front proposal:
1. Initial proposal (client-id + late-binding over a second control stream) — rejected by the user as
   still too complex and not leaning enough into Orleans idioms.
2. Discovery that `clientId` already exists end-to-end in the login path, just hardcoded to a constant —
   accepted as the identity model (§3), but the transport still used the old hand-rolled dictionary.
3. Final pass (this doc): replaced the hand-rolled fan-out with direct per-`clientId` Orleans streams,
   deleting `HomeFeedStreamSubscriber` and the dictionary entirely, and collapsed `sessionId` out of the
   client's vocabulary altogether. Approved by the user as the version to write up.
