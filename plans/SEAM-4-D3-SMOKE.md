# Seam 4 D3 — Product Grill smoke (rails ownership + PrincipalTokenSlot)

**Tip:** `3778fa40` · D1 PASS · AppHost CANONICAL left alone (Seam 1 already live).  
**Bar:** GREEN≠GRILL — prove rails ownership + slot unify, do not re-litigate Seam 1 cookie cases.

## Ownership honesty (A)

| Rail | Location | Parallel in modules? |
|---|---|---|
| McpServerNeuron list/call | `src/Kernel/DigitalBrain.Sdk/Mcp/McpServerNeuron.cs` | No module reimplementation found |
| OAuth/PKCE + PrincipalTokenSlot | `DigitalBrain.Sdk/Mcp/{McpAuthorizationRail,PrincipalTokenSlot,DurableMcpTokenCache}.cs` | No module-local TokenStore |
| WebhookIngressNeuron | `src/Kernel/DigitalBrain.Sdk/Webhook/WebhookIngressNeuron.cs` | No parallel webhook stack in Google/SF |
| MapOAuthCallback mint | `src/Kernel/DigitalBrain.Kernel/MapOAuthCallback.cs` (Kernel only) | Integrations does not expand Kernel |

## PrincipalTokenSlot unify (B / R10)

- Slot key: `(serverKey, PrincipalId)` via `PrincipalTokenSlot` + durable dict
- Protect-on-write: `IDurablePayloadProtector` in `DurableMcpTokenCache` / `McpTokenPresence` (B2 tip-confirmed)
- Silent fallback: none on audited Sdk/Google/SF paths (B3 tip-clean @ `1acad9b0` / `5ce928c6`)
- B4 residual **listed not faked**: `ResolvePrincipalChat` → chat + `"main"`

## Webhook Emit shape (C)

- Per-subscription identity; emits `Accepted` / `Duplicate` / `Conflict` (inventory tip-true)

## Gate (D1)

```
dotnet build DigitalBrain.slnx -c Debug -warnaserror -p:RuntimeIdentifier= -p:SelfContained=false
→ Build succeeded. 0 Warning(s). 0 Error(s).
```

## Product Grill ask

Concur or catch on:
1. Ownership table above (no parallel OAuth/webhook in modules)
2. B3/B2 audits tip-clean
3. B4 residual honesty (Conversation extract, not Integrations fake-close)
4. D1 green with AI RID+IDE0004 gate fixes only (FREEZE untouched)

Live cookie/MCP path = Seam 1 (already PASS). Do not require AppHost restart for this smoke.
