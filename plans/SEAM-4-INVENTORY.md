# Seam 4 — tip inventory (complete-refactoring)

**Date:** 2026-08-12 PT · Tip ancestry: Seam4 docs @ `5ce928c6` + rails under `src/Kernel/DigitalBrain.Sdk` · plan `SEAM-4-PLAN.md` · folder `OWNERSHIP.md`  
**Bound to:** `plans/SEAM-4-ACCEPTANCE.md` order A → B → C → D  
**Freeze:** do not touch AppHost.cs / AIHostingExtensions / appsettings.Development.json  
**Vocab:** Neuron / Synapse / Connection / `ISynapseGraph`

## Tip rail map

| Rail | Path |
|---|---|
| MCP list/call | `src/Kernel/DigitalBrain.Sdk/Mcp/McpServerNeuron.cs` (`[GrainType("mcp")]`, instance = serverKey) |
| OAuth/PKCE | `OAuthPkce.cs`, `McpAuthorizationRail.cs`, `McpAuthorizationNeuron.cs` |
| Token slot | `PrincipalTokenSlot.cs` + `McpTokenPresence.SubjectKey` (= PrincipalId `N`) |
| Protect | `Protection/DurablePayloadProtector.cs` (`DigitalBrain:Security:StateProtectionKey`) |
| Integration descriptor | `Abstractions/Integrations/Integration.cs` (`ProtectedTokenReference` only) |
| Webhook | `Sdk/Webhook/WebhookIngressNeuron.cs` + Accepted\|Duplicate\|Conflict |
| Kernel host | `DigitalBrain.Kernel/MapOAuthCallback.cs` (+ `Program.cs`) |
| Module call-sites | Salesforce/Google: `McpServerDefinition` + Aspire `OAuthProviderHosting.Register` only |

**Folder note:** path `DigitalBrain.Sdk`, AssemblyName/RootNamespace `DigitalBrain.Modules.Sdk` — Integrations-owned rails, not Core interconnect. Rename non-goal this seam.

---

## A. Ownership honesty

| ID | Status | Evidence |
|---|---|---|
| **A1** | **DONE tip** | Sole outbound MCP neuron: `McpServerNeuron`. Modules only `AddSingleton(new McpServerDefinition(` (Salesforce/Google). No module `McpClient` stack. `DigitalBrain.Mcp` = northbound host tools, not parallel outbound client. |
| **A2** | **DONE tip** | PKCE/token/slot under Sdk/Mcp. Modules do not reimplement PKCE/callback. |
| **A3** | **DONE tip** | Webhook rail only under Sdk/Webhook. `rg Webhook` in Core + Modules → empty. |
| **A4** | **DONE tip** | `MapOAuthCallback` Kernel-only. Sdk: no `new ActorContext` / `HttpActor` mint. |

## B. Principal-keyed slots (R10)

| ID | Status | Evidence |
|---|---|---|
| **B1** | **DONE tip** | Per-server `mcp` grain + `PrincipalTokenSlot(..., SubjectKey(actor))` → `(serverKey, PrincipalId)`. Auth park slots: `McpAuthorizationNeuron.SlotKey` = `{serverKey}/{principal:N}`. `Integration` is descriptor, not dual token grain. Dead `IDurableValue` token cache/presence overloads removed (Seam 4 slice 1). |
| **B2** | **MOSTLY DONE — residual** | Durable tokens via `IDurablePayloadProtector`; pending stores `ProtectedCodeVerifier`. **Residual:** `BeginMcpAuthorization` still has plaintext `CodeVerifier` on synapse surface — confirm never journaled as secret before claiming B2 green. |
| **B3** | **DONE tip / no counterexample** | Placeholder credentials refused (`McpOAuthOptions`). No publish/share personal-credential transfer found under Sdk MCP. OAuth `refresh_token` grant ≠ cross-principal credential fallback. |
| **B4** | **RESIDUAL** | `McpAuthorizationNeuron.ResolvePrincipalChat` → `NeuronId("chat", Id.Owner, PrincipalPartition.InstanceName(actor.PrincipalId, "main"))` when requesting neuron is not chat. Conversation extract blocks clean fix — **do not fake done**. |

## C. Webhook ingress shape

| ID | Status | Evidence |
|---|---|---|
| **C1** | **DONE tip** | Handler requires `Id.Name == synapse.SubscriptionId`. |
| **C2** | **DONE tip** | `VerifiedWebhookDeliveryReceived` → digest dedupe → Emit Accepted\|Duplicate\|Conflict. |
| **C3** | **DONE tip** | No Core webhook types; no Streams/EH brain-bus substitute found. |

## D. Proof / gate

| ID | Status | Notes |
|---|---|---|
| **D1** | **PENDING** | Narrow Sdk build then `DigitalBrain.slnx -warnaserror`; no FREEZE hacks. |
| **D2** | **THIS FILE** | Ownership + grep evidence. |
| **D3** | **LATER** | Rails ownership + principal-slot smoke; not Seam 1 cookie re-litigation. |

## Reproducible greps

```bash
rg -n -t cs 'class McpServerNeuron|class PrincipalTokenSlot|class WebhookIngressNeuron|MapOAuthCallback|class McpAuthorizationNeuron' src -g '!**/obj/**' -g '!**/bin/**'
rg -n -t cs 'McpClient|OAuthPkce|WebhookIngress' src/Modules -g '!**/obj/**' -g '!**/bin/**'
rg -n -t cs 'Webhook|VerifiedWebhook' src/Kernel/DigitalBrain.Core src/Modules -g '!**/obj/**' -g '!**/bin/**'
rg -n -t cs 'new ActorContext|HttpActor' src/Kernel/DigitalBrain.Sdk -g '!**/obj/**' -g '!**/bin/**'
rg -n -t cs 'ResolvePrincipalChat|"main"' src/Kernel/DigitalBrain.Sdk/Mcp/McpAuthorizationNeuron.cs
rg -n -t cs 'StateProtectionKey|CodeVerifier|ProtectedCodeVerifier' src/Kernel/DigitalBrain.Sdk -g '!**/obj/**' -g '!**/bin/**'
```

## Ordered remaining slices

See `plans/SEAM-4-PLAN.md`. Snapshot:

1. **Done this wave:** kill dead `IDurableValue` dual token API (`DurableMcpTokenCache` ctor + `McpTokenPresence.IsMissingOrExpired` overload) — R10 one-slot honesty.  
2. Confirm B2 (CodeVerifier journal path) — micro-fix only if tip hole.  
3. Webhook **verify** helper: tip has ingress Emit only; no Sdk verify yet — hold for Eng Desk provider shape (do not invent).  
4. Keep B4 residual listed (Conversation extract).  
5. D1 local builds without FREEZE edits.  
6. D3 Product Grill rails smoke after D1.

## Kernel ask / non-goals

- **Kernel:** `MapOAuthCallback` only — do not expand Kernel OAuth/PKCE.  
- **Out of scope:** Gmail MCP parity, X webhook polish, FireRowsAs, dual-catalog, Conversation extract, graph rename, central tests, Behavior Studio, FREEZE files.
