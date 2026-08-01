# DigitalBrain Productization Design

**Status:** Draft for owner review (not implementation-authorized)

**Date:** 2026-08-01

**Branch ground (audit):** `feature/digitalbrain-architecture-continuation` @ `0949ffd8`  
**Merge-base:** `eb9c84d` (master)

**Scope:** Product definition, ship gates, deploy shape, deletion policy, and verification.  
**Not in scope of this document:** line-by-line implementation tasks (those follow only after this design is approved and a plan is written).

---

## 1. Verdict

DigitalBrain remains aimed at the **approved architecture** (neurons, synapses, Tasks, behaviors, memory, discovery). Productization does **not** shrink that vision to a chat-only wedge.

It **does** require:

1. Honest configuration and secrets (no fake OAuth “Healthy”).
2. Live proof of the nine acceptance criteria with real provider credentials.
3. A clear **two-image product** surface for deploy, without collapsing behavior process isolation.
4. Deletion of prototype theater (placeholder defaults, stub NL→C#, false status claims).
5. Explicit separation of **v1 ship gates** vs **Designed later** (e.g. UI model marketplace).

The prototype works as a lab. The product is the lab **made dependable, deployable, and truthful**.

---

## 2. Locked owner decisions

| ID | Decision | Product rule |
|---|---|---|
| **B** | Full design acceptance | All nine criteria in §3 are v1 ship gates |
| **B2** | Dual providers | Google **and** Salesforce live paths required |
| **C1** | Behavior self-programming | NL → scenarios → **model-generated C#** → BDD → publish |
| **D1** | Secrets | Real secrets in product; **mocks only in tests** |
| **E1** | Discovery | Semantic (vector) retrieval is a hard gate; exact catalog remains authority |
| **F1** | Unions | Behavior input unions are a hard gate |
| **G1** | Memory | Public `IVectorMemory` is a hard gate |
| **I1a** | Deploy shape | Two Hub images; MCP folded into brain; **isolated behavior process** |
| **J2+** | Clients | Window + headless + **web**; dev window/headless; **deploy = web** |
| **K1** | Studio | Full **six-view parity** web ↔ desktop |
| **L2** | Models | Configurable endpoints: local Ollama; Azure cloud via config/Key Vault |
| **M3** | Model UI | **No** v1 Settings model-switch gate; multi-model UI combine is **later** |

---

## 3. Ship gates (nine acceptance criteria)

Product may be claimed only when each criterion has **run-and-quoted** evidence (live system + journals/traces where applicable). Unit/scripted tests alone are insufficient for criteria that name user-visible outcomes.

| # | Criterion | v1 bar |
|---|---|---|
| 1 | Module/behavior → discovery | Active module/behavior appears in **exact catalog** and **semantic projection**; natural-language retrieval surfaces it; exact validation rejects poison/stale candidates |
| 2 | “Read my last three emails” | Via `IGmail` + `GmailRequest` (no hand-written `read_recent_messages`); live with real Google app credentials |
| 3 | Auth continuation | Missing auth → Flutter/web user action → OAuth → **same Task** continues |
| 4 | Public `IVectorMemory` | Community-style consumer uses vector memory **independent** of internal projections; no public Qdrant types |
| 5 | Behavior input unions | Multi-case behavior-owned input without central interface edit; stable case IDs; preview confined to behavior toolchain |
| 6 | Publish gates | No publish until scenarios, C#, compatibility, grants, security pass |
| 7 | Isolation + replay | Authored code only in isolated worker; operation replay; `OutcomeUncertain` when required |
| 8 | Non-programmer Studio | Understand, stop, request **test-driven** change (C1 ladder) on **web and desktop** with full six views |
| 9 | No hardcoded provider tools | Adding Google/Salesforce/Memory/behaviors does not require editing assistant tool lists; catalog materialization is the path |

**Local-only assistant tools** (e.g. convene model team) may remain as `AdditionalToolsFor` exceptions when they are not provider modules.

---

## 4. Product surfaces

### 4.1 Deployable product (I1a)

**Docker Hub product images (names):**

| Image | Responsibility |
|---|---|
| `digitalbrain` | Orleans silo; all product modules; **northbound MCP as endpoint(s) on this service**; behavior **control plane**; supervised **behavior worker process** (same image or internal sidecar — never load authored assemblies into the silo process) |
| `digitalbrain-ui` | HTTP/SSE edge: chat, auth callback, user actions, behavior APIs, **web host** for deploy UX |

**Not product images (platform dependencies):**

- Vector store (Qdrant or managed equivalent)
- Model runtime (Ollama local; Azure OpenAI / equivalent in cloud)
- Durable storage (Azure Storage / equivalent)
- **Azure Key Vault** for Google, Salesforce, model keys, and other secrets

**Explicit folds:**

- Northbound MCP is **not** a third product; it is a module/surface of `digitalbrain`.
- Behavior **execution** remains a **separate process** from the silo (I1a). Product simplicity ≠ single OS process for untrusted codegen.

### 4.2 Clients (J2+, K1)

| Host | Environment |
|---|---|
| Window host | Local interactive development |
| Headless host | Local/CI automation without chrome |
| Web host | **Deploy / product UX** |

**K1:** Behavior Studio six views and C1 flows must work with **full parity** on web and desktop. Headless is for automation, not a reduced product definition.

### 4.3 Secrets and configuration (D1 + Azure)

| Context | Rule |
|---|---|
| Product / Azure | Real Google and Salesforce **application** credentials from **Key Vault** (or equivalent inject into config). No secrets in images or git |
| Local product run | Real secrets via Aspire parameters / user-secrets / equivalent — **no** `local-dev` / `local-dev-secret` / dummy redirect defaults |
| Automated tests | Fake MCP, scripted chat, doubles only |
| Explicit live suites | Real secrets when present; fail closed with clear message when absent |
| Redirect URI | Must target the real UI OAuth callback (product path `/oauth/mcp/callback` on the UI service), not a dummy localhost path |
| Process health vs provider readiness | `/health` must not imply OAuth-ready; missing/invalid provider config is visible as setup required |

Google remains a confidential client (secret required). Salesforce remains public-client style (secret optional) per module definitions — **do not force a common secret model**.

### 4.4 Models (L2, M3)

| Concern | v1 rule |
|---|---|
| Dev | Ollama (or configured local stack) with deployment-selected default assistant model |
| Azure | Cloud chat + embeddings via configuration; secrets in Key Vault |
| UI model switching | **Not** a v1 gate |
| Multi-model combine | **Later:** users pick/combine any models **available on that deployment** (Azure and/or Ollama), building on existing group-chat/team substrate — Designed, not ship-blocking |

---

## 5. Requirement posture (Five Steps)

### Step 1 — Requirements less dumb

| Requirement | Verdict under this design |
|---|---|
| Nine acceptance criteria | **Keep** as ship gates |
| Dual live Google + Salesforce | **Keep** |
| C1 full codegen ladder | **Keep** (replace stub `BehaviorAuthor`) |
| Semantic discovery | **Keep** as gate |
| Unions | **Keep** as gate |
| Public IVectorMemory | **Keep** as gate |
| UI model marketplace | **Defer** (Designed) |
| Placeholder OAuth for green Aspire | **Delete** |
| “Studio exists ⇒ C1 done” | **Delete** assumption |
| Pure directed synapses on **every** neuron including Chat/Time | **Rewrite** — marker+synapse for integrations; entry neurons may keep operation methods |
| Absolute “no docs tree” vs agent specs | **Rewrite** — product status in README; this design + historical plans may live under `docs/superpowers` as owner-requested artifacts; site docs stay external |

### Step 2 — Delete (implementation debt targets)

Delete or remove from product path (after proofs where noted):

| Candidate | Action |
|---|---|
| Run-mode OAuth placeholders (`local-dev`, `local-dev-secret`, dummy redirect) | **Delete** |
| `BehaviorAuthor` stub presented as AI codegen | **Replace** (C1); do not ship as theater |
| Separate customer-facing MCP deployable identity | **Fold** into `digitalbrain` |
| `ExecuteLegacy` once Task-rail owns run-once/tool execution | **Delete after migrate** |
| Dual operation phase enums / mirrored broker DTOs | **Collapse** |
| `MemoryUserActionCustody` on packable SDK surface | **Move** to testing |
| README / status claims that lag or overclaim NL authoring | **Rewrite** |
| Giant test files as unmaintainable monoliths | **Split** (not delete proofs) |
| AccountEnrichment demo hardcoding as the only Studio truth | **Reduce** to sample, not product default fiction |

**Do not delete:** reverse broker, worker/behavior execution relays, exact catalog, Tasks user-action rail, isolated worker boundary, Fake MCP test edges.

### Step 3 — Simplify what survives

- One product brain image with MCP endpoints; one UI image with web host.
- Exact catalog always authoritative; vector search is candidate generation (E1 still requires it to work for NL).
- Single Task-owned execution path for behaviors (retire legacy run-once after migration).
- Provider modules stay deep; no shared account registry.
- Assistant provider tools only via catalog materialization.

### Step 4 — Accelerate cycle time

- Fix or replace broken Microsoft.Testing.Platform handshake for `dotnet test` if it remains red; prefer reliable gates (`dotnet exec` / documented runner) until fixed.
- Keep L1 module tests on Fake MCP; do not require Aspire for catalog/compiler proofs.
- Live dual-provider and C1 proofs are Explicit / deploy oracles, not every PR without secrets.
- Avoid MCP `list_resources` (hangs); use `aspire describe` / CLI.

### Step 5 — Automate last

Automate only after processes are justified:

- PublishGate / public surface law (exists)
- Wire contract goldens (exists)
- Optional: fail CI if product host injects known placeholder OAuth values
- Optional: image build pipeline for the two Hub names
- Do **not** automate NL codegen success as a silent green without scenarios

---

## 6. Architecture outcomes (keep / how)

| Outcome | Product stance |
|---|---|
| Pure directed synapses (integrations) | Keep for Google/SF/Memory/behaviors markers |
| Interfaces + synapse contracts | Keep |
| Auto-discovered modules/capabilities | Keep; E1 semantic + exact |
| No hardcoded assistant provider tools | Keep |
| Tasks own durable execution | Keep |
| GmailRequest → IGmail without narrow wrappers | Keep; live gate |
| User-defined logical inputs / unions | Keep; F1 |
| Behavior C# + Gherkin publication gates | Keep; C1 |
| Isolated behavior execution | Keep; I1a process boundary |
| Public IVectorMemory | Keep; G1 |
| Qdrant encapsulated | Keep |
| Exact authority, vector candidates | Keep |
| Flutter/web Studio full six views | Keep; K1 |
| Provider-owned app config | Keep; D1 / Key Vault |
| User-owned OAuth + Task continuation | Keep; B2 |

---

## 7. Logical flows (must work end-to-end)

### 7.1 Discovery → tool → synapse

```text
AddModule / publish behavior
  → exact ActiveCapabilityCatalog
  → projection reconcile (reserved namespaces)
  → CapabilityRouter: semantic search → exact validate (fallback exact terms not sufficient for E1 claim)
  → SynapseCapabilityTool.Materialize
  → directed SendAsync → provider/behavior
```

### 7.2 Gmail (and SF analog)

```text
User intent
  → catalog offers Gmail/SF tools
  → GmailRequest / Salesforce intent synapses
  → module MCP + OAuth
  → UserActionRequired → Task wait
  → web/desktop action → browser → UI callback
  → same Task continues → response
```

### 7.3 Behavior C1

```text
Studio Assistant change
  → scenario proposal (real, not generic stub-only)
  → user approves scenarios
  → model generates C# + bindings
  → compile + BDD + grants + security
  → publish signed revision
  → isolated worker + Tasks ops / replay
```

### 7.4 Memory

```text
Community/sample → IVectorMemory synapses → provider (Qdrant internal)
Projection boot → reserved namespaces only; no secret payloads in projection text
```

---

## 8. Testing and proof policy

| Layer | Role under productization |
|---|---|
| Compiler / source-gen / PublishGate | Law; every PR |
| DigitalBrain.Testing + module L1 | Wiring, journals, Fake MCP; every PR |
| Integrations L1 | OAuth rail with fakes |
| Behaviors / Tasks L1 | Isolation, replay, user-action |
| OS UI / composition | Edge contracts |
| Flutter unit/widget | Wire + six views (desktop and web as they land) |
| Explicit product / deploy live | **Authoritative** for B2, C1, E1, OAuth continuation with **real secrets** |
| Default CI without secrets | Must stay green without Google/SF apps |

**Product claim checklist (owner machine or Azure with Key Vault):**

1. Build Release green.  
2. Deploy or run product topology without placeholder OAuth.  
3. Live Gmail criterion 2–3 with journals.  
4. Live Salesforce read + approval-gated mutation path.  
5. Semantic discovery evidence for provider/behavior tools.  
6. C1 full ladder on **web** (and parity check desktop).  
7. Union multi-case publish proof.  
8. Public IVectorMemory sample/consumer proof.  
9. Isolation: authored code not in silo process.  
10. Secrets absent from journals/traces/prompts/vectors/manifests.

---

## 9. Implementation sequencing (design order only)

When implementation is later authorized, work in this **dependency order** (plans will detail TDD slices):

1. **Honesty layer** — remove OAuth placeholders; provider readiness; redirect correctness; README status rewrite.  
2. **Deploy shape** — fold MCP into `digitalbrain`; define behavior worker process packaging; draft two-image layout; Key Vault config mapping.  
3. **Live dual providers** — real secrets path; Explicit proofs Gmail + Salesforce.  
4. **E1 discovery quality** — projection + NL retrieval proofs for providers/behaviors.  
5. **C1 BehaviorAuthor** — real codegen + admission; kill theater.  
6. **F1 unions** — end-to-end multi-case behavior as ship proof.  
7. **G1 public memory** — sample/consumer + packaging honesty.  
8. **Web host + K1 parity** — Studio six views on web; deploy path uses web.  
9. **Legacy delete** — ExecuteLegacy after Task-only run path; DTO/enum collapse; custody move.  
10. **Hardening** — image publish pipeline, Azure deploy docs-as-code in plan, full claim checklist.

No slice may “fix” a red gate by weakening assertions, skipping tests, or restoring placeholders.

---

## 10. Non-goals (v1)

- UI Settings model marketplace / BYO API keys from the client (M3; later Designed)  
- Multi-principal IdP (remains Designed per README unless reopened)  
- Graph memory  
- Full IDE in Studio  
- Public Qdrant API  
- KernelTask / WorkId  
- In-silo load of authored behavior assemblies  
- Treating `docs/superpowers` historical plans as runtime truth over this design + README Built table  

---

## 11. Risks

| Risk | Why it matters | Mitigation |
|---|---|---|
| Model cannot emit valid behavior C# | C1 never closes | Scenario gates; measured eval harness; no fake Author |
| Dual live OAuth ops burden | B2 blocks claim | Key Vault; clear setup UX; Explicit suites |
| Semantic retrieval flaky | E1 blocks claim | Projection tests + live NL tool-selection evidence |
| Web Studio parity cost | K1 large | Sequential view parity; shared view-models |
| Folding MCP incorrectly | Breaks tools | Endpoint parity tests; single owner process |
| Collapsing behavior process | Breaks isolation | I1a non-negotiable; process boundary tests |
| Scope stacking | Schedule slip | Sequencing §9; no silent scope adds |

---

## 12. Future completion (branch finish)

Only after approved implementation **and** a full green verification suite (including Explicit live proofs with real secrets where required):

Use `superpowers:finishing-a-development-branch` and present:

1. Merge back to base branch locally  
2. Push and create a Pull Request  
3. Keep the branch as-is  

Until then: no merge/push/finish from this audit stream.

---

## 13. Authorization boundary

| Action | Status after this document |
|---|---|
| Research / design / plan writing | Allowed after owner approves **this** design |
| Implementation / refactor / test edits | **Forbidden** until owner **explicitly authorizes execution** of an approved plan |
| Aspire restart / secret injection by agents | Only with owner approval |
| Docker Hub publish / Azure deploy | Owner-operated or explicitly authorized |

---

## 14. Self-review (author)

| Check | Result |
|---|---|
| Placeholders / TBD | None intentional; live proof details deferred to plan |
| Contradictions | I1a vs single-process: resolved as separate behavior process. M3 vs multi-model future: later Designed. B vs Chat method APIs: rewritten not absolute |
| Scope | Large by owner choice (B…K1); sequencing §9 is mandatory |
| Ambiguity | Behavior worker “in-image vs sidecar” left as packaging choice under I1a — plan must pick one with process isolation tests |
| Five Steps | Requirements → delete → simplify → cycle → automate order stated |
| Unsupported requirements | C1 model quality still unmeasured — called out as risk, not assumed solved |

---

## 15. Document history

| Date | Change |
|---|---|
| 2026-08-01 | Initial productization design from repository-wide audit + owner grilling (B, B2, C1, D1, E1, F1, G1, I1a, J2+, K1, L2, M3, N1) |
