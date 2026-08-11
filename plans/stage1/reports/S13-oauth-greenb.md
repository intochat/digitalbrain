# S1.3-GREEN-b — close the four GRILL BLOCKERs report

## What changed

### BLOCKER 1 — Begin recovery principal-blind
- `McpAuthorizationNeuron.Begin` — idempotent CommandId recovery now requires matching `Actor.PrincipalId`. Wrong principal throws settled `NeuronAuthorizationException` with the same “not pending” shape (no state / actor leak).

### BLOCKER 2 (+ related 4 surface) — client-facing secret Take
- `IMcpAuthorization` — **removed** `TakeCompletedCode` from the `[ClientEntryPoint]` surface.
- `IMcpAuthorizationCodes` (new, **not** ClientEntryPoint) — host-only one-shot take of code+verifier; grain-to-grain via capability reification; unattributed clients refused by owner-bound filter.
- `McpAuthorizationNeuron` implements both interfaces; Take remains one-shot durable.
- `McpAuthorizationRail`, `McpOAuthCallback`, `GmailAuthRail` consume codes via `IMcpAuthorizationCodes` only.

### BLOCKER 3 — client-trusted Actor on `db.mcp.*`
- `SynapseCapabilityTool.BindModelArguments` — **strips** any model/client `actor` key before bind.
- `SynapseCapabilityTool.StampVerifiedActor` — host stamps the verified principal onto any `Actor` property.
- `SystemTools` — after bind, stamps `verifiedActor` so forged model Actor never reaches neurons.
- `VerifiedActor` (Core, **Orleans RequestContext** — not AsyncLocal) — propagates Chat → Agent grain calls; `Chat.SendStreaming` enters with `SendMessage.Actor`; `Agent`/`SystemTools` read it at fire time.
- `McpServerNeuron` still settled-refuses missing Actor on live MCP path.

### Trust boundary (origins of `db.mcp.*`)
| Origin | Actor trust |
|--------|-------------|
| HTTP host (`MapOwnerCommands` etc.) | Stamped from authenticated `HttpActor` (S1.2) → chat Actor → fire stamp |
| Assistant session `fire` tool | Stamped from `VerifiedActor` (chat turn principal); model Actor stripped |
| Direct `IDigitalBrain` / scripting cluster clients | **Owner-trusted domain** (ratified scripting stance) — Actor on the synapse is accepted as operator intent; not a remote untrusted client |

## Tests

| BLOCKER | Test | Assertion |
|---------|------|-----------|
| 1 | `CrossPrincipalBeginRecoveryByStolenCommandIdRefusesLikeUnknown` | Bob + Alice’s CommandId → settled refuse, no state leak; Alice recovers |
| 2 | `ClientAuthorizationContractExposesNoCodeOrVerifierSurface` | `IMcpAuthorization` methods = Begin/Bind/Claim/Deliver only; no `McpAuthorizationCodeResult`; codes interface not ClientEntryPoint |
| 2 | `CompletedAuthorizationCodeRemainsOneShotWithoutClientTake` + flipped `CompletedAuthorizationCodeIsOneShotAndReplayIsRefused` | Second Deliver refused; claim Completed; client has no Take |
| 3 | `ModelBoundActorIsStrippedAndVerifiedActorIsStamped` | Forged actor stripped; verified principal stamped (composition) |
| 3 | `ChatTurnVerifiedActorPropagatesToAgentAcrossGrainCall` | Chat.Send with Actor=alice → ScriptedAgent sees VerifiedActor.Current == alice (RequestContext hop) |
| 3 | `ForgedActorThroughFirePathIsReplacedByVerifiedPrincipal` | Strip+stamp+live MCP journal inbound Actor is alice, not forged |

Existing S1.3 pins remain green (OAuth rail + MCP gateway proofs).

## Gate

```
dotnet build DigitalBrain.slnx
Build succeeded.
    0 Error(s)
(node NO_COLOR AppHost noise only — not C# warnings)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 121, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 41.131s
```

## Conflicts & risks
- `IMcpAuthorizationCodes` is still an Orleans grain interface reachable in-process by same-owner grains (intended). It is **not** ClientEntryPoint; unattributed GetGrainProxy Take is refused by the owner-bound filter.
- Ambient `VerifiedActor` uses Orleans `RequestContext` (Guid + username) so it propagates across grain calls; AsyncLocal was tried and rejected by verification probe.
- Direct scripting `IDigitalBrain.FireAsync(CallMcpTool{Actor=…})` remains owner-trusted; not stamped by VerifiedActor (no chat ambient). Documented above.
- `Claim(CommandId)` remains principal-blind for Open state recovery (weaker residual after Take removal; backlog, not one of the four BLOCKERs).

## GRILL findings NOT taken (backlog)

| # | Severity | Why left |
|---|----------|----------|
| 4 | MAJOR | PKCE still optional at Begin for shared store; Gmail typed path non-PKCE until S1.6. Partial: secret Take removed from client; MCP rail still always mints PKCE. |
| 5 | MAJOR | Isolation/destructive pins still weak as security e2e (purpose strings + FakeMcpTransport). Not required to close BLOCKERs 1–3. |
| 6 | MAJOR | Failed MCP calls still lack integration-bearing audit outcome fact. |
| 7 | MINOR | Code hub Completions soft-cap under concurrent waiters. |
| 8 | MINOR | `[Description]` on auth facts. |
| 9 | MINOR | Composition PKCE pins are source greps, not provider round-trips. |

## Out of scope
- Gmail typed-path deletion / Google MCP migration (S1.6).
- Chat turn pipeline deeper than Actor ambient stamp (S1.5).
- Execution (S1.4), Flutter, CI.
- No new packages; no git; no aspire/AppHost run.
