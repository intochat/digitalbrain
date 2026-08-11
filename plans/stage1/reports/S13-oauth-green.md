# S1.3-GREEN — OAuth/Integration rail report

## What changed

### Contracts / abstractions
- `src/Kernel/DigitalBrain.Abstractions/Integrations/Integration.cs` — ratified `Integration` + `IntegrationScope` (`User`|`Workspace`).
- `src/Modules/SDK/DigitalBrain.Modules.Sdk/Mcp/McpAuthorizationVocabulary.cs` — `BeginMcpAuthorization` gains required `Actor` + optional PKCE fields; `McpAuthorizationCodeResult` carries `CodeVerifier` + `Actor`.
- `src/Modules/SDK/DigitalBrain.Modules.Sdk/Mcp/AuthorizationFacts.cs` — `AuthorizationRequired` / `Completed` / `Denied` stamp optional `Actor`.
- `src/Modules/SDK/DigitalBrain.Modules.Sdk/Mcp/IMcpServer.cs` — `ListMcpTools` / `CallMcpTool` additive `Actor`; `McpToolReturned` stamps `Actor` + `IntegrationSubject` for journal audit.

### Rail (P0-1 dual state + PKCE)
- `McpAuthorizationRail.cs` — **sole state mint**: `OAuthPkce.CreateS256Pair` + `BuildPkceAuthorizeUrl` (S256 always). Manual non-PKCE Salesforce URL path deleted. On `Claim.Completed`, takes the one-shot code + verifier, exchanges tokens (HTTP or `IMcpTokenExchanger` test seam), stores under principal-scoped purpose — so the MCP client library never opens a second authorization transaction when tokens are present.
- `OAuthPkce.cs` — S256 verifier/challenge helper (no new packages).
- `McpTokenExchange.cs` — authorization-code + `code_verifier` exchange against configured / Salesforce default token endpoint.
- `McpOAuthCallback.cs` — **does not mint state** from `AuthorizationUri`. Recovers the rail-minted transaction via `Claim(commandId)` and awaits that state only.

### Bounded one-shot state (P0-1 store)
- `McpAuthorizationNeuron.cs` — pending records bind `Actor`, `CodeChallenge`, protected `CodeVerifier`, `ExpiresAt` (15m TTL), `Consumed`. Capacity cap `MaxPendingStates = 64`. Mutation-time expiry sweep. Unknown callback states refuse **without** writing the static hub. Completed states refuse a second `DeliverCallback`. `TakeCompletedCode` is one-shot (clears code, sets `Consumed`).
- `McpAuthorizationCodeHub.cs` — `Complete` drops orphans (no waiter/session); Completions bounded (max 64) with eviction of non-waiter entries.

### Principal-keyed tokens (P0-5)
- `McpTokenPresence.cs` — purpose shape `integration/{scope}/{provider}/{subjectId}` via `UserIntegration` / `Purpose`. Subject key = principal `N` format.
- `PrincipalTokenSlot.cs` + `DurableMcpTokenCache.cs` — per-principal durable dictionary slot (`mcp.gateway.oauth.principals`) replaces neuron-identity `IDurableValue`.
- `McpServerNeuron.cs` — resolves tokens strictly for `synapse.Actor`; missing actor on live MCP path is a settled refusal with the sign-in fix path. No silent fallback across principals.
- `McpClientSessions.cs` / `McpOAuthSession.cs` — thread `ActorContext` into the session purpose.

### Destructive tools (P0-7)
- `McpServerNeuron.HandleAsync(CallMcpTool)` — removed the destructive-tool blanket rejection. Catalog still reports `Destructive` hints; execution is allowed. Audit: inbound `CallMcpTool.Actor` + outbound `McpToolReturned{Actor, IntegrationSubject}`.

### Gmail (minimal compatibility only)
- `GmailAuthRail.cs` — passes a stable `ActorContext` into `Begin` (SHA-256 of grain user key). Typed Gmail path / neuron-keyed Google tokens remain until S1.6; not migrated here.

### Tests (RED pins flipped)
- `OAuthRailProofs.cs` — all `// PIN-DEFECT(P0-1|P0-5)` markers removed; assertions target ratified behavior (sole mint, PKCE, no orphan Completions, principal purpose, one-shot replay refuse, principal on pending, A/B isolation).
- `McpGatewayProofs.cs` — P0-7 pin flipped: destructive tool returns `McpToolReturned` with actor/subject stamps.
- `ChatSignInOfferProofs.cs` — `Begin` call updated for `Actor` + PKCE fields.

## Design note

**Single mint + token-before-library.** The dual-state defect existed because the rail pre-minted a non-PKCE human URL (state A) while the MCP client library later minted a second PKCE URL (state B). GREEN makes the rail the only mint: PKCE S256 is always on the authorize URL, the pending record is durable on `McpAuthorizationNeuron` (not a static dictionary), and on Completed the rail exchanges the code with the stored verifier and writes principal-scoped tokens. `CreateAsync` then finds tokens in `DurableMcpTokenCache` and never opens library OAuth — so there is no second state. The library callback remains only as a recovery waiter for an already-minted rail transaction; it explicitly refuses to mint from `AuthorizationUri`.

**Principal binding.** `ActorContext` (S1.2) is required on `Begin` and on live `db.mcp.*` calls. Pending state is bound at mint time to `{workspace owner grain, principal, provider/server key}`. `/oauth/callback` stays `AllowAnonymous` (provider redirect) and only correlates `state` → pending record; completion identity is the bound actor, not the HTTP principal. Tokens are keyed `integration/user/{provider}/{principal:N}` under the protector purpose — user A cannot read user B’s envelope.

**Migration honesty.** Pre-production install: old neuron-identity token keys are **not** migrated. Delete-and-reauthorize is the ratified Stage 1 stance. Gmail typed-path token keying is intentionally untouched (S1.6).

## Tests

| Former pin | Flipped test | New assertion |
|------------|--------------|---------------|
| P0-1 dual state | `AuthorizationRailIsTheSoleStateMintAndLibraryCallbackDoesNotMint` | Rail mints PKCE; callback does not mint from URI |
| P0-1 no PKCE | `SalesforceAuthorizeUrlAlwaysCarriesPkceS256` | `code_challenge` + `S256` always |
| P0-1 unbounded Completions | `CodeHubDropsUnknownStatesInsteadOfAccumulatingOrphans` | orphan Complete → count stays 0 |
| P0-1 unknown → hub | `UnknownCallbackStatesAreRejectedAndDoNotFillTheCodeHub` | refuse, Completions unchanged |
| P0-1 replay | `CompletedAuthorizationCodeIsOneShotAndReplayIsRefused` | second Deliver refused; second Take null |
| P0-5 token keying | `McpTokenPurposesKeyByPrincipalNotNeuronIdentity` | `integration/user/.../principal` |
| P0-5 callback | `OAuthCallbackStaysAnonymousAtHttpAndResolvesOnlyThroughStateBinding` | HTTP anonymous; pending has Actor |
| P0-5 pending shape | `AuthorizationPendingBindsTheLocalUserPrincipal` | Begin emits Actor on Required |
| P0-5 isolation (new) | `PrincipalTokenSlotsIsolateUserAFromUserB` | distinct purposes; same-state cross-principal Begin throws |
| P0-7 destructive | `DestructiveToolsAreCallableWhenAuthorized` | `McpToolReturned` with actor/subject |

## Gate

```
dotnet build DigitalBrain.slnx
Build succeeded.
    0 Error(s)
(node NO_COLOR AppHost noise only — not C# warnings)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 115, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 41.421s
```

## Conflicts & risks
- Token exchange against a real provider still needs `ClientId`/`ClientSecret`/`RedirectUri` (+ optional `TokenEndpoint`). Fake transport tests bypass OAuth; live MCP without tokens throws `McpAuthorizationRequiredException` as before.
- `IMcpTokenExchanger` is the DI test seam for exchange without network; production uses `McpTokenExchange` HTTP POST.
- Idempotent `Begin` after Completed is used to recover `State` for Take/exchange — a dedicated `GetCommandState` grain method would be cleaner later but was avoided to keep the diff small.
- Gmail still stores tokens under neuron identity (`DurableGoogleTokenStore.Purpose`); only the shared authorization Begin now carries an actor stamp. Full Gmail principal binding is S1.6.
- Multi-silo callback correctness relies on durable pending on the authorization neuron (not the static hub). Hub is in-process waiters only.

## Out of scope
- Gmail typed-path deletion / Google MCP migration (S1.6).
- Chat turn pipeline (S1.5), Execution (S1.4), Flutter, CI.
- No new NuGet packages; no git; no aspire/AppHost run.
- No migration of pre-existing neuron-keyed MCP token blobs (delete-and-reauthorize).
