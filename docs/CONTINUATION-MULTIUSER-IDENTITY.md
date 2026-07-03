# CONTINUATION — Multi-User Identity, OAuth Isolation & Conversation State

Status: DESIGN APPROVED-PENDING (owner decisions D-MU1…D-MU6 below). No implementation yet.
Repo: `E:\brain`. Prior art researched: `E:\IAW` (`src/Core/Context`, `src/Core/Agents` + tests).
Date: 2026-07-04.

This document is the session handoff for implementing per-user isolation across OAuth
integrations (Salesforce, Google, future providers) and the Ino chat layer. Read it top to
bottom before touching code. Every claim below was verified against the current tree, not
assumed.

---

## 1. Confirmed problems (verified in code)

P1. **Grain-routing bypass → cross-replica race.** `DigitalBrain.Kernel/Program.cs` maps
`SalesforceClientFactory.DefaultCallbackPath` as a minimal-API endpoint that reads/writes
`IPackConfigStore` and performs the token exchange directly, bypassing the
`SalesforceAuthNeuron` that ran `StartOAuthAsync`. With 3 kernel replicas behind Aspire's
proxy the callback can land on a different replica; distributed traces confirmed
same-replica → success, cross-replica → empty pending-state read, 100% reproducible.
Mitigated (pending PKCE isolated into `OAuthPendingPackName` slot) but not fixed.

P2. **Every per-user resource is a global singleton.** Grain keys: `"salesforce-auth-main"`,
`"salesforce-main"`, `"google-auth-main"`, `"gmail-main"`, `"ino-main"`, `"session-main"`.
`IPackConfigStore` scope hardcoded to `"default"` in `SalesforceAuthNeuron`, the callback
endpoint, `BuildGoogleCredential`, `InoNeuron.HasGoogleCredentialAsync` /
`HasSalesforceCredentialAsync`, and the scoped DI client factories in `Program.cs`.

P3. **Identity exists but is a dead end.** `UserSessionNeuron` (`"session-main"`) journals
`LoginSucceeded` / `UserSessionCreated` and exposes `GetSessionAsync(sessionId) →
UserSessionState { UserId, … }`. Nothing consumes `UserId` for grain keying or store scoping.

P4. **OAuth callback correlation is unsolved for multi-user.** `state = Guid.NewGuid("N")`,
correlated only via the global pending slot. The callback GET is cold and unauthenticated —
it must be able to resolve back to the correct user's grain by itself.

P5. **No conversation abstraction.** `InoNeuron` (`"ino-main"`) builds context by string-mashing
journal tails (`BuildContextAsync`), has no per-user/per-thread separation, no token budget,
no summarization discipline (a `MemorySummary` synapse exists but is ad hoc).

P6. **NEW — found during this research.** Two multi-user leaks at the boundary:
  a. `GatewayService.WatchHomeFeed` streams *every* `HomeFeedBus` card to *every* subscriber.
     Multi-user without per-session filtering = every client sees every user's surfaces.
  b. The gateway trusts client-supplied `sessionId` / `userId` / `buyerId` payload fields.
     Identity is client-asserted, never validated against `session-main`.

---

## 2. Prior-art verdict (E:\IAW)

Researched: `ContextProviderIdentity`, `UserContextProvider`, `PolicyContextProvider`,
`RAGContextProvider`, `AgentRoutingContextProvider`, `Agent.cs` (+ partials),
`DurableChatHistoryProvider`, `ChatReducer`, `HistorySummarizer`, `Agent.Authorization.cs`,
`Directory.Packages.props`, `test/Core.Tests`.

**Trustworthy.** IAW is on Microsoft.Agents.AI **1.0.0 GA**. Verified against current
Microsoft Learn docs (2026-07): custom `ChatHistoryProvider` with
`ProvideChatHistoryAsync`/`StoreChatHistoryAsync` is the official third-party storage
pattern; `AIAgent.CurrentRunContext` is a real documented ambient that flows across async;
`AgentSession.StateBag` is the documented home for session-scoped state;
`MessageAIContextProvider` + `.AsBuilder().Use(middleware)` match the documented
ChatClientAgent pipeline. Test coverage is real: `HistoryDurabilityTests` proves history
survives forced grain deactivation on a live `TestCluster`; `ChatReducerTests` covers the
reducer.

**Patterns to adopt:**
- Composite grain key `{userId}` / `{userId}/{threadId}` as the identity carrier, parsed by
  one shared helper (`Agent.ParseIdentityFromGrainKey` shape).
- `ChatReducer` algorithm: recent window + non-reducible pinning (files/images/"remember"/
  "approval") + image eviction outside the window + per-message truncation + total char
  budget with oldest-first eviction.
- `HistorySummarizer` algorithm: incremental (re-summarize only when the old window grew),
  summary + watermark persisted durably, restore-once-after-reactivation, fail-open to the
  existing summary.
- The composable context-provider stack shape (profile / policy / RAG / routing providers,
  each fail-open with a logged warning).

**Do NOT port:**
- `long.TryParse` userId validation (Telegram numeric IDs; DigitalBrain userIds are
  usernames).
- The ambient-identity machinery (`CurrentRunContext` / `StateBag`). IAW needs it because
  the framework invokes its providers. DigitalBrain invokes its own pipeline — identity can
  and should be passed explicitly (see §4.5).
- The Microsoft.Agents.AI dependency itself — deferred, see D-MU5.

---

## 3. Invariants (write these down, they govern everything below)

I1. **The grain key IS the identity.** Per-user neurons are keyed `{userId}`; conversation
neurons `{userId}/{threadId}`. Orleans single-activation-per-key turns isolation from a
discipline into a property.

I2. **userId is derived server-side, exactly once, at the gateway** via
`session-main.GetSessionAsync(sessionId)`. From there it travels only as a grain-key
segment. Client-supplied identity fields are display hints at most, never authorization.

I3. **Journals record truth about what happened; the encrypted pack-config store holds
secrets.** Journal `OAuthFlowStarted` / `OAuthCompleted` / `OAuthFailed` (no secrets).
Tokens, refresh tokens, pending PKCE material never enter a journal (Core Law 2 makes
journals permanent — secrets there are forever).

I4. **Pack-config scope is two-tier.** `"default"` = app-level (connected-app
client_id/client_secret, Google Cloud client creds, LLM keys — genuinely shared).
`"user:{userId}"` = per-user (access/refresh tokens, instance_url, pending PKCE).

I5. **Incremental.** Salesforce ships first; nothing requires a big-bang rewrite of Google
or the chat layer.

---

## 4. Design

### 4.1 Identity spine (`DigitalBrain.Core`)

```csharp
public readonly record struct NeuronScope(UserId UserId, string? ThreadId)
{
    public static bool TryParse(string grainKey, out NeuronScope scope) { /* split on first '/' */ }
    public string ToKey() => ThreadId is null ? UserId.Value : $"{UserId.Value}/{ThreadId}";
}

public static class PackConfigScopes
{
    public const string App = "default";
    public static string ForUser(UserId userId) => $"user:{userId.Value}";
}
```

`Neuron` base exposes `protected NeuronScope? Scope`. Both the auth layer and the chat
layer read identity from the same place — one mechanism, not one per integration.

Prerequisite: `UserSessionNeuron.NormalizeUsername` must reject `/`, whitespace and quotes
at registration (userId becomes a grain-key segment). Cheap now, painful later.

### 4.2 Gateway identity resolution + feed filtering (fixes P3, P6)

`GatewayService` resolves `sessionId → UserSessionState` once per call (helper method;
short-TTL in-process cache acceptable later). Rejects invalid/expired sessions for
user-scoped operations. `InoRequest`, auth signals, install actions all route to
user-keyed grains using the *resolved* userId. Delete trust in payload
`userId`/`buyerId`.

`HomeFeedBus.Subscribe(sessionId)` filters cards: a card addressed to a
sessionId/userId reaches only subscribers authenticated for it; unaddressed system cards
remain broadcast. `WatchHomeFeed` request gains a sessionId (validated).

### 4.3 OAuth redesign (fixes P1, P2, P4)

**Routing (kills the replica race by construction).** One endpoint replaces per-provider
endpoints:

```csharp
app.MapGet("/oauth/{provider}/callback", async (string provider, HttpRequest request,
    IGrainFactory grains, IDataProtectionProvider dp) =>
{
    var state = OAuthState.Unprotect(dp, request.Query["state"]);
    var auth = grains.GetGrain<IProviderAuthNeuron>(NeuronKeys.Auth(provider, state.UserId));
    var result = await auth.CompleteOAuthAsync(new OAuthCallback(
        request.Query["code"], state.Nonce,
        request.Query["error"], request.Query["error_description"]));
    return Results.Content(OAuthResultPage(result), "text/html", statusCode: result.StatusCode);
});
```

The endpoint parses and routes — no store IO, no token exchange. Orleans delivers
`CompleteOAuthAsync` to the single live activation that ran `StartOAuthAsync`, on whichever
replica it lives, regardless of which replica the HTTP request hit.

**Correlation (D-MU2).** `state = IDataProtector.Protect({ userId, provider, nonce,
issuedAt })`. The cluster already shares a blob-backed DataProtection key ring across all 3
replicas → tamper-proof, confidential, replica-agnostic, zero new grains. The grain still
compares the nonce against its own pending flow, preserving (and per-user strengthening)
the CSRF check. Fallback option (only if a provider imposes state-length limits): a
singleton `OAuthFlowRegistry` grain mapping nonce → owner key; race-free but reintroduces a
singleton hot path + TTL bookkeeping. Salesforce and Google do not need the fallback.

**Pending PKCE storage (D-MU3).** Stays in the per-user pack-config slot
(`user:{userId}` / `OAuthPendingPackName`). The clobbering race is already dead once only
the owning grain reads/writes the slot. Grain memory alone is rejected (flows take minutes;
deactivation mid-flow loses the verifier). Journaling the verifier is rejected per I3.

**Shared abstraction (deletes Salesforce/Google duplication).** Generic `OAuthFlowNeuron`
base (key = `{userId}`) owning start/complete/nonce/pending-slot mechanics + journaled
lifecycle synapses, parameterized by:

```csharp
public interface IOAuthProviderAdapter
{
    string Provider { get; }
    string PackName { get; }
    bool HasAppConfig(IReadOnlyDictionary<string, string> appValues);
    string CreateAuthorizationUrl(IReadOnlyDictionary<string, string> appValues,
        string redirectUri, string protectedState, string codeChallenge);
    Task<IReadOnlyDictionary<string, string>> ExchangeCodeAsync(
        IReadOnlyDictionary<string, string> values, string code, string redirectUri);
}
```

`SalesforceAuthNeuron` shrinks to an adapter (~30 lines). `GoogleAuthNeuron`'s current
dev-placeholder flow (`DevClientId`, callback "to be wired") is deleted and Google is born
onto the shared path — nothing to migrate.

### 4.4 Per-user API clients (fixes the scoped-DI gap)

Current factories (`BuildGoogleCredential(sp, "google", "default")`, Salesforce
`CreateApiClientAsync(...).GetAwaiter().GetResult()`) can't know the user and fail-fast at
activation. With per-user grains:

- Factories resolve `IGrainContextAccessor` from the per-activation DI scope, parse
  `NeuronScope` from the activating grain's key, read `user:{userId}` scope.
- Switch eager-throw-on-activation → lazy resolution (`IUserApiClientFactory` /
  `Func<Task<T>>`). With per-user grains, "user hasn't connected yet" is the common path;
  correct behavior is activate fine + surface the connect button on first use (`InoNeuron`
  already knows how).

### 4.5 Chat layer (fixes P5)

`IInoNeuron` keyed `"{userId}/{threadId}"`. Gateway resolves session → userId, picks
threadId (client-supplied to continue; absent = user's default thread). Each thread grain
gets dual journals for free — and **`InoRequest`/`InoResponse` synapses in the thread
journal ARE the durable chat history**. No second history store; a projection.

DigitalBrain-owned seam (no Microsoft.Agents.AI, see D-MU5):

```csharp
public readonly record struct TurnContext(NeuronScope Scope, string? SessionId, string Prompt);

public interface ITurnContextProvider
{
    ValueTask<IReadOnlyList<ChatMessage>> ProvideAsync(TurnContext turn, CancellationToken ct);
}
```

`TurnPipeline` composes, in order: journal-projected history (ChatReducer + HistorySummarizer
algorithms ported from IAW onto the Synapse projection; summary + watermark persisted per
thread) → user-profile provider → skill/pack context (structured replacement for what
`BuildContextAsync` scrapes) → optional Qdrant RAG via existing `DigitalBrain.Context`.
Providers are fail-open with logged warnings (IAW shape). Identity is passed explicitly via
`TurnContext` — no ambient state. `InoNeuron.HandleAsync(InoRequest)` shrinks to intent
shortcuts + `pipeline.BuildAsync(turn)` + `IChatClient` + response synapses.
`BuildContextAsync` is deleted. The keyword-regex intent routing is out of scope here.

If DevUI / framework middleware is ever wanted, `ITurnContextProvider` adapts to
`MessageAIContextProvider` trivially — the seam is dependency-shaped either way.

---

## 5. Owner decisions

- **D-MU1** — Identity carrier = composite grain key `{userId}` / `{userId}/{threadId}` +
  `NeuronScope` in Core; usernames forbid `/`, whitespace, quotes. [PROPOSED ✅]
- **D-MU2** — OAuth callback correlation = DataProtection-encrypted `state`
  (userId + provider + nonce + issuedAt), shared key ring already in place. Registry-grain
  fallback documented, not built. [PROPOSED ✅]
- **D-MU3** — Pending PKCE lives in per-user pack-config slot, single-writer (owning
  grain). Never journaled. Tokens never journaled (I3). [PROPOSED ✅]
- **D-MU4** — Pack-config scope convention: `"default"` = app-level, `"user:{userId}"` =
  per-user. [PROPOSED ✅]
- **D-MU5** — Microsoft.Agents.AI dependency: **NOT taken now.** Reasons: (1) its session +
  history abstractions duplicate what Neuron journals already are — adopting it means two
  sources of conversational truth or a provider that only projects the journal; (2) ambient
  `CurrentRunContext`/`StateBag` exists to smuggle identity into framework callbacks —
  grain-key identity passed explicitly is strictly better; (3) repo already rides Orleans
  10.2.1-preview journaling alphas — avoid another fast-moving framework. Borrow the
  algorithms (reducer, summarizer, provider-stack shape), own the seam. Revisit if DevUI /
  tool-approval middleware becomes a concrete need. [PROPOSED ✅]
- **D-MU6** — `session-main` stays a singleton for now (identity directory + session
  resolution). Acknowledged as the next `"*-main"` in line; mitigation path = gateway-side
  short-TTL cache, later per-session grain. Not this iteration. [PROPOSED ✅]

Open (decide before S5): threadId format — user-visible slug vs opaque id; default-thread
semantics per user.

---

## 6. Stages

**S1 — Salesforce callback grain-routed (ships alone, fixes P1 permanently).**
Move token exchange + pending-state read into `SalesforceAuthNeuron.CompleteOAuthAsync`.
Endpoint becomes parse-and-route. Keys stay `"salesforce-auth-main"`; protected state
carries `userId = "default"`. Acceptance: TestingHost regression test delivering the
callback via a different silo frontend than the one that started the flow — passes; the
old direct-store path is deleted from `Program.cs`.

**S2 — Identity spine.** Username charset validation; `NeuronScope` + `PackConfigScopes`
in Core; gateway-side session resolution helper; `HomeFeedBus` per-session filtering;
delete trust in payload identity fields. Acceptance: unauthenticated `WatchHomeFeed` sees
only login + unaddressed system cards; user A's client never receives a card addressed to
user B.

**S3 — Salesforce per-user.** Auth + CRM neurons keyed `{userId}`; tokens under
`user:{userId}`; connected-app config stays `"default"`; `IGrainContextAccessor`-based lazy
client factory. Acceptance: two-user interleaved OAuth test (below) green.

**S4 — Google on the shared flow.** `OAuthFlowNeuron` + `IOAuthProviderAdapter`;
Salesforce refactored onto it; Google adapter written; dev-placeholder Google flow and the
per-provider callback endpoint deleted in favor of `/oauth/{provider}/callback`.

**S5 — Chat layer.** `{userId}/{threadId}` Ino threads; `TurnPipeline` with ported
reducer/summarizer; `BuildContextAsync` deleted; `"ino-main"` retired or aliased to the dev
user's default thread.

---

## 7. Test story

- **Replica-routing regression (S1):** start flow on silo A's activation, deliver callback
  through silo B's frontend, assert exchange succeeds and pending nonce matches.
- **Cross-contamination (S3+):** users A and B run interleaved `StartOAuth` →
  callback flows; assert A's nonce/verifier/tokens are unreadable from every B-keyed grain
  and every `user:B` store read, and vice versa; wrong-user callback (A's protected state,
  B's code) fails closed.
- **State tampering:** mutated protected `state` → unprotect fails → 400, no grain call.
- **Feed isolation (S2):** two authenticated `WatchHomeFeed` streams; card addressed to A's
  session never appears on B's stream.
- **Thread isolation (S5):** interleaved conversations on `A/t1`, `B/t1`, `A/t2`; each
  thread's projected history contains only its own turns; summarizer watermark per thread.
- **History durability (S5):** force activation collection mid-conversation (IAW
  `HistoryDurabilityTests` shape); projected history + summary survive reactivation.

---

## 8. Risks / notes for the implementing session

- Filesystem MCP quirk: do not `list_directory` the repo root; use targeted subdirectory
  listings + `read_multiple_files` batches.
- `[GenerateSerializer]` + sequential `[Id(n)]` + concrete arrays on every new synapse
  (OAuthFlowStarted/Completed/Failed, OAuthCallback, TurnContext-adjacent records that
  cross grain boundaries) — non-negotiable.
- Journals are never deleted (Core Law 2) — hence I3; review every new synapse for secret
  content before it ships.
- `AspireNeuron` call inside `UserSessionNeuron.HandleAsync(LoginRequest)`
  (`StartDistributedApp`) fires per login — verify it is idempotent under multi-user login
  storms before S2.
- The gateway's `ConfigurationProvided` branch keeps honoring explicit `scope` but must
  whitelist: only `"default"` (admin-gated later) or the caller's own `user:{userId}`.
- Google credential scopes currently hardcode Gmail/Drive/Calendar full access in
  `BuildGoogleCredential` — revisit minimal scopes when Google lands on the shared flow.
