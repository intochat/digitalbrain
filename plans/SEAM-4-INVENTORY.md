# Seam 4 — tip inventory (complete-refactoring)

**Date:** 2026-08-12 · Tip at inventory: `38b6055d` ancestry (Sdk rails present under `src/Kernel/DigitalBrain.Sdk`)  
**Bound to:** `plans/SEAM-4-ACCEPTANCE.md` order A → B → C → D  
**Freeze:** do not touch AppHost.cs / AIHostingExtensions / appsettings.Development.json

## A. Ownership honesty (tip greps)

| ID | Status | Evidence |
|---|---|---|
| **A1** MCP list/call on Sdk | **DONE tip** | `src/Kernel/DigitalBrain.Sdk/Mcp/McpServerNeuron.cs` (`ListToolsAsync` / `CallToolAsync` via `IMcpToolTransport` + `McpClientSessions`). No second module MCP client stack found under `src/Modules` for same serverKey. |
| **A2** OAuth/PKCE + TokenSlot in Sdk | **DONE tip** | `McpAuthorizationRail.cs`, `McpOAuthSession.cs`, `PrincipalTokenSlot.cs`, `McpTokenExchange.cs`. Modules register OAuth **hosting definitions** only (e.g. Salesforce Aspire) — call rails, do not reimplement PKCE. |
| **A3** WebhookIngress Sdk rail | **DONE tip** | `WebhookIngressNeuron` + `VerifiedWebhookDeliveryReceived` + Accepted/Duplicate/Conflict under `src/Kernel/DigitalBrain.Sdk/Webhook/`. No module ingress clones found. |
| **A4** Kernel MapOAuthCallback only | **DONE tip** | `src/Kernel/DigitalBrain.Kernel/MapOAuthCallback.cs` + `Program.cs` `app.MapOAuthCallback()`. Sdk `McpOAuthCallback` joins auth neuron — does not mint `ActorContext`. |

**Folder note:** Sdk project path is `src/Kernel/DigitalBrain.Sdk` with namespace `DigitalBrain.Modules.Sdk.*` — ownership is Sdk rails (Integrations), not Core interconnect. Residual: consider path/namespace honesty doc in PR (no rename required this seam unless Eng Desk asks).

## B. Principal-keyed slots (R10)

| ID | Status | Evidence |
|---|---|---|
| **B1** `(serverKey, PrincipalId)` slot | **DONE tip** | `McpAuthorizationNeuron.SlotKey` → `{serverKey}/{principal:N}`; `PrincipalTokenSlot` addresses one subject key in durable dict. |
| **B2** Tokens protected / not journaled as secrets | **LIKELY DONE** | Protection helpers under `Sdk/Protection`; tokens written as protected payloads to durable dict — confirm no plaintext journal fields in follow-up slice. |
| **B3** No silent credential fallback | **NEEDS SLICE** | Grep + product path review: publish/share must not transfer personal credentials (gate in B slice). |
| **B4** No hardcode chat+main park | **RESIDUAL** | `McpAuthorizationNeuron.ResolvePrincipalChat` → `NeuronId("chat", owner, PrincipalPartition… "main")`. List as Conversation-extract residual — do not fake done. |

## C. Webhook ingress shape

| ID | Status | Evidence |
|---|---|---|
| **C1** Grain key = SubscriptionId | **DONE tip** | `[GrainType("webhook-ingress")]`; handler requires `Id.Name == synapse.SubscriptionId`. |
| **C2** Emit Accepted\|Duplicate\|Conflict | **DONE tip** | `WebhookIngressNeuron.HandleAsync` Emit path after digest dedupe. |
| **C3** No Core webhook types | **DONE tip** | Types live under Sdk/Webhook; no Core webhook stack found. |

## D. Proof / gate

| ID | Status | Notes |
|---|---|---|
| **D1** `dotnet build DigitalBrain.slnx -warnaserror` | **PENDING** | Run after B/C honesty slices; AppHost FREEZE means don't "fix" build via AppHost hacks. |
| **D2** PR ownership note + grep evidence | **THIS COMMIT** | Acceptance + this inventory. |
| **D3** Product Grill Seam 4 smoke | **LATER** | Not re-litigate Seam 1 cookie grill. |

## Ordered implementation slices (remaining)

1. **A residual:** PR note that Sdk lives under `src/Kernel/DigitalBrain.Sdk` but is Integrations-owned; optional namespace/path move is **non-goal** unless Architect amends.
2. **B3:** Audit share/publish credential paths; refuse silent fallback (code if tip has a hole).
3. **B2 confirm:** Prove protected-payload write path; no secret strings in journals.
4. **B4 residual:** Document Conversation extract dependency; no fake fix.
5. **D1:** Local `dotnet build DigitalBrain.slnx -warnaserror` green on tip without FREEZE file edits.
6. **D3:** Short Product Grill rails smoke (slot key + webhook emit) after D1.

Kernel coordination: none expected beyond existing `MapOAuthCallback` — ping Kernel only if callback mint edge needs change.
