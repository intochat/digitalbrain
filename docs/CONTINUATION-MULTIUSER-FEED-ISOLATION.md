# CONTINUATION — Multi-User Feed Isolation: Client-Server Session Bridge

Status: RESOLVED — see docs/superpowers/specs/2026-07-04-multiuser-feed-isolation-design.md (design) and
docs/superpowers/plans/2026-07-04-multiuser-feed-isolation-clientid-routing.md (implementation). Per-session
feed isolation (P6a) is enforced via per-clientId Orleans stream routing; the fail-open shim from commit
50ed11e has been fully reverted.
Repo: `E:\brain`. Prior art — read in this order: `docs/CONTINUATION-MULTIUSER-IDENTITY.md` (original
identity design, decisions D-MU1–D-MU7, invariants I1–I5), `docs/CONTINUATION-MULTIUSER-S2-S3.md` (the S2/S3
implementation this bug was found in, now shipped), `docs/superpowers/plans/2026-07-04-multiuser-s2-s3-
identity-and-salesforce-per-user.md` (the implementation plan — its "Known Limitations" section has the
full bug write-up), `CONTINUITY.md` (dated entries for Stage S1, Stage S2/S3, the live-bug mitigation, and
the resolution — read the `2026-07-04` entries there before anything else). Date: 2026-07-04.

---

## 0. Mandatory process requirement for this session

**Use Context7 to look up current Orleans documentation before writing or changing any code, and before
dispatching any subagent that will write code.** Specifically: grain streaming (`IAsyncStream<T>`,
`IClusterClient.GetStreamProvider`, `StreamId.Create`), implicit stream subscriptions, and whether Orleans
has an idiomatic pattern for "route this stream item to exactly the one client that should see it" that is
better than the hand-rolled `ConcurrentDictionary<Guid, (string? SessionId, Channel<RfwCard>)>` fan-out this
repo currently uses (`DigitalBrain.Kernel/Ui/HomeFeedBus.cs`). That mechanism was built without checking
Orleans' own streaming primitives for a native fit, and its poor match to how the real client actually
connects is the root cause of today's bug. Also verify gRPC / gRPC-Web server-streaming semantics — can a
server proactively re-scope an already-open stream, or is client reconnection the only option? (The Dart
client already has one documented constraint: gRPC-Web cannot client-stream — see
`app/lib/shell/forui_app_shell.dart` around the `_openUiSession` comment.) Confirm this against current
`grpc-dart`/`Grpc.Net.Client` docs rather than assuming.

This requirement exists because the S2/S3 session that caused today's bug did **not** do this — it
designed and shipped a hand-rolled addressing mechanism, and a live end-to-end test only happened after a
user hit the bug in production. Don't repeat that.

---

## 1. What actually broke, and why (already fully diagnosed — read before re-deriving)

Stage S2 (`docs/CONTINUATION-MULTIUSER-S2-S3.md`) added session-based addressing: `RfwCard` gained a
`SessionId` field, `HomeFeedBus.FanLocal` only delivers an addressed card to a subscriber whose own
registered session matches, and `GatewayService.WatchHomeFeed`/`InoRequest`/`AuthRequested` all resolve a
client-supplied `sessionId` against the real `IUserSessionNeuron` session grain. The **stated** acceptance
criterion: "unauthenticated `WatchHomeFeed` sees only login + unaddressed system cards; user A's client
never receives a card addressed to user B." All backend tests for this passed. The real app broke
completely within minutes of shipping.

**Root cause, confirmed by direct inspection of the Flutter client (not asserted from the backend plan):**

1. **The client never has a real session to send.** `app/lib/shell/forui_app_shell.dart:96` opens
   `WatchHomeFeed` exactly once, in `initState()` (app startup, before any login), always with an empty
   `WatchHomeFeedRequest()`, and never reconnects it. Login is entirely **server-driven**: the server sends
   a form surface, the client just dispatches whatever `synapseType` the form declares — there is no
   `LoginRequest` construction anywhere in the Dart client (`grep -rn "LoginRequest" app/lib` — zero hits),
   and nothing in the client ever reads or stores the `sessionId` that `UserSessionCreated`/`LoginSucceeded`
   return. So the field S2.4 added to `WatchHomeFeedRequest` has no way to ever be populated by the real
   app as currently written.

2. **"sessionId" already meant something else in this codebase.** `app/lib/features/chat/chat_screen.dart:58`:
   `final String _sessionId = 'chat-${Random().nextInt(1 << 31)}';` — a client-generated, per-widget-instance
   **UI correlation token**, sent with every `InoRequest` (`chat_screen.dart:169`) purely so the chat screen
   can recognize its own replies (`chat_screen.dart:136`: `if (data['sessionId'] != _sessionId) return;`).
   This predates the multiuser work entirely and has nothing to do with authentication. S2.5's `InoRequest`
   branch tried to resolve this token against the real session grain (it never matches → collapses to
   `null`), which broke the client's own reply-matching — a real regression in a feature that already
   worked, not a security fix.

3. **These two problems compound.** Even after passing the raw token through unchanged again, `HomeFeedBus`'s
   addressing still requires the **subscriber** (the `WatchHomeFeed` connection) to have registered a
   matching session — which no client connection ever does, for either meaning of "sessionId" — so every
   addressed card (chat replies, the post-login shell, installed-bundles/marketplace/task-manager surfaces,
   Salesforce/Google credential forms) was silently dropped for every real client.

**Exact commits from the S2/S3 session that introduced this:** `6de07e9` (RfwCard/HomeFeedBus addressing),
`1e08b4f` (WatchHomeFeed session resolution), `eef58e7`+`0a5bada` (payload-identity-trust deletion,
including the `InoRequest` branch), `367636d` (Salesforce `AuthRequested` branch requiring a real session).

---

## 2. Current mitigation (temporary — must be replaced, not just left in place)

Commit `50ed11e` applied a fail-open compatibility shim, chosen explicitly over a full redesign to unblock
the running app immediately:

- `DigitalBrain.Kernel/Ui/HomeFeedBus.cs`, `FanLocal`: only enforces addressing once the **subscriber** has
  registered a non-null session. No client registers one today, so every subscriber currently sees every
  card — full fail-open, matching pre-S2 behavior.
- `DigitalBrain.Kernel/Gateway/GatewayService.cs`, `InoRequest` branch: passes the client's `sessionId`
  straight through again instead of resolving it (restores the chat correlation-token pass-through).
- `DigitalBrain.Kernel/Gateway/GatewayService.cs`, `SalesforceSignals.AuthRequested` branch: falls back to
  the `"anonymous"` identity instead of throwing `Unauthenticated` when no real session resolves (restores
  the single-user "Connect Salesforce" flow).

**Net effect: P6a (per-session feed isolation) is not enforced right now.** Any client that connects to
`WatchHomeFeed` sees every card ever broadcast, addressed or not. This is acceptable only because the
system is currently single-user in practice. It must not ship to a multi-user deployment as-is.

---

## 3. What "true multi-user separation" requires — open design questions, assess before implementing

Do not assume the shape of the fix yet. These need research (Context7 + tracing the real client) before a
plan is written:

**A. Client session capture.** How does the Flutter client learn its real login `sessionId` after the
server-driven login form succeeds, and get it into `WatchHomeFeed`? The login response arrives as a
broadcast surface on the *same* feed connection that's already open and unauthenticated — there is no
separate "login succeeded" callback today.

**B. Stream re-scoping vs. reconnect.** Given gRPC-Web can't client-stream, is a full reconnect (tear down
+ reopen `WatchHomeFeed` with the now-known session) the only option, or does gRPC/gRPC-Web offer any
mid-stream re-negotiation? If reconnect is required: what happens to cards broadcast in the gap between
disconnect and reconnect? Does the client need a "catch-up" mechanism, or is a brief gap acceptable given
`HomeFeedBus`'s content-hash dedup would just mean a missed card, not a duplicate?

**C. Is in-process filtering the right mechanism at all, or should this be Orleans-native?** Investigate
whether keying the stream itself per-user (e.g. `StreamId.Create("homefeed", userId)` instead of one shared
`StreamId.Create("homefeed", Guid.Empty)` fanned out and filtered by `HomeFeedBus`) is more idiomatic —
Orleans grains already key per-user everywhere else in this codebase (`NeuronScope`, Salesforce grains).
Would each authenticated client just subscribe to *its own* stream, with a separate always-broadcast
"system" stream for unaddressed cards (login form, etc.)? This would remove the addressing check from
`HomeFeedBus` entirely rather than fixing it in place.

**D. Untangle the two "sessionId" concepts everywhere, not just where this bug surfaced.** Grep every
producer and consumer of the string `"sessionId"` on both sides (`app/lib/**`, `DigitalBrain.Kernel/**`,
`DigitalBrain.Core/**`) before touching addressing logic again. Proposal to evaluate: keep `sessionId`
meaning "real login session" everywhere, and rename the chat's per-widget correlation token to something
that can never collide again (e.g. `conversationId` or `replyToken`) — both client and server side,
including every `UiSurface.Props["sessionId"]` producer that's actually using it as a routing tag today
(`InoNeuron.DeliverReplySurfaceAsync` and friends).

**E. Map every concurrent `WatchHomeFeed` connection a single app instance opens.** A repo-wide grep this
session found `watchHomeFeed`/`WatchHomeFeedRequest` referenced in `app/lib/shell/forui_app_shell.dart`,
`app/lib/features/chat/chat_screen.dart`, `app/lib/features/canvas/living_canvas_screen.dart`,
`app/lib/features/surface_demo/surface_demo_screen.dart`, `app/lib/features/experience/
experience_host_screen.dart`, and `app/lib/features/canvas/panel/panel_manager.dart` — **not individually
verified this session**. Confirm whether these are independent, simultaneously-open connections (in which
case "one client = one session = one stream" is false and any per-connection design needs to handle that),
dead/legacy code, or something else, before designing.

**F. Reconcile with D-MU6 and the S5 chat-threading design.** `docs/CONTINUATION-MULTIUSER-IDENTITY.md`'s
D-MU6 keeps `session-main` a singleton "for now," with a documented mitigation path of a gateway-side
short-TTL cache or eventually a per-session grain. S5 (not yet started) plans `{userId}/{threadId}`-keyed
Ino grains for real chat history. Whatever fix this doc's work lands on should not paint S5 into a corner —
re-read both before finalizing a design.

---

## 4. Explicit anti-patterns to avoid (lessons from this specific incident)

- Don't assume a JSON field name (`"sessionId"`) means one thing across an entire codebase without grepping
  every producer and consumer first — a shared name does not imply a shared concept.
- Don't ship a client-visible behavior change without driving the actual client end-to-end before declaring
  done. A green backend test suite (`DigitalBrain.Tests`, `DigitalBrain.Salesforce.Tests`) gave false
  confidence here; none of those tests exercise the real Flutter app's connection lifecycle.
- Don't skip Context7 for framework API assumptions. The addressing mechanism was hand-rolled without
  checking what Orleans streaming already offers for this exact problem shape.
- Prefer the framework's idiomatic mechanism over an equivalent hand-rolled one when one likely exists.

---

## 5. Suggested first steps for the next session

1. **`superpowers:brainstorming`** before locking a design — this has multiple valid shapes (reconnect-based
   client fix vs. per-user Orleans stream vs. something else) and deserves an explicit compare, not the
   first idea that compiles.
2. Context7: Orleans streaming (implicit subscriptions, stream keys/namespaces, `IAsyncStream<T>`,
   `GetStreamProvider`) and gRPC/gRPC-Web server-streaming reconnection semantics.
3. Trace every Flutter call site listed in §3E to build a complete, verified map of concurrent
   `WatchHomeFeed` connections before designing anything.
4. Grep every producer/consumer of `"sessionId"` (Dart + C#) per §3D before touching addressing logic.
5. Once the shape is clear, write a new `docs/CONTINUATION-...`/plan doc following this repo's established
   pattern (see the docs listed at the top) rather than resuming directly in code.

---

## 6. Test story requirement for whatever plan follows this

Any new plan must include an explicit step to drive the real Flutter app end-to-end (not only backend
unit/integration tests) before declaring the work done — that gap is exactly what let this bug reach
production undetected.
