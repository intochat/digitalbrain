# S1.3-GRILL — adversarial review of the OAuth/Integration rail

## What was reviewed

Judge only; no production edits; no git write.

| Slice | Location |
|-------|----------|
| RED (pins) | `c2874c11` — S1.3-RED OAuth/MCP characterization |
| GREEN (rail) | `a17b1ac6` — S1.3-GREEN OAuth/Integration rail |
| Diff | `git log -3`, `git diff HEAD~2..HEAD` |
| Worker reports | `plans/stage1/reports/S13-oauth-{red,green}.md` |
| Authority | `plans/RATIFIED-PRODUCT-DEFINITION.md` §1.14 / P0-1/P0-5/P0-7; brief `plans/stage1/briefs/S13-oauth-green.md`; `GROK.md` kernel traps |

## Gate (verified this session)

```
dotnet build DigitalBrain.slnx
Build succeeded.
    0 Error(s)
(node NO_COLOR AppHost noise only — not C# warnings)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 115, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 41.279s
```

Chart flake not observed. Gate green does **not** imply security acceptance.

## Attack results

### 1. PKCE on every authorize URL path

| Path | Result |
|------|--------|
| `McpAuthorizationRail.BuildPkceAuthorizeUrl` Salesforce branch | **PASS.** Always appends `code_challenge` + `code_challenge_method=S256` (`McpAuthorizationRail.cs:220–232`). |
| Same method — generic `AuthorizeEndpoint` branch | **PASS.** Same challenge params (`:235–252`). |
| Same method — public-base / localhost fallbacks | **PASS.** Same challenge params (`:263–280`). |
| `McpOAuthCallback` second mint | **PASS.** No `AuthorizationUri` state mint; recovers via `Claim` (`McpOAuthCallback.cs:10–36`). |
| Shared `Begin` contract enforcement | **FAIL (MAJOR).** `CodeChallenge` / `CodeVerifier` are optional (`McpAuthorizationVocabulary.cs:14–15`); `Begin` never refuses missing PKCE (`McpAuthorizationNeuron.cs:40–122`). |
| Gmail typed path (shared neuron) | **FAIL (MAJOR residual).** `GoogleSignIn.BuildAuthorizeUrl` has no PKCE (`GoogleSignIn.cs:22–45`); `GmailAuthRail` still `Begin`s without challenge/verifier (`GmailAuthRail.cs:105–120`). S1.6 owns Gmail deletion, but the **shared** authorization surface still accepts non-PKCE mints. |

MCP rail mint is PKCE-correct. The store boundary does not enforce it.

### 2. State store: bounded / expiring / one-shot / claim paths

| Check | Result |
|-------|--------|
| Durable pending (not static dict) | **PASS.** `IDurableDictionary` on `McpAuthorizationNeuron` (`:25–26, :123–125`). |
| Capacity | **PASS (open).** `MaxPendingStates = 64` + settled refusal (`:17, :100–104`). |
| TTL + mutation sweep | **PASS.** 15m `AuthorizationTtl`, `SweepExpiredAsync` (`:18, :463–481`). |
| Unknown callback → no hub growth | **PASS.** Refuse without `Complete` (`:211–214`); hub drops orphans (`McpAuthorizationCodeHub.cs:48–51`). |
| Deliver replay after Completed | **PASS.** Non-Open / Consumed refused (`McpAuthorizationNeuron.cs:225–228`). |
| `TakeCompletedCode` one-shot | **PASS for single consumer.** Clears code + `Consumed` (`:346–350`). |
| Hub Completions hard bound | **SOFT.** Eviction only drops non-waiter/non-session keys; can exceed 64 while waiters live (`McpAuthorizationCodeHub.cs:147–166`) — MINOR. |
| **Cross-principal claim via CommandId** | **BLOCKER.** See finding 1. |
| **Cross-principal take of code** | **BLOCKER.** See finding 2. |

### 3. Token purposes / user A → user B

| Check | Result |
|-------|--------|
| Purpose shape `integration/user/{provider}/{principal:N}` | **PASS** as string design (`McpTokenPresence.cs:63–93`). |
| Per-server grain dictionary + subject key | **PASS** for multi-provider collision (each `mcp:{server}` grain has its own `mcp.gateway.oauth.principals`). |
| Exchange checks bound actor vs caller | **PASS late.** After take (`McpAuthorizationRail.cs:142–147`). |
| **Actor is client-asserted** | **BLOCKER.** See finding 3. |
| Gmail tokens still neuron-keyed | **Residual (S1.6).** Honest in GREEN report; not a silent regression of MCP purpose. |
| Isolation proof in tests | **MAJOR weakness.** `PrincipalTokenSlotsIsolateUserAFromUserB` only asserts purpose strings + Begin state conflict — never stores tokens for A and proves B cannot resolve them (`OAuthRailProofs.cs:277–317`). |

### 4. Destructive-tool removal vs unauthorized / audit

| Check | Result |
|-------|--------|
| Destructive blanket rejection removed | **PASS.** Comment + no `tool.Destructive` throw (`McpServerNeuron.cs:57–58`). Catalog still exposes `Destructive` hint. |
| Missing actor on live MCP path | **PASS.** Settled `NeuronAuthorizationException` with sign-in fix path (`:241–246`). |
| Unknown tool / bad FireRowsAs | **PASS.** Still settled refusals (`:43–55, :114–118`). |
| Audit on success | **PASS partial.** Inbound `CallMcpTool.Actor` + outbound `McpToolReturned{Actor, IntegrationSubject}` (`:89–97`). |
| Audit on failure outcome | **MAJOR soft.** No explicit failure audit fact with integration subject; relies on inbound synapse + exception. Brief asked actor+integration+tool+correlation+**outcome**. |
| Pin quality for P0-7 | **MAJOR weak.** `DestructiveToolsAreCallableWhenAuthorized` uses cluster `IMcpToolTransport` which **skips** `AuthorizedAsync` entirely (`McpServerNeuron.cs:203–207`, `BrainClusterFixture.cs:59–60`). Proves tool returns, not principal OAuth boundary. |

### 5. Kernel traps on touched neurons

| Trap | Result |
|------|--------|
| 2 zero-receiver emit | **PASS with note.** `AuthorizationRequired` is `EmitAsync` + directed `SendAsync` to `chat:main` (`McpAuthorizationNeuron.cs:134–136`). Chat consumes via `OnUnboundSynapseAsync` (not `IHandle`) — correct trap-8 avoidance. |
| 3 reification | **PASS.** `CapabilityRequested` journals contract/method/target only — not Begin args (`CapabilityRequested.cs`, `OutgoingReificationFilter`). `IMcpAuthorization` is not FrameworkInterfaces (not kernel infra); reification is expected. |
| 4 settled refusals | **PASS mostly.** Capacity / missing actor / bad tool use `NeuronAuthorizationException`. State-conflict `Begin` throws `InvalidOperationException` (`McpAuthorizationNeuron.cs:85–86`) — not settled; OK for grain RPC, wrong shape if ever delivered as a turn. |
| 8 broadcast catalog | **PASS.** No new `IHandle<Authorization*>`. |

### 6. Pins deleted vs flipped

| RED pin | GREEN disposition |
|---------|-------------------|
| Dual state mint | Flipped → `AuthorizationRailIsTheSoleStateMintAndLibraryCallbackDoesNotMint` |
| No PKCE on SF URL | Flipped → `SalesforceAuthorizeUrlAlwaysCarriesPkceS256` |
| Unbounded Completions | Flipped → `CodeHubDropsUnknownStatesInsteadOfAccumulatingOrphans` |
| Unknown → hub fill | Flipped → `UnknownCallbackStatesAreRejectedAndDoNotFillTheCodeHub` |
| Code replay | Flipped → `CompletedAuthorizationCodeIsOneShotAndReplayIsRefused` |
| Token keying | Flipped → `McpTokenPurposesKeyByPrincipalNotNeuronIdentity` |
| Callback principal-blind | Flipped (reframed) → anonymous HTTP + pending Actor binding |
| Pending no principal | Flipped → `AuthorizationPendingBindsTheLocalUserPrincipal` |
| Destructive refuse | Flipped → `DestructiveToolsAreCallableWhenAuthorized` |

No `// PIN-DEFECT` markers remain under `src/Tests`. **No pin was deleted without flip.** Composition pins are largely source greps (honest characterization style inherited from RED).

### 7. Secrets hygiene

| Channel | Result |
|---------|--------|
| Durable pending code / verifier | **PASS.** Protected via `IDurablePayloadProtector` (`McpAuthorizationNeuron.cs:396–436`). |
| Auth journal facts | **PASS.** `Authorization{Required,Completed,Denied}` carry CommandId/Server/State/Actor — not code/verifier/token. |
| Capability reification | **PASS.** No argument payload. |
| Token storage | **PASS.** Protector purpose envelopes in `DurableMcpTokenCache`. |
| Token exchange error body | **PASS.** Status only, not response body (`McpTokenExchange.cs:53–57`). |
| **Client-facing Take API** | **BLOCKER.** Returns plaintext `Code` + `CodeVerifier` on a `[ClientEntryPoint]` grain (`IMcpAuthorization.cs:6–30`, `McpAuthorizationNeuron.cs:342–351`). |
| In-process hub Completions | **MINOR.** Plaintext code+verifier until waiter consumes (`McpAuthorizationCodeHub` + DeliverCallback `:251–257`). Not journaled. |

### 8. Quality / dead code / contract honesty

| Item | Result |
|------|--------|
| Manual non-PKCE `SignInUrl` path deleted from MCP rail | **PASS.** |
| Dual library mint killed | **PASS** (design note in GREEN report matches code). |
| Migration honesty | **PASS.** Pre-prod delete-and-reauthorize stated. |
| `Integration` record type landed | **PASS** (`Integration.cs`). Not yet a durable store entity beyond purpose string — fine for Stage 1. |
| Idempotent `Begin` as state recovery | **Smell (GREEN admits).** Works; leaves actor-blind recovery hole (finding 1). |
| `[Description]` on auth facts | **MINOR.** Violates “names ARE documentation”; pre-existing, GREEN only added Actor. |
| Gmail “compat” still full typed OAuth | **Expected residual** for S1.6. |

---

## Numbered findings

1. **BLOCKER — Idempotent `Begin` recovers any principal’s pending auth by `CommandId` alone.**  
   `McpAuthorizationNeuron.cs:54–69` returns the recorded `AuthorizationRequired` (including **State**) whenever the command exists, **without** comparing `request.Actor` to `recorded.Actor`.  
   `CommandId` + `State` are published on the installation-wide authorization SSE (`MapAuthorizationStreams.cs:72–85`).  
   Combined with finding 2, any client that can `GetGrainProxy<IMcpAuthorization>` (interface is `[ClientEntryPoint]`, `IMcpAuthorization.cs:6`) can mint a recovery Begin for Alice’s command and learn her state.  
   P0-5 / brief “completion resolves ONLY through the state binding” + principal-bound pending is incomplete.

2. **BLOCKER — `TakeCompletedCode` is principal-blind ClientEntryPoint secret exfiltration.**  
   `IMcpAuthorization.cs:29–30`, `McpAuthorizationNeuron.cs:314–351`.  
   No actor/caller check; returns plaintext authorization **code** and **PKCE verifier**.  
   GREEN *expanded* the return shape with `CodeVerifier` (`McpAuthorizationVocabulary.cs:40–44`).  
   Race after `DeliverCallback` / `AuthorizationCompleted` SSE: attacker Take wins over rail `ExchangeCompletedAsync` (`McpAuthorizationRail.cs:135`).  
   Attacker can complete the provider token exchange out-of-band with Alice’s code+verifier.  
   One-shot helps the second taker, not the first malicious one.

3. **BLOCKER — MCP tool Actor is client-trusted identity (banned); token slots follow the claimed principal.**  
   `CallMcpTool.Actor` is optional model/client JSON (`IMcpServer.cs:37–42`).  
   `McpServerNeuron.AuthorizedAsync` keys `PrincipalTokenSlot` from that claim (`McpServerNeuron.cs:241–249`).  
   `SystemTools` / `SynapseCapabilityTool.BindModelArguments` does not strip or overwrite Actor with the verified host principal.  
   User A who knows (or guesses) user B’s `PrincipalId` resolves **B’s** integration tokens.  
   Violates ratified ban on client-trusted identity and P0-5 “tokens keyed by **verified** principal — never … silent fallback / other users.”  
   Storage purpose design is correct; **trust root is wrong**.

4. **MAJOR — PKCE not enforced at the authorization store; Gmail (and any ClientEntryPoint Begin) can still mint non-PKCE states.**  
   Optional challenge/verifier on `BeginMcpAuthorization` (`McpAuthorizationVocabulary.cs:14–15`); no validation in `Begin`.  
   Gmail path: `GmailAuthRail.cs:105–120` + `GoogleSignIn.BuildAuthorizeUrl` without challenge.  
   Ratified: “ONE PKCE authorization flow for all providers; the manual non-PKCE URL path dies.”  
   Brief out-of-scope for Gmail *deletion* does not license an open non-PKCE mint on the shared neuron.

5. **MAJOR — Isolation and destructive pins do not prove the ratified security properties.**  
   - Isolation: purpose-string inequality only (`OAuthRailProofs.cs:277–317`).  
   - Destructive: `FakeMcpTransport` bypasses OAuth/actor gate (`McpServerNeuron.cs:203–207`).  
   Gate-green tests can pass while findings 1–3 remain.

6. **MAJOR — Audit outcome incomplete on failed tool calls.**  
   Success stamps `Actor` + `IntegrationSubject` on `McpToolReturned` (`McpServerNeuron.cs:89–97`).  
   Failures rethrow without a durable outcome fact carrying integration subject (`:71–76`).  
   Brief: audit actor + integration + tool + correlation + **outcome**.

7. **MINOR — Code hub Completions bound is soft under concurrent waiters.**  
   `McpAuthorizationCodeHub.cs:147–166` may retain >64 entries while every key still has a waiter/session.

8. **MINOR — Auth synapses still carry `[Description]`.**  
   `AuthorizationFacts.cs:8–28` — convention debt (names-as-docs).

9. **MINOR — Composition PKCE/mint pins are source greps**, not provider-round-trip behavioral proofs (`OAuthRailProofs.cs:15–28`). Acceptable as RED-style characterization; do not treat as e2e security evidence.

---

## What GREEN got right (credit)

- Single MCP rail mint with S256 on every `BuildPkceAuthorizeUrl` branch.
- Library callback no longer mints state from `AuthorizationUri`.
- Durable pending with TTL, capacity, protected code/verifier, one-shot Deliver + Take.
- Hub orphan Completions no longer accumulate unbounded garbage.
- Purpose strings move off neuron identity for MCP.
- Destructive blanket rejection removed; live missing-actor path still settled-refuses.
- Migration honesty and Gmail residual documented.
- All nine RED pins flipped with markers removed (not silently deleted).

## Verdict rationale

Gate and pin hygiene are clean, and the MCP rail is a real improvement on P0-1 dual-state / hub orphans / replay.  
**P0-5 is not met:** principal appears on records but is not a verified control plane. Three independent paths let principal A obtain principal B’s OAuth material or tokens (CommandId recovery → Take; client Take of published state; forged Actor on `db.mcp.call-tool`). Those are BLOCKER-class against the ratified product definition and this brief. Fix those before APPROVE; do not treat test suite green as acceptance.

## Out of scope (noticed, not charged as S1.3 implementation debt beyond notes)

- Gmail typed-path deletion / Google MCP migration (S1.6) — residual non-PKCE mint **is** charged because it uses the shared Begin surface.
- Chat turn pipeline stamping Actor through the assistant (S1.5) — does not excuse trusting client Actor on the MCP neuron.
- Execution (S1.4), Flutter, CI.

---

## Findings index (severity)

| # | Severity | One-line |
|---|----------|----------|
| 1 | **BLOCKER** | Begin-by-CommandId ignores Actor → cross-principal state recovery |
| 2 | **BLOCKER** | ClientEntryPoint TakeCompletedCode returns code+verifier, no principal check |
| 3 | **BLOCKER** | CallMcpTool.Actor is client-trusted; tokens resolve from claimed principal |
| 4 | **MAJOR** | PKCE optional at Begin; Gmail (shared rail) still non-PKCE |
| 5 | **MAJOR** | Isolation / destructive pins do not prove isolation or live auth boundary |
| 6 | **MAJOR** | Failed MCP calls lack integration-bearing audit outcome |
| 7 | **MINOR** | Completions soft-cap under waiters |
| 8 | **MINOR** | `[Description]` on auth facts |
| 9 | **MINOR** | Source-grep composition pins ≠ behavioral e2e |

VERDICT: REJECT
