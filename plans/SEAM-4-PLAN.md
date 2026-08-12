# Seam 4 — PLAN (ordered slices)

**Owner:** Integrations · Kernel = `MapOAuthCallback` only · Platform = secrets  
**Tip:** `complete-refactoring` · slice-1 code @ `3dbb3e94` · D1 @ `3778fa40`  
**Freeze:** AppHost.cs / AIHostingExtensions / appsettings.Development.json  
**Bound to:** `SEAM-4-ACCEPTANCE.md` · tip inventory `SEAM-4-INVENTORY.md`  
**Folder ownership note:** `src/Kernel/DigitalBrain.Sdk/OWNERSHIP.md`

## What NOT to touch

- AppHost FREEZE files; kill/restart AppHost
- Conversation extract / B4 chat+main “fix” (list residual only)
- Assembly rename `DigitalBrain.Modules.Sdk` → `DigitalBrain.Sdk` (separate PR)
- Seam 3 Core folder moves; graph/`ISynapseGraph` rename
- Gmail MCP parity, X webhook polish, FireRowsAs, dual-catalog unification
- CloudAgent / push / invent product scope

## Ownership boundaries

| Surface | Owner | Callers |
|---|---|---|
| Sdk MCP/OAuth/Webhook/Protect | Integrations | Modules register defs; Kernel callback only |
| `MapOAuthCallback` | Kernel host | Sdk `IMcpAuthorization.DeliverCallback` |
| `McpServerDefinition` + Aspire OAuth params | Modules + Aspire.Hosting | Sdk runtime |
| Core webhook types | **forbidden** | — |

## Ordered slices

| # | Slice | Done when | Status |
|---|---|---|---|
| **0** | Honesty docs + tip inventory + acceptance | Acceptance/inventory/plan/ownership match tip | **DONE** (`OWNERSHIP.md` + prior acceptance/inventory) |
| **1** | Kill dead dual token API | No `IDurableValue` token ctor/overload; only `PrincipalTokenSlot` | **DONE** @ `3dbb3e94` (R10; Sdk build green) |
| **2** | B2 journal confirm | Prove `CodeVerifier` on `BeginMcpAuthorization` never journals plaintext secrets (or micro-fix) | pending (protect-at-rest tip-clean; synapse-surface confirm) |
| **3** | Webhook verify helper honesty | Tip gap: ingress assumes pre-verified synapse; add Sdk verify **only** if Eng Desk names provider shape | hold |
| **4** | D1 gate | Sdk + `DigitalBrain.slnx -warnaserror` green locally | **DONE tip** @ `3778fa40` (see D3 smoke) |
| **5** | D2 PR ownership note | Grep evidence no module OAuth/webhook entrypoints | docs ready (`OWNERSHIP.md` + inventory greps) |
| **6** | D3 Product Grill | Rails + principal-slot smoke packet | packet @ `plans/SEAM-4-D3-SMOKE.md` — await Grill |
| **7** | B4 residual | Keep listed until Conversation extract | residual |

## Kernel coordination

**Only:** keep `MapOAuthCallback` as the sole HTTP OAuth callback edge. Do not move PKCE/token exchange into Kernel. No second callback map in Modules.

## Eng Desk one-liner template

`Seam4 slice N @ <SHA>: <what>; residuals: B4 chat+main; Kernel ask: MapOAuthCallback only.`
