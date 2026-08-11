# DigitalBrain — Grok CLI Orchestration Plan

> Companion to `plans/RATIFIED-PRODUCT-DEFINITION.md` (the binding scope document).
> Purpose: shape the codebase from demo residue into the ratified product using **multiple grok
> CLI sessions** as the workforce, with Vlad as merge authority.
> Stage 1 = **"Stabilize and strangle"** (ratified delivery strategy). Stages 2–4 outlined at the end.
>
> **Owner amendment — 2026-08-11:** grok orchestration has ended; Codex is the implementer. The
> central automated-test project was intentionally deleted and no automated tests are created or
> run during this refit. Production source is the current truth. Verification is source
> characterization, adversarial diff review, zero-warning build/static analysis, and live smoke.
> A per-module testing framework is deferred to final hardening. Salesforce Contracts is kept as
> a permanent product boundary. This amendment supersedes conflicting historical protocol below.

---

## 1. Operating model

Codex implements one seam at a time on `stage1-stabilize-strangle`; Vlad remains the only merge
authority for `master`. Plans and reports remain the durable decision log.

```
                       ┌────────────────────────────┐
                       │  plans/  (briefs, reports) │  ← the shared brain of the refit
                       └─────────────┬──────────────┘
        source characterization       │             report evidence
  ┌───────────────┐   ┌──────────────┼───────────────┐   ┌───────────────┐
  │ SOURCE        │──►│ GREEN         │──► self-GRILL ┼──►│ build/static  │──► commit
  │ (read/routes) │   │ (one seam)    │              │   │ gate + smoke  │
  └───────────────┘   └───────────────┘              │   └───────────────┘
```

### Protocol rules

1. **One seam per iteration on one branch.** Read `GROK.md`, the ratified definition, the relevant
   brief/report, and the production source before editing.
2. **SOURCE → GREEN → GRILL → GATE → COMMIT**:
   - **SOURCE** — characterize current implementations, routes, manifests, and observable behavior.
   - **GREEN** — make the smallest coherent production change required by the ratified product.
   - **GRILL** — adversarially inspect the complete diff for kernel traps, scope creep, banned
     patterns, stale call sites, and silent behavior changes. Correct findings before proceeding.
   - **GATE** — stop AppHost, then run the zero-warning .NET build and relevant production-source
     static analysis. Live-stack work has its own smoke evidence and must be stopped afterward.
3. **Vlad merges to main only after** the gate is green and self-review findings are resolved.
4. No automated test commands or test-project recreation during this refit. The future test
   architecture is per-module and belongs to final hardening.
5. **Stop-and-ask rule**: if a brief conflicts with `GROK.md`, the ratified definition, or reality
   in the code, the session STOPS and writes the conflict into its report instead of improvising.
   (This is the lesson of the refusal-visibility trap: a blocked agent that can't see the reason
   can't self-correct — so we make every refusal loud and written.)
6. **Implementation hygiene**: keep seams small and file-pointed. Stop AppHost before building
   (P0-9: file locks). Never run a test command under the current owner amendment.

---

## 2. GROK.md — the standing orders (S1.0 creates it at repo root)

Content spec (mirrors `CLAUDE.md`, adds orchestration law; full draft in Appendix A):

- Build/static commands, "0 warnings expected", and AppHost-stop preflight.
- The **9 kernel traps** verbatim from `CLAUDE.md`.
- **Banned forever**: keyword god-switches; provider/action-specific synapses
  (`SalesforceReadSynapse`-style); moving outbox traffic to Orleans Streams; second job
  frameworks; MAF types leaking outside AI/Behavior internals; MAF edges as Connections;
  hot-loading generated assemblies into the silo; new NuGet packages without owner approval;
  weakening `TreatWarningsAsErrors`; touching wire aliases (`db.*`, `ui.*`, `chat.*`, `probe.*`).
- Source-first method and the deferred per-module testing-framework decision.
- Implementation law: one-seam rule, report template, stop-and-ask rule, gate command.
- Pointer to `plans/RATIFIED-PRODUCT-DEFINITION.md` as the scope authority.

---

## 3. Stage 1 — "Stabilize and strangle"

Ratified sequence as amended: boundary (done) → characterize production source/routes → replace
one defective seam per iteration → adversarial review → build/static gate. Excluded from Stage 1: Behavior
build-out, Engineering module, graph renaming, project consolidation.

### Lane map

| Lane | Nature | Runs |
|------|--------|------|
| **A** | Seam surgery (sequential iterations S1.2 → S1.6) | one seam at a time |
| **B** | Janitor — characterize-then-delete trash | parallel, small branches |
| **C** | Flutter — production-source cleanup, analyzer green | parallel |
| **D** | Harness/CI — gates, baseline | first, then as-needed |

Historical grok concurrency is closed. Codex now works the remaining list sequentially to keep one
coherent solution and one reviewable diff per commit.

---

### S1.0 — Harness & baseline (Lane D, 1 session)

**Objective**: make every later session cheap to verify and impossible to silently break.

- Create `GROK.md` (Appendix A), `plans/` scaffolding, report templates.
- Create `scripts/gate.ps1`: `dotnet build DigitalBrain.slnx -warnaserror` → `flutter analyze lib`
  for the three Flutter packages when `-Flutter` is requested → exit non-zero on failure.
- CI (`.github`): gate the .NET source build. Flutter production-source analysis and the AppHost
  smoke remain explicit local exit gates until CI infrastructure is intentionally added.
- Reproduce and document P0-9 (rebuild-while-running file locks) in the report; the fix is simply
  gate discipline (stop AppHost first) — codify in `GROK.md`.
- **Exit**: `gate.ps1` green on main; baseline report committed.

### S1.1 — Historical characterization wave

**Historical objective**: capture behavior around every P0 seam before replacement. Its deleted
automated tests and reports are archaeology only; current truth is the production implementation.

| Area | Pins to write (referencing P0s) |
|------|--------------------------------|
| OAuth rails | fake-provider round trip; the dual-state defect; state dictionary growth; code replay accepted (pin the *defect* so its fix is visible) — P0-1, P0-5 |
| HTTP surface | `/owner/commands` happy path; client-supplied `chatName` reaches the transcript (P0-4); SSE resume behavior; unauthenticated access today (P0-3) |
| Turn lifecycle | browser-abort cancels the AI run mid-turn (P0-2); MAF session lost on mid-stream crash (P0-6) |
| MCP gateway | `list-tools`/`call-tool`; destructive-tool blanket rejection (P0-7); `FireRowsAs` row cap |
| Tasks/Execution machinery | command receipts growth; `OutcomeUncertain` dead-end; AttemptId in operation identity (P0-8) |
| Composition | reflected manifests; the AppHost-vs-`ComposedModules` dual catalog; broadcast catalog ghosts |
| Webhook slice (**keep per amendment**) | characterize current webhook behavior as the seed of the SDK ingress rail — no deletion |
| Flutter (Lane C) | fix pre-existing `activateControl` drift so Lane C is green before CI gates it |

**Exit (amended)**: reports exist as historical evidence; current source is audited at Stage-1 exit.

### S1.2 — Seam: Identity boundary (Lane A; kills P0-3, P0-4)

The ratified identity-first slice:

- ASP.NET Core Identity at the Host boundary (username/password; local accounts; loopback dev may
  skip login; HTTPS mandatory beyond localhost).
- Orleans grains own product state: `Principal`, `Workspace`, `Membership` (+`AutomationPrincipal`
  reserved for Behaviors), roles Owner/Admin, Builder, Viewer.
- Replace the singleton `"dev"` owner with a workspace-scoped brain factory; authenticate **every**
  HTTP/SSE endpoint; propagate a trusted `ActorContext`; **persist actor stamps into durable
  commands** (RequestContext alone doesn't survive reminders/retries/restarts).
- Server derives chat/transcript identity from the authenticated principal — client-supplied
  `chatName` trust removed.
- Source/live evidence: cross-workspace denial, role checks, SSE auth, OAuth-callback user binding
  (prep for S1.3), audit stamps, and two users with two private transcripts.
- **Exit**: two local users, private chats, all endpoints authenticated; gate + GRILL green.

### S1.3 — Seam: OAuth/Integration rail (Lane A; kills P0-1, P0-5, P0-7)

- One PKCE flow for all providers (the manual-URL path dies); state is **bounded, expiring,
  one-shot, bound to workspace + user + credential subject**; completed codes non-replayable.
- Introduce the ratified Integration record: `{Provider, Scope: User|Workspace, SubjectId,
  ExternalAccount, GrantedScopes, ProtectedTokenReference}`; tokens keyed by verified principal,
  never by neuron name. Strictly per-user resolution in chat; no silent fallback.
- **Remove the destructive-tool blanket rejection** (ratified allow-all): `tools/list` is the
  catalog; keep the generic invariants — never cross user integration boundaries, audit
  actor/integration/tool/correlation/outcome, time/size/call-count limits, never log tokens.
- Source/live evidence: trace fake-provider PKCE, replay, expiry, restart, multi-silo callback,
  and per-user isolation paths (user A can never reach user B's Salesforce).
- **Exit**: Salesforce MCP works per-user end-to-end through the new rail; gate + GRILL green.

### S1.4 — Seam: Execution kernel (Lane A; kills P0-8)

- **Rename** `DigitalBrain.Modules.Tasks` → `DigitalBrain.Modules.Execution` (`IExecution`,
  `ExecutionNeuron`) — ratified, **no compatibility layer** (no real callers).
- Deepen per the ratified hybrid: minimal external `Apply/Read` (+ versioned `Cancel`); attempts,
  workers, reminders, cursors, operation phases, blocker custody all internal.
- Fix the machinery: bound receipts/operations; `OutcomeUncertain` gets a reconciliation path and
  **never auto-retries** a started non-idempotent operation; operation identity stable **across
  attempts** (drop AttemptId from it) so a retry can suppress the duplicate side effect; delete the
  unimplemented supervised `GroupChat` worker methods or implement them — no dead throws.
- Audit the **spike scenario**: one execution through restart / OAuth wait+resume / cancel /
  reconnect / duplicate submission / uncertain external write.
- **Exit**: source invariants for the scenario hold on the bare Execution kernel; build/static
  gate + adversarial review green.

### S1.5 — Seam: Durable conversation turns (Lane A; kills P0-2, P0-6)

Conversation becomes Execution's **first production adapter** (ratified):

- POST appends the user message + a durable `TurnId` and returns promptly; the AI run proceeds
  independently of the HTTP connection (browser refresh never cancels the brain's work; explicit
  cancel = versioned command).
- **FIFO** (ratified): one active Execution per Conversation; extra turns durably queued; cancel
  advances the queue; different Conversations run concurrently.
- Turn lifecycle Pending → Running → Completed/Failed/Cancelled projected to the UI; reconnecting
  clients resume from the conversation sequence and see real status.
- Fix MAF session persistence (P0-6): persist at safe points, not only after stream completion;
  fingerprint drift handled by explicit reset/migration path.
- **Exit**: full ratified spike matrix is source-audited **through the chat surface**; live smoke:
  send message → kill silo mid-turn → restart → turn completes or fails durably, never vanishes.

### S1.6 — Strangler: Gmail → official Google Gmail MCP (Lane A)

Ratified 5-step sequence: (1) generic per-user MCP OAuth proven stable (S1.3), (2) Salesforce
reconnected through it, (3) official Gmail MCP server connected, (4) the same user-isolation +
OAuth invariants are source-audited against Gmail MCP, (5) **delete the typed Gmail path**
(planner/token-store/auth-rail) — parity first, deletion second.
**Exit**: Gmail works per-user via MCP; typed path gone; gate + GRILL green.

### Lane B — Janitor backlog (parallel; each item = tiny branch, characterize → delete → gate)

| # | Item | Rule |
|---|------|------|
| J1 | `WantsTimeButton`/`ShowTime` god-switch (W2) + add `Author` to `Responded` | after chat pins exist (S1.1) |
| J2 | `DigitalBrain.Modules.Salesforce.Contracts` project | **KEEP** — permanent boundary for module neuron and synapse interfaces (owner amendment) |
| J3 | Stale `docker-compose.yml` `DigitalBrain__Modules__0..9` env vars naming dead classes | delete |
| J4 | Dual module catalog (AppHost list vs `ComposedModules`) | collapse to one source **only if purely mechanical**; else park for Stage 2 (consolidation is excluded from Stage 1) |
| J5 | Unused Orleans Streams/PubSub provisioning (idle-polling noise) | first **prove** non-use under aspire telemetry, then remove provisioning; interconnect never moves to Streams |
| J6 | Behavior Studio fixtures (`seed_demo_behaviors.dart`, fixture `BehaviorClient`) | quarantine behind an explicit "DEMO — no host yet" flag; do NOT delete (Stage 3 builds the real host) |
| J7 | Webhook slice | **KEEP** (owner amendment) — characterized in S1.1, stabilized as SDK ingress rail in Stage 2; only the duplicated token-presence slice is a deletion candidate |
| J8 | Repo hygiene: `bin`/`obj`/`.vs` ignored, `Planning.txt` moved to `plans/history/` | mechanical |

### Lane C — Flutter (parallel)

Analyze production `lib/` source for core/kit/shell, remove show-time sample residue, and keep kit
standalone (never imports core/shell). Existing Flutter tests are untouched and not executed.

### Stage 1 exit criteria (all must hold)

1. All nine P0 source implementations audited against the ratified behavior and seam reports.
2. Ratified spike invariants source-audited through the chat surface (S1.5), with observable paths
   included in the live AppHost smoke where local integrations permit.
3. `gate.ps1 -Flutter` green; CI gates the .NET source build; AppHost smoke evidence is recorded.
4. Two authenticated users on one workspace use private chats with per-user Salesforce **and**
   Gmail via MCP, locally.
5. Janitor backlog resolved — deleted, or kept with a written reason (J6/J7).
6. Zero keyword god-switches; zero unauthenticated endpoints; zero client-trusted identity.
7. `plans/stage1/reports/` complete; open decisions log updated (LTS-vs-preview decided at exit).
8. Automated tests remain explicitly deferred; final hardening will create module-owned test
   projects/frameworks rather than restore one central suite.

---

## 4. Stages 2–4 (outline — each gets its own plan file when Stage 1 exits)

- **Stage 2 — Strangle deeper (structure)**: extract the Conversation module (ratified D1–D6; Chat
  neuron dissolves into ConversationNeuron + Flutter projection; `IConversationResponder` inverts
  UI→AI); grill conversation storage shape; formalize **SDK rails** — Authorization/OAuth folder,
  **webhook ingress rail** (amendment §1.18), state-protection — then the deferred project
  consolidation and graph rename (`ConnectionGraphNeuron` vs `TopologyGraphNeuron`), single module
  catalog if J4 was parked.
- **Stage 3 — Build the product (Behavior)**: single-file C# Behavior runtime — pinned immutable
  artifacts, out-of-process killable worker, **no ambient authority**, stable operation keys with
  replay-from-entry, `RecoveryExecution` for fixes (all ratified); approval flow (one approval at
  creation → autonomous); real Behavior host behind the Studio; **X/Twitter module as the first
  webhook-rail consumer** — "when @elonmusk posts, run behavior" becomes the acceptance test;
  proof journey: Azure dev RG + `digitalbrain.tech` + publish-to-workspace.
- **Stage 4 — Self-aware (Engineering)**: read-only Engineering control plane over
  logs/traces/metrics; delivery-pulse event stream; Brain Map polish; refusal-visibility decision
  (kernel refusal-replies vs error-bearing responses) — the top-priority open item from `CLAUDE.md`.
- **Final hardening**: design and implement the per-module automated-testing framework after
  product seams and module ownership stabilize; never recreate one central test project.

---

## 5. Risk register (agent-workforce specific)

| Risk | Mitigation |
|------|------------|
| Grok hallucinates Orleans/MAF/Aspire APIs (preview stack) | `TreatWarningsAsErrors` + gate after every change; pinned package versions; no new packages; Microsoft Learn / Context7 lookups encouraged in briefs |
| Context overflow → sessions "summarize" instead of reading | small file-pointed briefs; CodeGraph MCP for maps; forbid whole-repo dumps |
| Two sessions collide in one solution | lane map; worktrees; Vlad merges sequentially; rebase-before-gate |
| Deleted historical tests are mistaken for current authority | owner amendment is repeated in active plans; current gates inspect/build production source only |
| Windows file locks break builds (P0-9) | stop AppHost before building — in `GROK.md` and `gate.ps1` preflight |
| Historical characterization pins are mistaken for current behavior | source audit follows current routes and implementation; reports are archaeology only |
| Agent deletes something load-bearing (trap 2: silent zero-receiver loss) | delete only after characterization; GRILL explicitly checks routes/receivers for anything removed |
| Scope creep ("while I was here…") | one-seam rule + GRILL rejects out-of-scope diffs outright |

---

## Appendix A — historical GROK.md draft (superseded)

The following draft is retained only to explain old reports. It is not an executable instruction;
the 2026-08-11 owner amendment and the root `GROK.md` govern current work.

```markdown
# GROK.md — standing orders for grok CLI sessions

You are one session in a multi-session refit of DigitalBrain. You have ONE role
(RED | GREEN | GRILL | JANITOR), ONE brief (plans/stage1/S1.x-brief.md), ONE branch.
Scope authority: plans/RATIFIED-PRODUCT-DEFINITION.md. Never exceed your brief.

## Commands
- Build:  dotnet build DigitalBrain.slnx        # 0 warnings expected (warnaserror)
- Test:   dotnet test src/Tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj
          # run WITHOUT the aspire stack up (timing tests flake on a saturated machine)
- Stack:  dotnet run --project src/Kernel/DigitalBrain.AppHost   # STOP it before building
- Flutter: flutter analyze && flutter test   (per package in src/Modules/UI/Flutter/{core,kit,shell})
- Gate:   pwsh scripts/gate.ps1              # run before ending your session; paste output in report

## Kernel traps (violating any = automatic GRILL rejection)
1. A turn cannot await the effect of its own Send — use Neuron.FlushOutboxAsync.
2. Zero-receiver emissions are silently lost — confirm routes before making things visible.
3. Non-framework grain calls between neurons are reified — kernel interfaces belong in
   CapabilityInvocation.FrameworkInterfaces.
4. Deterministic refusals throw NeuronAuthorizationException (settled); everything else retries 1000×.
5. Only RequestSynapse<TResponse> materialize as model tools.
6. Manifests are reflected, never written.
7. Models pass names where schemas want GUIDs; missing value-type JSON binds to defaults — validate.
8. Any IHandle<T> joins the broadcast catalog and spawns ghosts per Emit.
9. Keyword god-switches are banned.

## Banned forever
Provider/action-specific synapses; outbox traffic on Orleans Streams; second job frameworks;
MAF types outside AI/Behavior internals; MAF edges as Connections; hot-loading generated
assemblies into the silo; new NuGet packages without owner approval; weakening warnaserror;
changing wire aliases (db.*, ui.*, chat.*, probe.*); client-trusted identity.

## Method
TDD is mandatory: failing test first, minimal green, refactor. Two test kinds only
(NeuronTest-style, DigitalBrainTest-style) in src/Tests/DigitalBrain.Tests.
Defect pins carry // PIN-DEFECT(P0-x) markers; only the seam that fixes P0-x may flip them.

## Ending your session
Write plans/stage1/reports/S1.x-<role>.md: what changed, tests added/flipped, gate output,
open risks, anything out of scope you noticed (do NOT fix it). If blocked by a rule conflict,
STOP and write the conflict into the report — a written refusal beats silent improvisation.
```

## Appendix B — brief template (`plans/stage1/S1.x-brief.md`)

```markdown
# S1.x — <seam name>   (role: RED|GREEN|GRILL|JANITOR, branch: s1x-<role>)
Objective: <one sentence>
In scope: <projects/files>
Out of scope: <explicit no-go list>
Ratified constraints that bind this work: <bullet refs into RATIFIED-PRODUCT-DEFINITION.md>
Definition of done: <tests + gate + report>
```

## Appendix C — starter prompt for the first session (copy into grok CLI)

```
Read GROK.md if it exists; it does not yet — you are S1.0, the harness session, on branch s10-harness.
Read plans/RATIFIED-PRODUCT-DEFINITION.md and plans/GROK-ORCHESTRATION-STAGE1.md fully.
Execute S1.0 exactly as specified in §3 of the orchestration plan: create GROK.md from Appendix A,
plans/stage1/ scaffolding with briefs for S1.1 RED sessions from §3, scripts/gate.ps1, the CI
lanes, pin the CodeGraph MCP version, reproduce and document P0-9, record the baseline.
Do not touch any production code under src/. End by running gate.ps1 and writing
plans/stage1/reports/S1.0-harness.md with the output.
```
