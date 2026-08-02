# DigitalBrain Google SDK-First Productization Design

**Status:** Draft for owner review (direction decisions locked with owner 2026-08-01; implementation NOT authorized)

**Date:** 2026-08-01

**Branch ground (audit):** `feature/digitalbrain-architecture-continuation` @ `4a04cceb`; merge-base `eb9c84d7` (master)

**Parent architecture:** `docs/superpowers/specs/2026-07-30-neurons-synapses-behaviors-design.md` — this design *implements* its §2.6 ("provider implementation choices") for Google; it amends only provider wording, never the neuron/synapse/Tasks model.

**Supersedes for Google:** the hosted-MCP southbound assumed by the 2026-08-01 productization design/plan (Wave 4 Gmail half).

---

## 1. Locked decisions (owner, 2026-08-01)

| ID | Decision | Rule |
|---|---|---|
| **G1** | Google backend = **REST/SDK-only now** | `Google.Apis.Gmail.v1` + `Google.Apis.Auth` as *private* implementation of the Gmail neuron. Hosted Gmail MCP path deleted; resurrectable from git only by a future owner decision after GA/allowlist |
| **G2** | Public surface = **intent + typed ops, both in v1** | Keep `GmailRequest` (intent). Add typed op synapses (§3.2) for deterministic behavior/scripting access. Grants are per-synapse; both coexist |
| **G3** | Sign-in = **one flow: browser consent through the app's callback URL** | The dev port-listener (`LocalLoopback…`, 148 LOC) and the whole `AuthorizationMode` setting are deleted. The name "Edge" dies |
| **G4** | Salesforce **stays on MCP** | Contrast rail, public-client PKCE. Its sign-in gets the missing wire fixed: the callback handler awaits the app-callback-delivered code (`TakeCompletedCode` finally gets its caller) — the robot-browser GET dies for SF too |
| **G5** | PRM alignment WIP: **commit as history first**, then delete in the migration slice | Owner (or first implementation session with authorization) lands the dirty-tree PRM handler + tests as one historical commit; the migration slice deletes them |
| **G6** | Naming: **full rename including the callback path** | Internal `Mcp*` names on now-generic code become provider-neutral; callback moves to `/oauth/callback` with redirect URI re-registered in the Google console. Boundary: journaled wire aliases — see §8 open item O1 |
| **G7** | Intent planner catalog = **runtime-reflected SDK surface** (owner override of the direct-two-function draft) | The Gmail planner's tool catalog is derived at **module activation** from the `Google.Apis.Gmail.v1` surface + its NuGet XML documentation semantics, filtered by a read-only admission allowlist, cached for the activation lifetime. Execution goes through the module-owned `GmailService`; `gmail.readonly` scope is the hard outer boundary. No per-request compilation; no SDK types in prompts/journals (results map to bounded `GmailMessage` before re-entering the conversation); scripts/behaviors still never touch the SDK |

## 2. Verdict recap (from the 2026-08-01 audit)

Hosted Gmail MCP is Developer-Preview-gated, Workspace-only to enroll, with no consumer-account promise — and independently, the live interactive sign-in path in this repo cannot complete against real Google (`EdgeMcpAuthorizationCallback` robot-GETs the authorize URL; the genuinely delivered code lands in `CodeReady`/`TakeCompletedCode`, which nothing awaits — verified). The REST path uses the same Google Cloud client, the same registered redirect model, and removes ~10 of 15 hops from the Gmail request path. Full evidence: `claude/digitalbrain-google-sdk-audit-2026-08-01.md` (project) / audit report of this date.

**Honest gate the SDK path carries:** `gmail.readonly` is a restricted scope. Testing-mode apps: ≤100 test users, refresh tokens expire every 7 days (weekly re-consent in dev). GA distribution to strangers requires Google app verification + CASA. This goes in README status and setup UX — never hidden.

## 3. Public contract

### 3.1 Unchanged (intent surface)

`IGmail` (marker, owner-scoped, instance name = connection name), `GmailRequest(intent)` → `GmailResponse(messages, error)`, `GmailMessage(id, subject, sender, plaintextBody)`. Wire aliases unchanged. Acceptance criterion 2 ("read my last three emails" without a hand-written wrapper) unchanged.

### 3.2 New typed ops (v1, read-only)

```csharp
[Alias("db.google.gmail-search-request")]
GmailSearchRequest(string Query, int MaxResults = 10, CommandId CommandId) : RequestSynapse<GmailSearchResponse>
// Query = Gmail search-box syntax (from:, is:unread, newer_than:…) passed to users.messages.list q=

[Alias("db.google.gmail-search-response")]
GmailSearchResponse(CommandId, IReadOnlyList<GmailMessageHeader> Headers, string? Error)

[Alias("db.google.gmail-message-header")]
GmailMessageHeader(string Id, string Subject, string Sender)   // no body — metadata only

[Alias("db.google.gmail-get-message-request")]
GmailGetMessageRequest(string MessageId, CommandId CommandId) : RequestSynapse<GmailGetMessageResponse>

[Alias("db.google.gmail-get-message-response")]
GmailGetMessageResponse(CommandId, GmailMessage? Message, string? Error)
```

Rules: bounded exactly like the intent path (`MaxMessages` 10 cap on search, `MaxBodyChars` 8192 on body); ops are **deterministic** — no model call anywhere in their handling; PublishGate `GoogleVocabulary` extends to the new closed set; catalog metadata generated as for any synapse; behavior grants name these edges individually. Mutating ops (send/label/trash) are **out of v1** — adding any is a new grilled decision because it changes the scope set and the verification posture.

## 4. Module internals (all private)

**Facade.** One internal `GmailProvider` owning a lazily-built, long-lived `GmailService` per neuron activation (`HttpClientInitializer = UserCredential`; UserCredential is thread-safe and auto-refreshes). Two methods mirror the ops: `SearchAsync(query, max)` → `users.messages.list`; `GetMessageAsync(id)` → `users.messages.get(format=FULL)` mapped to bounded `GmailMessage`. The intent planner and the typed ops both call this same facade — one implementation, two surfaces.

**Planner (intent path only, per G7).** Same ≤6-turn `IChatClient` loop as today, but the tool catalog is **derived from the SDK itself at module activation**: enumerate the admitted `Google.Apis.Gmail.v1` request surface (v1 allowlist: `Users.Messages.List`, `Users.Messages.Get`, `Users.Threads.List/Get`, `Users.Labels.List` — read-only only), attach descriptions from the package's XML documentation (the "NuGet semantics"), and materialize each as an `AIFunction` whose invocation binds LLM-supplied arguments onto the typed request object and executes via the module-owned `GmailService`. Cached per activation — zero per-request reflection/compile. `SdkCatalogAdmission` is the successor of `Gmail.Admit.cs`: an explicit allowlist plus structural rules (no mutating verbs), tested exactly as the old admission was. Defense in depth: the OAuth token carries `gmail.readonly` only, so anything that escapes the allowlist still cannot mutate at Google's side. Results are mapped to bounded `GmailMessage`/header shapes before re-entering the planner conversation — raw SDK responses never reach prompts or journals.

**Token custody.** `DurableGoogleTokenStore : IDataStore` over the existing `IDurableValue<byte[]>` + `IDurablePayloadProtector` (same rollback-on-failed-commit pattern as `DurableMcpTokenCache`, which it replaces on the Google path). Serialized `TokenResponse` (with refresh token — `access_type=offline` is the flow default) protected at rest, keyed by the same purpose scheme (`google/oauth/{connection}/{durableIdentity}`). Multi-account = neuron instance name, unchanged.

**No Google SDK type** appears in any public contract, journal, prompt, manifest, or vector payload. PublishGate additions: Google module must not reference `ModelContextProtocol.*`; contracts assembly must not reference `Google.Apis.*`.

## 5. Sign-in rail (one flow, renamed)

```text
Gmail neuron: no stored refresh token (or refresh fails permanently)
  → build real Google authorize URL via GoogleAuthorizationCodeFlow
    (scopes, state, redirect = <ui-base>/oauth/callback, offline access, PKCE)
  → AuthorizationNeuron.Begin → AuthorizationRequired journaled (facts only)
  → AuthorizationRequiredException parks the owning Task; Flutter shows "Connect Google"
Human consents in own browser → Google redirects to app callback
  → UI endpoint /oauth/callback → AuthorizationNeuron.DeliverCallback
  → code held PROTECTED (payload protector) or in-memory — never plaintext durable state (fix, see O2)
  → AuthorizationCompleted (no secret) → completion target notified → same Task continues
Gmail neuron resumes: TakeCompletedCode(state) → flow.ExchangeCodeForTokenAsync
  → TokenResponse into DurableGoogleTokenStore → proceed with the original command
```

What this deletes: `EdgeMcpAuthorizationCallback` (robot browser), `LocalLoopbackMcpAuthorizationCallback` + `Process.Start` in-silo browser launch, `McpAuthorizationCodeHub` statics + `McpAuthorizationAmbient` (Google usage), the `AuthorizationMode`/`AuthorizationPreflight` configuration pair (Google path), and the synthetic `"authorized:…"` TokenContainer branch in the rail (`McpAuthorizationRail.cs:79-91`). The `McpRuntime` WhenAny/SignInReady choreography is unnecessary for Google because the neuron never blocks inside an HTTP session waiting for consent — it parks and resumes.

Salesforce (G4): keeps the MCP-SDK-driven exchange, but its `AuthorizationCallbackHandler` becomes "await the code delivered through the app callback" — implemented against `TakeCompletedCode`/hub-await, deleting the robot-GET branch there as well. This is the fix that makes SF's live sign-in actually completable by a human.

## 6. Aspire hosting and configuration

- Parameters keep their names and user-secrets: `google-client-id`, `google-client-secret` (secret), `google-redirect-uri` (run-mode default = `http://localhost:5080/oauth/callback` after G6 re-registration). `mcp-authorization-mode` is deleted.
- Hosting types renamed provider-neutral (`ProviderOAuthHosting` or per-module `GoogleHosting`); descriptions stop mentioning MCP for Google; Salesforce hosting text stays MCP-honest.
- Google Cloud: only `gmail.googleapis.com` required; `gmailmcp.googleapis.com` enablement becomes removable (owner console action, optional).
- Readiness honesty unchanged: missing client id/secret fails closed with the module name; placeholder rejection stays.

## 7. Deletion ledger (this design's slice)

| Delete | When |
|---|---|
| PRM alignment handler + tests + hosting registration | after G5 historical commit |
| `EdgeMcpAuthorizationCallback.cs` | with rail rework (SF branch replaced by code-await) |
| `LocalLoopbackMcpAuthorizationCallback.cs` + mode/preflight config keys | with rail rework |
| `McpAuthorizationCodeHub`/`McpAuthorizationAmbient` | Google usage immediately; entirely if SF's await lands on `TakeCompletedCode` grain calls instead of statics |
| Synthetic token branch `McpAuthorizationRail.cs:79-91` | with rail rework |
| `Gmail.Admit.cs`, `GmailPlanner` MCP plumbing, Google `McpServerDefinition`/endpoint override | with facade landing |
| Google references to `ModelContextProtocol.*` | end of slice (PublishGate law seals it) |

**Not deleted:** `AuthorizationNeuron` state machine + facts vocabulary, UI callback endpoint (path renamed), Tasks continuation rail, all Salesforce MCP machinery, `McpToolFingerprint` (SF approvals), `McpTestEdge`/`FakeMcpProviderHost` (SF + rail proofs).

## 8. Open items (owner)

- **O1 — journal aliases under G6 full rename.** CLR renames are free; **`[Alias]` wire strings are identity** (w3 ratified rule: no journaled aliases changed). Full naming honesty for `db.mcp.*` facts would require a journal reset/migration on existing streams. Recommendation: keep `db.mcp.*` aliases as-is in v1 (they carry provider-neutral facts; only the prefix is cosmetic); revisit only if a journal-reset moment arrives anyway. If you want alias renames now, say so explicitly — it is a data-compatibility decision, not a refactor.
- **O2 — authorization-code custody (pre-existing).** `PendingAuthorization.Code` is persisted in plaintext durable state today (`McpAuthorizationNeuron.cs:219/334`), while spec §11 says codes never enter journals. Design fixes it (protect or don't persist); flagged so the fix is deliberate, not incidental.
- **O3 — Salesforce platform MCP GA status**: verify before the SF live gate.
- **O4 — Google app verification timeline**: decide when (if) to submit for restricted-scope verification vs living with Testing mode's 7-day refresh expiry in dev.

## 9. Testing policy for the slice

L1: fake Gmail provider edge (scripted facade or fake REST host) replacing `ScriptedMcpSessionFactory` on the Google path — same `TestBrain` ergonomics; OAuth rail proofs point `GoogleAuthorizationCodeFlow` at a fake token endpoint (initializer supports custom token/auth server URLs), covering exchange, refresh, deny, state-mismatch, and the 7-day-expiry re-park deterministically with `TimeProvider`. PublishGate: vocabulary update (G2), the two new no-reference laws (§4). Explicit live oracle: `LiveAutomaticGmail` unchanged in shape — "Read my last three emails" through the real edge with real secrets, journals quoted; plus a live typed-op proof (search+get) and the same-Task continuation proof after real consent. Never a red root gate; never weakened assertions.

## 10. Sequencing sketch (for the plan document, not execution)

1. **Preflight:** G5 historical commit of PRM WIP; CodeGraph sync; baselines.
2. **Token custody + facade** (TDD against fake token endpoint + fake Gmail edge).
3. **Sign-in rail rework** (one flow; SF code-await fix; delete loopback/mode/synthetic token; O2 fix).
4. **Typed ops** (contracts + PublishGate + catalog + grants).
5. **Intent planner re-target** to facade functions; delete Admit/MCP plumbing; PublishGate no-MCP law.
6. **Naming sweep** (G6) + `/oauth/callback` re-registration; README status honesty (7-day consent note).
7. **Live proofs** (owner secrets): intent, ops, continuation; journals quoted.

Each slice: Five Steps in order, failing proof first, root gate green, Context7 before Google.Apis API use.

## 11. Self-review

| Check | Result |
|---|---|
| Contradicts parent spec? | No — implements §2.6; requires one wording amendment in §15 Slice 5 ("MCP tool selection" → "provider call selection") |
| MCP-as-requirement leftovers | None on Google path; SF explicitly MCP by decision G4 |
| Five Steps | Requirement challenged (audit §3), deletions enumerated (§7), simplification = hop collapse, cycle time = no allowlist/no port conflict, automation last (PublishGate laws only) |
| Placeholder/TBD | O1–O4 are explicit owner items, not hidden TBDs |
| Secrets | TokenResponse protected at rest; codes never plaintext-durable (O2); nothing in journals/prompts |
| Scope risks | G2 doubles public-surface work in v1 (owner-chosen); G6 re-registration is operational churn in every environment (owner-chosen); G7 runtime-reflected catalog is the audit's Strategy-E mechanism accepted by owner override with mitigations (activation-time build, allowlist admission, read-only scope boundary) — the allowlist test suite is the non-negotiable guard |
