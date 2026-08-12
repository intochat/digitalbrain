# Seam 4 — PLAN (ordered slices)

**Owner:** Integrations · Kernel = `MapOAuthCallback` only · Platform = secrets  
**Tip base noted at first honesty commit:** `complete-refactoring`  
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
| **0** | Honesty docs + tip inventory + acceptance | Acceptance/inventory/plan/ownership match tip | **LANDING** (prior docs + this plan/OWNERSHIP) |
| **1** | Kill dead dual token API | No `IDurableValue` token ctor/overload; only `PrincipalTokenSlot` | **THIS COMMIT** |
| **2** | B2 journal confirm | Prove `CodeVerifier` on `BeginMcpAuthorization` never journals plaintext secrets (or micro-fix) | pending |
| **3** | Webhook verify helper honesty | Document tip gap: ingress assumes pre-verified synapse; add Sdk verify helper **only** if Eng Desk names a provider shape (no invented X/Gmail verify) | pending / hold |
| **4** | D1 gate | `dotnet build src/Kernel/DigitalBrain.Sdk` then `DigitalBrain.slnx -warnaserror` green locally | in progress |
| **5** | D2 PR ownership note | Grep evidence no module OAuth/webhook entrypoints (inventory greps) | docs ready |
| **6** | D3 Product Grill | Rails + principal-slot smoke (not Seam 1 cookie re-litigation) | later |
| **7** | B4 residual | Keep listed until Conversation extract | residual |

## Kernel coordination

**Only:** keep `MapOAuthCallback` as the sole HTTP OAuth callback edge. Do not move PKCE/token exchange into Kernel. No second callback map in Modules.

## Eng Desk one-liner template

`Seam4 slice N @ <SHA>: <what>; residuals: B4 chat+main; Kernel ask: MapOAuthCallback only.`
