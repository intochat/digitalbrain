# Seam 4 — B3 audit (no silent credential fallback / no publish credential transfer)

**Date:** 2026-08-12 (PT) · **Tip:** `complete-refactoring` @ parent of this commit  
**Slice:** B3 only · LOCAL · FREEZE untouched (`AppHost.cs` / `AIHostingExtensions` / `appsettings.Development.json`)  
**Bound to:** `plans/SEAM-4-ACCEPTANCE.md` §B3 · inventory `plans/SEAM-4-INVENTORY.md`

## Verdict

**CLEAN (no concrete hole).** Tip refuses missing actor and placeholder app credentials; OAuth user tokens live only in Sdk `PrincipalTokenSlot` keyed by `PrincipalId`; Library publish/install copies structure members into principal registry — never OAuth envelopes across principals. No code fix landed.

Silent **refresh_token** grant in `McpAuthorizationRail.TryRefreshAsync` is same-principal OAuth refresh, not cross-principal / shared / demo credential fallback.

## Grep evidence (reproducible)

```bash
# Silent / demo / operator / placeholder credential language
rg -n -t cs -i 'fallback|demo.?token|operator.?token|shared.?token|DefaultToken|AnonymousToken|placeholder|silent' \
  src/Kernel/DigitalBrain.Sdk src/Modules -g '!**/obj/**' -g '!**/bin/**'

# Token storage / slot construction
rg -n -t cs 'PrincipalTokenSlot|DurableMcpTokenCache|mcp.gateway.oauth.principals|SubjectKey|TryReadTokens|StoreAsync' \
  src -g '!**/obj/**' -g '!**/bin/**'

# Module-owned token / PKCE reimplementation
rg -n -t cs 'TokenSlot|OAuthPkce|CodeVerifier|ProtectedToken|WriteTokens|SaveToken|ITokenCache' \
  src/Modules -g '!**/obj/**' -g '!**/bin/**'

# Google / Salesforce credential surface
rg -n -t cs -i 'oauth|token|pkce|credential|client.?secret|refresh_token' \
  src/Modules/Google src/Modules/SalesForce -g '!**/obj/**' -g '!**/bin/**'

# Publish / share / library vs tokens
rg -n -t cs -i 'publish|share|StructureJson|access_token|refresh_token|ProtectedToken|Bearer' \
  src/Modules/Library src/Kernel/DigitalBrain.Abstractions/Library \
  src/Kernel/DigitalBrain.Sdk/Mcp -g '!**/obj/**' -g '!**/bin/**'

# Workspace-scope integration (enum surface)
rg -n -t cs 'IntegrationScope\.Workspace|WorkspaceIntegration' src -g '!**/obj/**' -g '!**/bin/**'
```

### Hits that matter

| Check | Result |
|---|---|
| Placeholder app creds | `McpOAuthOptions.RejectPlaceholder` refuses `local-dev` / `local-dev-secret` / `http://localhost/oauth/callback` |
| Missing actor | `McpServerNeuron.AuthorizedAsync` throws — no anonymous / shared slot |
| Slot key | `McpTokenPresence.SubjectKey` = `PrincipalId` `"N"`; sole `new PrincipalTokenSlot` in `McpServerNeuron` |
| Token dictionary | Only `mcp.gateway.oauth.principals` on `McpServerNeuron` |
| Same-principal refresh | `TryRefreshAsync` reads/writes the **same** `PrincipalTokenSlot` |
| PKCE join | `McpAuthorizationNeuron` refuses wrong `PrincipalId` on command/slot recovery |
| Modules Google/SF | `McpServerDefinition` + Aspire `OAuthProviderHosting.Register` only — no token store / PKCE mint |
| Module TokenSlot/PKCE | empty under `src/Modules` (Salesforce Aspire text mentions PKCE in docs string only) |
| Library publish | Persists `StructureJson` + publisher id; no OAuth fields |
| Library install | `ParseMembers` → `RegisterInstance` / `SetInstanceEnabled` on installer principal registry — no `PrincipalTokenSlot` write |
| `DurableMcpTokenCache(` call sites | Only `PrincipalTokenSlot` overloads used (`McpTokenPresence.StoreAsync`, `McpClientSessions`) |
| `IntegrationScope.Workspace` | Enum exists; **no** `WorkspaceIntegration` / `.Workspace` call path under tip `src` |

## Residual list (honesty — not faked green)

1. **B4 residual (do not invent Conversation extract):** `McpAuthorizationNeuron.ResolvePrincipalChat` still parks to `NeuronId("chat", …, PrincipalPartition… "main")` when the requester is not already a chat. Listed in acceptance/inventory; out of B3 scope.
2. **Dead dual API (not a live hole):** `DurableMcpTokenCache(IDurableValue<byte[]>)` and `McpTokenPresence.IsMissingOrExpired(IDurableValue<byte[]>)` remain; tip call sites use `PrincipalTokenSlot` only. Optional later delete if Eng Desk wants API honesty.
3. **`IntegrationScope.Workspace` unused:** enum surface only; no workspace-shared token rail. Keep unused until a product scope exists — do not wire a shared fallback.
4. **Library `StructureJson` opacity:** publish stores opaque JSON; install only reads `members[]` grainType/localName/role. Tip does **not** copy Sdk OAuth envelopes. Residual: no scrubber if a publisher stuffs credential-shaped strings into StructureJson content (user-content abuse, not PrincipalTokenSlot transfer).
5. **B2 residual (inventory):** plaintext `CodeVerifier` on `BeginMcpAuthorization` synapse surface — protect-at-rest already on pending state; journal confirm is a separate slice.
6. **FREEZE / non-goals:** AppHost / AIHostingExtensions / appsettings.Development.json untouched; no CloudAgent; no push; no Conversation extract “fix”.

## Eng Desk one-liner

`B3 CLEAN @ <this SHA>: no silent/shared credential fallback; Library publish/install does not transfer PrincipalTokenSlot OAuth; B4 chat+main residual unchanged.`
