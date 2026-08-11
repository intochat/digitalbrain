# S1.3-GRILL-2 — re-attack of GREEN-b OAuth rail fixes

## What was reviewed

Judge only; no production edits; no git write.

| Slice | Location |
|-------|----------|
| Prior REJECT | `plans/stage1/reports/S13-grill.md` (4 BLOCKERs) |
| Fix commit | `26859652` — S1.3-GREEN-b close GRILL blockers |
| Fix report | `plans/stage1/reports/S13-oauth-greenb.md` |
| Diff surface | `McpAuthorizationNeuron`, `IMcpAuthorization`/`IMcpAuthorizationCodes`, `VerifiedActor`, `SynapseCapabilityTool`, `SystemTools`, `Chat`, boundary proofs |

## Gate (verified this session)

```
dotnet build DigitalBrain.slnx
Build succeeded.
    0 Error(s)
(node NO_COLOR AppHost noise only — not C# warnings)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 121, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 41.576s
```

ChartVocabularyProofs single-timeout flake **not observed**. Gate green is necessary but not sufficient for security acceptance — attacks below are code-level.

---

## BLOCKER 1 re-attack — Begin recovery via stolen CommandId

### Original defect
Idempotent `Begin` recovered any pending auth by `CommandId` alone and returned `AuthorizationRequired` (including **State**) without comparing actors.

### Fix under test
`McpAuthorizationNeuron.Begin` (`McpAuthorizationNeuron.cs:55–66`): if the command exists and `recorded.Actor` is null **or** `recorded.Actor.PrincipalId != request.Actor.PrincipalId`, throw settled `NeuronAuthorizationException` with message shape `"… is not pending."` (no state / actor leak). Matching principal returns the recorded required fact.

### Concrete variants tried (static + pin)

| Variant | Result |
|---------|--------|
| Bob + Alice’s CommandId, junk state/URL | **CLOSED.** Settled refuse; message contains `"not pending"`; must not contain Alice’s state or username. Pin: `CrossPrincipalBeginRecoveryByStolenCommandIdRefusesLikeUnknown` (`OAuthBoundaryProofs.cs:164–214`). |
| Empty / null `Actor` | **CLOSED for recovery.** `ArgumentNullException.ThrowIfNull(request.Actor)` at `:50` — never reaches the recovery branch. (Shape is argument exception, not settled refuse — not a principal leak.) |
| Empty `PrincipalId` | **CLOSED.** `PrincipalId` constructor refuses `Guid.Empty` (`PrincipalId.cs:12–15`) — cannot construct a blank principal. |
| Case differences on principal | **N/A / closed.** Identity is `Guid` equality on `PrincipalId.Value`, not a string. Username is not consulted for recovery (`:62`). |
| Workspace scope confusion | **N/A.** `ActorContext` is only `(PrincipalId, Username)` (`ActorContext.cs:5–7`) — no workspace field on the recovery path. |
| Same principal, different username | Recovers (by design). Bound identity is principal, not display name. |
| Wrong principal on **Denied** / **Completed** command | Wrong principal hits the same `"not pending"` branch **before** outcome branching (`:61–66` before `:68–72`) — no existence / outcome leak. |

### Residual (not re-opening BLOCKER 1)
`Claim(CommandId)` remains principal-blind (`McpAuthorizationNeuron.cs:275–322`, ClientEntryPoint `IMcpAuthorization.cs:26–27`). For **Open** outcomes it returns `AuthorizationRequired` with **State**. GREEN-b already logged this as backlog. State is also projected on the installation-wide auth SSE (`MapAuthorizationStreams.cs:72–78` / `AuthorizationEvent.State`). **Without** client `TakeCompletedCode`, State alone does not yield code/verifier. Severity: **MAJOR residual** (state existence leak via Claim), not the original BLOCKER chain.

### Verdict on BLOCKER 1
**CLOSED.** Stolen-CommandId Begin recovery no longer returns another principal’s pending auth under the variants above.

---

## BLOCKER 2 re-attack — code/verifier on client-reachable surfaces

### Original defect
`TakeCompletedCode` on `[ClientEntryPoint] IMcpAuthorization` returned plaintext `Code` + `CodeVerifier`.

### Fix under test
- `IMcpAuthorization` methods: `Begin`, `BindCompletionTarget`, `Claim`, `DeliverCallback` only (`IMcpAuthorization.cs:6–28`). **No** `TakeCompletedCode`.
- `IMcpAuthorizationCodes` (new, **not** ClientEntryPoint) hosts one-shot take (`IMcpAuthorizationCodes.cs:5–13`).
- Owner-bound filter refuses unattributed callers for non-ClientEntryPoint interfaces (`OwnerBoundCallFilter.cs:61–67`, `124–135`).

### Surface audit (SDK Mcp folder)

| Surface | Secrets? | Notes |
|---------|----------|-------|
| `IMcpAuthorization.Begin` → `AuthorizationRequired` | **No** code/verifier | CommandId, Server*, SignInUrl, State, Actor (`AuthorizationFacts.cs:9–15`) |
| `IMcpAuthorization.Claim` → `McpAuthorizationClaim` | **No** | Kind + Required/Denied only (`McpAuthorizationVocabulary.cs:48–51`). Open Required carries State (see residual). |
| `IMcpAuthorization.DeliverCallback` → `McpAuthorizationCallbackDelivery` | **No** return secrets | Bool triple only (`:33–36`). Code is **input** from the OAuth callback host. |
| `IMcpAuthorization.BindCompletionTarget` | **No** | void |
| `IMcp` / `CallMcpTool` → `McpToolReturned` | **No** OAuth secrets | Tool content + Actor + IntegrationSubject (`IMcpServer.cs:46–52`) |
| `ListMcpTools` → `McpToolsListed` | **No** | Catalog only |
| `IMcpAuthorizationCodes.TakeCompletedCode` → `McpAuthorizationCodeResult` | **Yes** (host-only) | Code + CodeVerifier (`:40–44`). Not ClientEntryPoint. Callers: rail, OAuth callback, Gmail rail — grain `GetGrain` paths. |
| Auth SSE `AuthorizationEvent` | **No** code/verifier | Sequence, Kind, CommandId, Server*, SignInUrl, State (`HttpSurfaceModels.cs:39–47`) |
| Journal facts `Authorization{Required,Completed,Denied}` | **No** code/verifier | Same shape as before |
| In-process `McpAuthorizationCodeHub` Completions | Plaintext until consumed | Silo-local, not ClientEntryPoint (prior MINOR) |

Pin: `ClientAuthorizationContractExposesNoCodeOrVerifierSurface` (`OAuthBoundaryProofs.cs:18–42`) asserts method set, no `McpAuthorizationCodeResult` on client returns, codes interface lacks `[ClientEntryPoint]`.

### Verdict on BLOCKER 2
**CLOSED.** No ClientEntryPoint method and no client-facing RequestSynapse reply in the Mcp SDK returns authorization code or PKCE verifier.

---

## BLOCKER 3 re-attack — forged Actor / token slot trust

### Original defect
`CallMcpTool.Actor` was client/model JSON; `McpServerNeuron.AuthorizedAsync` keyed tokens from that claim.

### Fix under test
| Layer | Behavior |
|-------|----------|
| Model bind | `BindModelArguments` strips any `actor` key case-insensitively (`SynapseCapabilityTool.cs:36–40`) |
| Host stamp | `StampVerifiedActor` overwrites any `Actor` property from verified principal (`:74–100`) |
| Fire path | `SystemTools.FireCoreAsync` bind then stamp with constructor-captured `verifiedActor` (`SystemTools.cs:186–189`) |
| Ambient hop | `VerifiedActor` uses Orleans `RequestContext` (`VerifiedActor.cs:6–47`); `Chat.SendStreaming` `Enter`s with `SendMessage.Actor` (`Chat.cs:65–73`); `Agent.ResolveTools` passes `VerifiedActor.Current` into `SystemTools` (`Agent.cs:97`) |
| Live MCP | Missing actor still settled-refuses (`McpServerNeuron.cs:241–246`); tokens keyed by `SubjectKey(actor)` (`:248–249`) |

### Attack A — model fire path
Pin `ModelBoundActorIsStrippedAndVerifiedActorIsStamped` + `ForgedActorThroughFirePathIsReplacedByVerifiedPrincipal` (`OAuthBoundaryProofs.cs:45–157`): forged bob actor stripped; stamped alice lands on inbound `CallMcpTool` journal at the MCP grain.

If `verifiedActor` is null after strip, Actor stays null → live path refuses missing actor (cannot silently fall into another user’s slot).

### Attack B — crafted synapse from non-session / non-model path
| Origin | Can forge Actor reach MCP tokens? | Classification |
|--------|-----------------------------------|----------------|
| HTTP `MapOwnerCommands` chat send | **No.** Stamps `HttpActor` into `SendMessage` (`MapOwnerCommands.cs:33–37, 163–164`). No arbitrary synapse fire. | Multi-user remote surface — closed |
| Assistant `fire` tool | **No.** Strip + stamp (Attack A). | Closed |
| `IDigitalBrain` / `ISessionNeuron.Fire` cluster client with `CallMcpTool{Actor=…}` | **Yes.** Session forwards synapse as-is (`SessionNeuron.cs:7–15`). | **Owner-trusted scripting domain** (GREEN-b documented; ratified scripting stance) |
| Arbitrary domain neuron `SendAsync` of a hand-built `CallMcpTool` | Only if silo code constructs it — no remote untrusted constructor path found in UI/timer/button modules. | Not a remote principal attack |
| Direct `IAgent.RespondStreaming` without Chat Enter | Tools built with `VerifiedActor.Current == null` → stamp no-op → null Actor → MCP refuses. Forged model actor still stripped. | No token slot hit |

Nothing **other than** the owner-trusted cluster/scripting domain (session `Fire` / `IDigitalBrain`) gets a forged Actor through to live token resolution for multi-user HTTP + model fire.

### Verdict on BLOCKER 3
**CLOSED** for the ratified remote/model attack surface. Residual trust root is intentional owner scripting + cluster session Fire (documented).

---

## BLOCKER 4 (one-shot after interface split) re-attack

### What “one-shot” must still mean
1. Second `DeliverCallback` on a Completed/Consumed state refused.
2. Host `TakeCompletedCode` clears durable code; second take yields null.
3. Client has no Take surface (so one-shot cannot be raced by a ClientEntryPoint consumer).

### Evidence
| Check | Location | Result |
|-------|----------|--------|
| Deliver one-shot | `McpAuthorizationNeuron.cs:236–239` | Non-Open or Consumed → `Accepted: false` |
| Take one-shot | `:345–362` | Sets `Consumed = true`, `Code = null`, persists |
| Client no Take | `IMcpAuthorization.cs` + composition pin | Closed |
| Pins | `CompletedAuthorizationCodeIsOneShotAndReplayIsRefused` (`OAuthRailProofs.cs:171–213`); `CompletedAuthorizationCodeRemainsOneShotWithoutClientTake` (`OAuthBoundaryProofs.cs:217–256`) | Second Deliver refused; Claim shows Completed |
| Rail / callback consumers | `McpAuthorizationRail.cs:139`; `McpOAuthCallback.cs:71,104,134`; `GmailAuthRail.cs:189` | All use `IMcpAuthorizationCodes` |

Hub still one-shots Completions via `TryRemove` on await (`McpAuthorizationCodeHub.cs:113–116, 137`) — silo-local.

### Verdict on one-shot
**CLOSED / survived.** Interface split moved Take off the client surface without relaxing Deliver or durable consume semantics.

---

## Spot-check — VerifiedActor / RequestContext concurrent bleed

| Concern | Assessment |
|---------|------------|
| Propagation mechanism | Orleans `RequestContext` keys `db.verified-actor.principal` / `.username` (`VerifiedActor.cs:11–12, 18–26, 42–43`) — chosen so Chat → Agent grain calls hop (AsyncLocal would not). |
| Enter / restore | `Enter` returns `Restore` that puts previous values back on dispose (`:49–74`). Chat wraps the full respond loop in `using` (`Chat.cs:65–73`). |
| Tool isolation | `SystemTools` **captures** `verifiedActor` at construction (`Agent.cs:97`); fire stamps that snapshot (`SystemTools.cs:189`), **not** a re-read of ambient `Current` at tool-invoke time. Even if `TurnBoundFunction` schedules work (`TurnBoundFunction.cs:14–19`) and ambient context is thin, the stamp uses the turn-bound snapshot. |
| Concurrent two chats | Neurons are single-threaded (kernel trap). Distinct principal-scoped chat grains (`MapOwnerCommands` / principal instance names) each Enter their own actor when calling the agent. Shared agent activations process turns sequentially — no concurrent interleave of two Enters on one activation. |
| Bleed residual | No code path found that leaves Alice’s principal in RequestContext after dispose for Bob’s subsequent turn on the same agent activation. Pin `ChatTurnVerifiedActorPropagatesToAgentAcrossGrainCall` proves the hop; no concurrent-bleed pin exists (would be nice-to-have, not a reopen). |

**Bleed judgment:** No VerifiedActor cross-turn bleed found under the concurrency model.

---

## Backlog from GRILL-1 still open (not re-BLOCKERs for this re-grill)

| # | Severity | Item |
|---|----------|------|
| Claim residual | **MAJOR** | `Claim(CommandId)` principal-blind; Open returns State (`McpAuthorizationNeuron.cs:303–311`). GREEN-b backlog. |
| G1-4 | MAJOR | PKCE still optional at Begin (`McpAuthorizationVocabulary.cs:14–15`); Gmail shared non-PKCE mint until S1.6. |
| G1-5 | MAJOR | Isolation/destructive pins still weak as security e2e (purpose strings + `IMcpToolTransport` bypass at `McpServerNeuron.cs:203–207`). |
| G1-6 | MAJOR | Failed MCP calls lack integration-bearing audit outcome fact. |
| G1-7 | MINOR | Code hub Completions soft-cap under waiters. |
| G1-8 | MINOR | `[Description]` on auth facts. |
| G1-9 | MINOR | Composition PKCE pins are source greps. |
| SendMessage trust | MINOR/soft | Cluster `IChat.Send(SendMessage{Actor})` trusts the actor field; HTTP host stamps cookie principal. Same owner-scripting class as session Fire — not multi-user HTTP. |

---

## Findings index (this re-grill only)

| # | Severity | One-line |
|---|----------|----------|
| R1 | **MAJOR residual** | `Claim` still principal-blind; Open returns State (chain to secrets broken by Take removal) — `McpAuthorizationNeuron.cs:275–311` |
| R2 | MINOR residual | Null Actor on Begin throws `ArgumentNullException` not settled refuse — `:50` |
| R3 | MINOR residual | No concurrent VerifiedActor bleed pin (code review only) |

**None of R1–R3 reopen BLOCKERs 1–3 or one-shot.**

---

## Verdict rationale

GREEN-b closed the three attack chains that made P0-5 fail:

1. Begin-by-CommandId no longer recovers another principal’s pending (incl. empty/case/workspace variants where applicable).
2. Code + verifier never leave host-only `IMcpAuthorizationCodes`; ClientEntryPoint + Mcp RequestSynapse replies are clean.
3. Model fire strips forged Actor and stamps verified principal; multi-user HTTP stamps cookie actor; only owner-trusted session/scripting Fire still accepts synapse Actor by design.
4. One-shot Deliver + durable Take survived the interface split; client can no longer race Take.

Gate: **121/121** green this session. Remaining MAJOR items are backlog (Claim, PKCE store, pin quality, failure audit) — not the four rejected BLOCKERs.

VERDICT: APPROVE
