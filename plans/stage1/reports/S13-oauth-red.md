# S1.3-RED — OAuth/MCP authorization rail characterization report

## What changed
- `src/Tests/DigitalBrain.Tests/OAuthRailProofs.cs` — new characterization pins for P0-1 (dual state, no PKCE, unbounded Completions, code replay), P0-5 (token keying by neuron identity + callback ignores principal), and supporting shapes.
- `src/Tests/DigitalBrain.Tests/McpGatewayProofs.cs` — added `// PIN-DEFECT(P0-7)` on the existing destructive-tool refusal pin (behavior unchanged).
- `src/Modules/SDK/DigitalBrain.Modules.Sdk/DigitalBrain.Modules.Sdk.csproj` — `InternalsVisibleTo DigitalBrain.Tests` (test seam only; no behavior change).
- `src/Modules/SDK/DigitalBrain.Modules.Sdk/Mcp/McpAuthorizationCodeHub.cs` — `CompletionsCountForTests` characterization seam (exposes count of the existing static Completions dictionary; no behavior change).

## Tests
Added (all green characterization pins):

| Pin | Test | Marker |
|-----|------|--------|
| P0-1 dual state | `OAuthRailCompositionProofs.ManualRailAndLibraryCallbackMintSeparateOAuthStates` | `// PIN-DEFECT(P0-1)` |
| P0-1 no PKCE | `OAuthRailCompositionProofs.ManuallyBuiltSalesforceAuthorizeUrlCarriesNoPkceChallenge` | `// PIN-DEFECT(P0-1)` |
| P0-1 unbounded Completions | `OAuthRailCompositionProofs.CodeHubCompletionsAccumulateUnknownStatesWithoutExpiryOrEviction` | `// PIN-DEFECT(P0-1)` |
| P0-1 unknown state → Completions | `OAuthRailNeuronProofs.UnknownCallbackStatesAreRejectedAtTheGrainButStillFillTheCodeHub` | `// PIN-DEFECT(P0-1)` |
| P0-1 code replay | `OAuthRailNeuronProofs.CompletedAuthorizationCodeCanBeReplayedWithoutRefusal` | `// PIN-DEFECT(P0-1)` |
| P0-5 token keying | `OAuthRailCompositionProofs.McpAndGmailTokenPurposesKeyByServerAndNeuronIdentityNotPrincipal` | `// PIN-DEFECT(P0-5)` |
| P0-5 callback no principal | `OAuthRailCompositionProofs.OAuthCallbackDeliveryIgnoresAuthenticatedPrincipal` | `// PIN-DEFECT(P0-5)` |
| P0-5 pending fact shape | `OAuthRailNeuronProofs.AuthorizationPendingShapeHasNoLocalUserBinding` | `// PIN-DEFECT(P0-5)` |
| P0-7 destructive refuse | `McpGatewayProofs.DestructiveToolsRefuseWithoutTheApprovalFlow` | `// PIN-DEFECT(P0-7)` (pre-existing behavioral pin; marker added) |

### Exact locations pinned (today)

| Concern | Location | Today’s behavior |
|---------|----------|------------------|
| Manual state mint | `McpAuthorizationRail.BeginNewAsync` | `state = Guid.NewGuid().ToString("N")` → `SignInUrl` → `Begin` |
| Library state mint | `McpOAuthCallback.AuthorizeAsync` | `state` from `context.AuthorizationUri` query (MCP client library) → `RegisterSession` + `Begin` |
| No PKCE | `McpAuthorizationRail.SignInUrl` (Salesforce branch) | `response_type=code` + client_id/redirect/scope/state only — no `code_challenge` |
| Unbounded Completions | `McpAuthorizationCodeHub.Completions` static `ConcurrentDictionary` | `Complete` always writes; no TTL/eviction except `AwaitAsync` consume / `ResetForTests` |
| Replay | `McpAuthorizationNeuron.DeliverCallback` when Outcome is Completed | Re-`Complete`s hub with stored code; returns `Accepted: true, Completed: true` |
| MCP token purpose | `McpTokenPresence.Purpose` | `mcp/oauth/{serverKey}/{durableIdentity}` where durableIdentity = `NeuronId.ToString()` (`mcp:owner/serverKey`) |
| Gmail token purpose | `DurableGoogleTokenStore.Purpose` | `google/oauth/{connectionName}/{durableIdentity}`; Gmail sets `_durableIdentity = Id.ToString()`, `_userKey = Id.Name` |
| Pending record | `PendingAuthorization` | CommandId, ServerKey, ServerDisplayName, SignInUrl, State, Outcome, Code, Iss, CompletionTarget, CompletionNotified, RequestingNeuron — **no** Principal/User/Actor |
| Callback HTTP | `MapOAuthCallback` | `AllowAnonymous`; parameters `state/code/error/iss` only; no ClaimsPrincipal / HttpActor |
| Delivery record | `DeliverMcpAuthorizationCallback` | State, Code, Error, Iss only |
| Destructive refuse | `McpServerNeuron.HandleAsync(CallMcpTool)` | `tool.Destructive` → `NeuronAuthorizationException` (settled) |

### Dual-state note
Full end-to-end dual-state values (rail Guid vs library Guid on one live OAuth round-trip) are **not** exercised against a real MCP OAuth provider (out of scope / no aspire). RED pins the two mint sites and that they are different flows (rail synthesizes Guid; callback consumes library URI state). GREEN can flip these when the rail is unified.

Flipped pins: none (RED only).

## Gate
```
dotnet build DigitalBrain.slnx
Build succeeded.
    0 Error(s)
(Time Elapsed ~4s; node NO_COLOR noise from CodeGraph AppHost target is not a C# warning)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 114, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 41.329s
```

## Conflicts & risks
- Dual-state **values** on a live MCP OAuth exchange are source-characterized (two mint sites), not proven equal/unequal from one instrumented session — would need a fake OAuth provider or library hook. GREEN should replace dual mint with one flow and flip the pins.
- `McpAuthorizationCodeHub.CompletionsCountForTests` is a production-assembly test seam (alongside existing `ResetForTests`). Safe: read-only count property.
- `InternalsVisibleTo` is a production-project edit but not a behavior change (allowed RED test seam per GROK).
- Source-inventory pins that `ReadRepoFile` from the test base directory will fail if the test exe is ever run without the source tree present (same stance as S1.2-RED).
- `_pending` durable dictionary on `McpAuthorizationNeuron` also never evicts completed entries (enables replay). Completions static dict is the "unbounded static dictionary" called out in the defect; durable pending is the durable half of the same P0-1.

## Out of scope
- No production OAuth/MCP behavior changes beyond InternalsVisibleTo + CompletionsCountForTests.
- No S1.2 auth changes, no principal-bound token storage, no PKCE implementation, no destructive-tool allow-all (GREEN).
- No new NuGet packages, no git.
- No aspire / AppHost run.
