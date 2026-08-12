# DigitalBrain.Sdk — ownership (Seam 4)

**Owner:** Integrations (platform rails)  
**Folder:** `src/Kernel/DigitalBrain.Sdk/`  
**Assembly / RootNamespace (transitional):** `DigitalBrain.Modules.Sdk`  
**Rename:** non-goal this seam — folder is the ownership claim; AssemblyName stays until a dedicated rename PR.

## Owns (rails)

| Rail | Tip symbols |
|---|---|
| Outbound MCP list+call | `Mcp/McpServerNeuron` (`[GrainType("mcp")]`), `ListMcp*`, `CallMcpTool` |
| OAuth/PKCE + slots (R10) | `McpAuthorization*`, `OAuthPkce`, `DurableMcpTokenCache`, `PrincipalTokenSlot` |
| Webhook ingress | `Webhook/WebhookIngressNeuron` + `VerifiedWebhookDeliveryReceived` → Accepted\|Duplicate\|Conflict |
| Durable payload protection | `Protection/*` (`DigitalBrain:Security:StateProtectionKey`) |

## Does not own

- Kernel host `MapOAuthCallback` (one-shot HTTP mint edge only) — `DigitalBrain.Kernel`
- Actor / `HttpActor` mint — Kernel host (Seam 1)
- Product MCP server definitions — Modules (Google/Salesforce register `McpServerDefinition` only)
- Aspire OAuth parameter wiring — `DigitalBrain.Aspire.Hosting` (`OAuthProviderHosting`)
- Core interconnect (Neuron/Journal/Outbox/`ISynapseGraph`) — Core

## Module contract

Modules **call** these rails. They must not invent a second OAuth/PKCE, token slot, MCP client, or webhook ingress stack.

## Bound to

`plans/SEAM-4-ACCEPTANCE.md` · `plans/SEAM-4-INVENTORY.md` · `plans/SEAM-4-PLAN.md` · `plans/SEAMS-2-4-ROUTING.md` Seam 4
