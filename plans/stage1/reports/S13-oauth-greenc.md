# S1.3-GREEN-c — OAuth residuals R1–R3 (grill2) report

## What changed

| File | Change |
|------|--------|
| `IMcpAuthorization.cs` | `Claim(CommandId, ActorContext actor, …)` — principal-bound status surface |
| `McpAuthorizationNeuron.cs` | Begin: null Actor → settled `NeuronAuthorizationException` (not `ArgumentNullException`). Claim: require Actor; wrong/missing principal / unknown / expired → same settled `"… is not pending."` (no State/username leak); matching principal returns status as before |
| `McpAuthorizationRail.cs` | Passes rail `actor` into Claim; treats settled not-pending as “no claim” |
| `McpOAuthCallback.cs` | Requires session Actor; Claim + IsDenied use it; host path keeps working |
| `GmailAuthRail.cs` | Claims with the same stable principal used at Begin |
| `ScriptedAgent.cs` | Per-turn `ObservedVerifiedActorTurns` bag for concurrent multi-principal pins |
| `OAuthBoundaryProofs.cs` / `OAuthRailProofs.cs` | New pins + Claim call sites pass actor |

Bind remains binder-neuron-gated (`RequireAuthorizedCompletionTargetBinder`); DeliverCallback is OAuth-host state delivery (not a principal status poll). No other ClientEntryPoint status verb returned another principal’s pending State.

## Tests

| Residual | Test | Assertion |
|----------|------|-----------|
| R1 | `CrossPrincipalClaimByStolenCommandIdRefusesLikeUnknown` | Bob + Alice’s CommandId → settled refuse, no state/username leak; Alice Claim → Required + State |
| R2 | `NullActorOnBeginAndClaimRefusesSettled` | null Actor on Begin and Claim → `NeuronAuthorizationException`, not `ArgumentNullException` |
| R3 | `ConcurrentChatTurnsDoNotBleedVerifiedActorAcrossRequestContext` | Two concurrent chat Sends (alice/bob) → shared ScriptedAgent sees both principals; no null bleed |
| regression | existing Claim one-shot pins | Call with minting actor |

## Gate

```
dotnet build DigitalBrain.slnx
Build succeeded.
    0 Error(s)
(node NO_COLOR AppHost noise only — not C# warnings)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 124, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 40.990s
```

## Conflicts & risks

- ClientEntryPoint `Claim` now requires an Actor argument. Callers that only held a stolen CommandId cannot poll Open State; intentional. Host rails already know the actor from the MCP call / Gmail userKey / OAuth session.
- Unknown/expired Claim now settles as `NeuronAuthorizationException` (was `InvalidOperationException`) so delivery does not retry; rails catch the settled type for “no pending claim”.
- Bind was not re-shaped with an Actor parameter — already refuses non-requesting binders. Scope stayed on Claim residual.

## Out of scope

- PKCE store optionality / Gmail typed-path (G1-4, S1.6)
- Isolation/destructive pin quality (G1-5)
- Failed MCP audit outcomes (G1-6)
- Code hub soft-cap, Description attributes, composition greps
- No git; no aspire/AppHost run; no new packages
