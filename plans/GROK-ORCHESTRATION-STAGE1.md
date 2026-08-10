# DigitalBrain — Grok CLI Orchestration Plan

> Companion to `plans/RATIFIED-PRODUCT-DEFINITION.md` (the binding scope document).
> Purpose: shape the codebase from demo residue into the ratified product using **multiple grok
> CLI sessions** as the workforce, with Vlad as merge authority.
> Stage 1 = **"Stabilize and strangle"** (ratified delivery strategy). Stages 2–4 outlined at the end.

---

## 1. Operating model

Grok CLI sessions cannot talk to each other. All coordination happens through **the repository
itself** — files are the message bus, git branches are the isolation, Vlad is the scheduler and
the only merge authority.

```
                       ┌────────────────────────────┐
                       │  plans/  (briefs, reports) │  ← the shared brain of the refit
                       └─────────────┬──────────────┘
        reads brief                  │                   writes report
  ┌───────────────┐   ┌──────────────┼───────────────┐   ┌───────────────┐
  │ RED session   │   │ GREEN session│               │   │ GRILL session │
  │ (tests only)  │──►│ (one seam)   │──► gate.ps1 ──┼──►│ (adversary)   │──► Vlad merges
  └───────────────┘   └──────────────┘               │   └───────────────┘
        branch: s1x-red      branch: s1x-green       │        no branch (reads diff)
```

### Protocol rules

1. **One session = one role = one brief = one branch.** A session reads `GROK.md` + its brief
   `plans/stage1/S1.x-brief.md`, works only inside its declared scope, and ends by writing
   `plans/stage1/reports/S1.x-<role>.md` with the gate output pasted in.
2. **Three roles per seam iteration**:
   - **RED** — writes characterization/failing tests only. May add test seams (e.g.
     `InternalsVisibleTo`) but never changes production behavior.
   - **GREEN** — minimal production change to satisfy the new spec; TDD per `CLAUDE.md`; may adjust
     the RED pins where the seam's *new* ratified behavior legitimately differs (each adjustment
     listed in the report).
   - **GRILL** — a **fresh** session with no prior context. Input: the diff + the ratified
     definition + `GROK.md`. Job: try to refute the change — hunt for violated kernel traps, scope
     creep, banned patterns, untested paths, silent behavior changes. Verdict: APPROVE / REJECT
     with reasons. Rejected work goes back to a new GREEN session with the grill report.
3. **Vlad merges to main only after**: gate green + GRILL approve. Sessions never merge, never
   push to main, never touch branches they don't own.
4. **One seam per iteration** (ratified). Seam iterations are **sequential** on Lane A. Only
   additive/isolated work (tests, janitor deletions, Flutter, CI) runs in **parallel lanes** on
   separate branches — ideally separate `git worktree`s so builds don't fight over `bin/obj`:
   `git worktree add ..\digitalbrain-laneB laneB`.
5. **Stop-and-ask rule**: if a brief conflicts with `GROK.md`, the ratified definition, or reality
   in the code, the session STOPS and writes the conflict into its report instead of improvising.
   (This is the lesson of the refusal-visibility trap: a blocked agent that can't see the reason
   can't self-correct — so we make every refusal loud and written.)
6. **Session hygiene**: keep briefs small and file-pointed; sessions may use the CodeGraph MCP
   (`.mcp.json`, version pinned in S1.0) for maps instead of dumping the whole repo into context.
   Stop AppHost before building (P0-9: file locks). Run tests **without** the aspire stack up.

---

## 2. GROK.md — the standing orders (S1.0 creates it at repo root)

Content spec (mirrors `CLAUDE.md`, adds orchestration law; full draft in Appendix A):

- Build/test commands, "0 warnings expected", tests run without aspire.
- The **9 kernel traps** verbatim from `CLAUDE.md`.
- **Banned forever**: keyword god-switches; provider/action-specific synapses
  (`SalesforceReadSynapse`-style); moving outbox traffic to Orleans Streams; second job
  frameworks; MAF types leaking outside AI/Behavior internals; MAF edges as Connections;
  hot-loading generated assemblies into the silo; new NuGet packages without owner approval;
  weakening `TreatWarningsAsErrors`; touching wire aliases (`db.*`, `ui.*`, `chat.*`, `probe.*`).
- **TDD mandatory**; two test kinds (NeuronTest / DigitalBrainTest) in the single test project.
- Orchestration law: roles, one-branch rule, report template, stop-and-ask rule, gate command.
- Pointer to `plans/RATIFIED-PRODUCT-DEFINITION.md` as the scope authority.

---

## 3. Stage 1 — "Stabilize and strangle"

Ratified sequence: boundary (done — the definition) → reproduce failures → characterization tests
→ replace one defective seam per iteration. Excluded from Stage 1 (return later): Behavior
build-out, Engineering module, graph renaming, project consolidation.

### Lane map

| Lane | Nature | Runs |
|------|--------|------|
| **A** | Seam surgery (sequential iterations S1.2 → S1.6) | one GREEN at a time |
| **B** | Janitor — characterize-then-delete trash | parallel, small branches |
| **C** | Flutter — test drift fix, analyzer green | parallel |
| **D** | Harness/CI — gates, baseline | first, then as-needed |

Practical concurrency: **max 2–3 grok sessions at once** (1 Lane-A + 1–2 Lane-B/C), plus fresh
GRILL sessions on demand. More parallelism than that just creates merge conflicts in one solution.

---

### S1.0 — Harness & baseline (Lane D, 1 session)

**Objective**: make every later session cheap to verify and impossible to silently break.

- Create `GROK.md` (Appendix A), `plans/` scaffolding, report templates.
- Create `scripts/gate.ps1`: `dotnet build DigitalBrain.slnx -warnaserror` → `dotnet test
  src/Tests/DigitalBrain.Tests` → `flutter analyze` + `flutter test` for the three Flutter
  packages → exit non-zero on any failure. Record a baseline run on `main`.
- CI (`.github`): add Flutter analyze+test lane (CI is .NET-only today), add an AppHost smoke lane
  (compose up → health endpoint → down), pin CodeGraph MCP version (stop `@latest`).
- Reproduce and document P0-9 (rebuild-while-running file locks) in the report; the fix is simply
  gate discipline (stop AppHost first) — codify in `GROK.md`.
- **Exit**: `gate.ps1` green on main; baseline report committed.

### S1.1 — Characterization wave (RED sessions; Lanes A+B+C in parallel)

**Objective**: pin today's actual behavior around every P0 seam before touching anything.
One RED session per area, additive-only branches:

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

**Exit**: all pins green in one merged test suite; a one-page coverage map in the report.

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
- Tests: cross-workspace denial, role checks, SSE auth, OAuth-callback user binding (prep for
  S1.3), audit stamps, two-users-two-private-transcripts.
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
- Tests: fake-provider e2e PKCE, replay, expiry, restart, multi-silo callback, per-user isolation
  (user A can never reach user B's Salesforce).
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
- Build the **spike harness** (the ratified prove-or-reject test): one execution through restart /
  OAuth wait+resume / cancel / reconnect / duplicate submission / uncertain external write.
- **Exit**: spike harness green on the bare Execution kernel; gate + GRILL green.

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
- **Exit**: full ratified spike matrix green **through the chat surface**; kill-the-silo demo:
  send message → kill silo mid-turn → restart → turn completes or fails durably, never vanishes.

### S1.6 — Strangler: Gmail → official Google Gmail MCP (Lane A)

Ratified 5-step sequence: (1) generic per-user MCP OAuth proven stable (S1.3), (2) Salesforce
reconnected through it, (3) official Gmail MCP server connected, (4) the same user-isolation +
OAuth test suite passes against Gmail MCP, (5) **delete the typed Gmail path**
(planner/token-store/auth-rail) — parity first, deletion second.
**Exit**: Gmail works per-user via MCP; typed path gone; gate + GRILL green.

### Lane B — Janitor backlog (parallel; each item = tiny branch, characterize → delete → gate)

| # | Item | Rule |
|---|------|------|
| J1 | `WantsTimeButton`/`ShowTime` god-switch (W2) + add `Author` to `Responded` | after chat pins exist (S1.1) |
| J2 | Empty `DigitalBrain.Modules.Salesforce.Contracts` project | delete (empty ≠ consolidation) |
| J3 | Stale `docker-compose.yml` `DigitalBrain__Modules__0..9` env vars naming dead classes | delete |
| J4 | Dual module catalog (AppHost list vs `ComposedModules`) | collapse to one source **only if purely mechanical**; else park for Stage 2 (consolidation is excluded from Stage 1) |
| J5 | Unused Orleans Streams/PubSub provisioning (idle-polling noise) | first **prove** non-use under aspire telemetry, then remove provisioning; interconnect never moves to Streams |
| J6 | Behavior Studio fixtures (`seed_demo_behaviors.dart`, fixture `BehaviorClient`) | quarantine behind an explicit "DEMO — no host yet" flag; do NOT delete (Stage 3 builds the real host) |
| J7 | Webhook slice | **KEEP** (owner amendment) — characterized in S1.1, stabilized as SDK ingress rail in Stage 2; only the duplicated token-presence slice is a deletion candidate |
| J8 | Repo hygiene: `bin`/`obj`/`.vs` ignored, `Planning.txt` moved to `plans/history/` | mechanical |

### Lane C — Flutter (parallel)

Fix `activateControl` test drift; `flutter analyze` + `flutter test` green for core/kit/shell;
kit stays standalone (never imports core/shell); then Lane C is gated by CI from S1.0 onward.

### Stage 1 exit criteria (all must hold)

1. All nine P0s closed, each with a test that would catch regression.
2. Ratified spike matrix green through the chat surface (S1.5).
3. `gate.ps1` green; CI gates .NET + Flutter + AppHost smoke.
4. Two authenticated users on one workspace use private chats with per-user Salesforce **and**
   Gmail via MCP, locally.
5. Janitor backlog resolved — deleted, or kept with a written reason (J6/J7).
6. Zero keyword god-switches; zero unauthenticated endpoints; zero client-trusted identity.
7. `plans/stage1/reports/` complete; open decisions log updated (LTS-vs-preview decided at exit).

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

---

## 5. Risk register (agent-workforce specific)

| Risk | Mitigation |
|------|------------|
| Grok hallucinates Orleans/MAF/Aspire APIs (preview stack) | `TreatWarningsAsErrors` + gate after every change; pinned package versions; no new packages; Microsoft Learn / Context7 lookups encouraged in briefs |
| Context overflow → sessions "summarize" instead of reading | small file-pointed briefs; CodeGraph MCP for maps; forbid whole-repo dumps |
| Two sessions collide in one solution | lane map; worktrees; Vlad merges sequentially; rebase-before-gate |
| Timing-flaky tests poison verdicts | tests run **without** aspire up (CLAUDE.md rule); flaky test = its own janitor ticket, never "just rerun" |
| Windows file locks break builds (P0-9) | stop AppHost before building — in `GROK.md` and `gate.ps1` preflight |
| Characterization pins the bug as the spec forever | every pin referencing a P0 carries a `// PIN-DEFECT(P0-x)` marker; GREEN sessions must flip exactly those markers, GRILL checks no marker survives its seam |
| Agent deletes something load-bearing (trap 2: silent zero-receiver loss) | delete only after characterization; GRILL explicitly checks routes/receivers for anything removed |
| Scope creep ("while I was here…") | one-seam rule + GRILL rejects out-of-scope diffs outright |

---

## Appendix A — GROK.md draft (S1.0 commits this at repo root)

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
