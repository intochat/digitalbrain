# Seam 4 — Sdk rails honesty (acceptance)

**Owner:** Integrations (lead) · Kernel host = `MapOAuthCallback` only · Platform = secrets wiring  
**Out of scope:** Gmail MCP parity, X webhook consumer polish, FireRowsAs (later Integrations product), AppHost freeze files unless Eng Desk lifts FREEZE, dual-catalog unification (separate), Conversation extract.

**Refs:** FINAL §3.3 / §6 / R10 · `plans/SEAMS-2-4-ROUTING.md` · tip Sdk under `src/`

---

## Acceptance checklist (all must hold)

### A. Ownership honesty
- [ ] **A1** Outbound MCP list/call path lives on Sdk `McpServerNeuron` (or successor in Sdk). No second module-owned MCP client stack for the same serverKey.
- [ ] **A2** OAuth/PKCE + token cache + `PrincipalTokenSlot` live in Sdk. Modules/Integrations product grains **call** rails; they do not reimplement PKCE/callback.
- [ ] **A3** `WebhookIngressNeuron` + verify + Accepted|Duplicate|Conflict contracts stay Sdk rail (not Core interconnect, not per-module ingress clones).
- [ ] **A4** Kernel host owns **only** one-shot `MapOAuthCallback` (+ cookie/`HttpActor` mint already Seam 1). Sdk must not mint `ActorContext`.

### B. Principal-keyed slots (R10)
- [ ] **B1** One slot model: keyed `(serverKey, PrincipalId)` via Sdk `PrincipalTokenSlot` (+ Abstractions Integration descriptors). No parallel “Integration grain vs TokenSlot” dual truth.
- [ ] **B2** Tokens protected (`StateProtectionKey`); **never** journaled as secrets.
- [ ] **B3** No silent credential fallback; publish/share never transfers personal credentials.
- [ ] **B4** `McpAuthorizationNeuron` (or successor) must **not** hardcode `chat`+`main` as the only park target — residual listed if Conversation extract still blocks a clean fix (name the residual; don’t fake done).

### C. Webhook ingress shape
- [ ] **C1** Grain key = `SubscriptionId` (per-subscription / per-principal fan-in), not one global ingress with thousands of graph targets.
- [ ] **C2** Handle `VerifiedWebhookDeliveryReceived` → dedupe → **Emit** Accepted|Duplicate|Conflict; graph/morphs attach subscribers.
- [ ] **C3** No Core webhook types; no Streams/EH as brain bus substitute.

### D. Proof / gate
- [ ] **D1** `dotnet build DigitalBrain.slnx -warnaserror` green (local tip).
- [ ] **D2** Short ownership note in PR: which types moved/stayed; grep evidence no duplicate OAuth/webhook entrypoints in Modules.
- [ ] **D3** Product Grill: GREEN≠GRILL — live cookie/MCP path already Seam 1; Seam 4 grill = rails ownership + principal slot unify smoke, not re-litigate Seam 1.

---

## Explicit non-goals this seam
- Renaming `ISynapseGraph` / grain `synapsegraph:`
- Restoring central test suite
- Behavior Studio
- Touching AppHost.cs / AIHostingExtensions / appsettings.Development.json while Eng Desk FREEZE is up

## Sign-off
```text
Integrations: done criteria met ________  date ________
Architect: accept / amend ________
Eng Desk: schedule next ________
```
