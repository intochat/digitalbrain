# IntoChat — 4-hour parallel delivery (orchestration plan)

Executes the remaining tasks of `2026-09-23-intochat-product-delivery.md` (the master plan) with
parallel **OpenCode** workers, one git worktree each, and Claude Code as the orchestrator
(dispatch, merge, integration tests, review). The master plan stays the source for each task's
files, tests and acceptance; this document is only *how* and *in what order*.

Status of record: `2026-09-23-intochat-product-delivery-verification.md` (one row per task).

## 1. Target and honest boundary

**Goal in 4 hours:** every remaining task P1.3 → P5b.1 is *code-complete*: merged into
`delivery/integration`, solution builds, its named tests green at high severity, `aspire run`
healthy, the journeys J1–J6 pass on the scripted model and on fakes.

**Not closable by code, in any timeframe** (master plan §0.3) — built behind flags or fakes and
left `blocked`, never faked as done:
G-1 selling entity/markets · G-2 Stripe confirmation (Stripe runs against a fake adapter / test
mode) · G-3 counsel · G-4 DPA/pilot terms · G-5 design partners · G-6 LeadGenerator source review ·
G-7 live-model key (P1.8 runs only if a key is supplied) · G-8 sandbox pen test · G-9 VAT ·
G-10 demand tests A1–A9.

Working assumptions: D1–D17 and T1–T11 at their recommended values (master plan §0.1).

## 2. Remaining work (23 tasks)

| Done | In progress | To do |
|---|---|---|
| P0.1–P0.8, P1.1, P1.2 | P1.3 (E2E red: `database "supabase" does not exist`) | P1.4–P1.9, P2.1–P2.10, P3.1–P3.2, P4.1–P4.3, P5a.1, P5b.1 |

## 3. How the parallelism is made safe

1. **Seams first (Wave 0, orchestrator).** The orchestrator commits, on `delivery/integration`,
   every *shared* registration so workers never need the hot files:
   - module skeletons (Contracts + impl + `Tests/Unit`, pattern of `src/Modules/Time/`) for
     Receipts, Compute, MyData, Identity, Connections, Apps, Automations, Inbox, Discovery, Broker,
     Marketplace, all added to `DigitalBrain.slnx`;
   - their `WithModule<T>()` lines in the product profile of `AppHost.cs`;
   - one `ProductModuleEndpoints.cs` with a `Map…Endpoints()` call per module (empty bodies), wired
     once from `Program.cs`;
   - the cross-module contracts, frozen for the run: `IntentContext`, `CallerContext`,
     `ICallFilter`, `IReceipts` + `ReceiptDraft`, `IMeterSink` + `MeterEvent`, `AppManifest`
     (app.json record), `IInboxItems` + `InboxItem`, `IJobs`, `ICapabilityCatalog`,
     `IConnectionVault` (all consuming `SecretRef` from P1.1).
   A worker that needs a contract change writes it to `CONTRACT-CHANGE.md` in its worktree; the
   orchestrator applies it at merge and rebroadcasts.
2. **Hot files are owned by the orchestrator:** `AppHost.cs`, `Program.cs`, `DigitalBrain.slnx`,
   `Directory.Packages.props`, `AIClients.cs`, `ServiceDefaultsExtensions.cs`,
   `DigitalBrainRuntimeHostingExtensions.cs`. Workers request edits in `HOTFILES.md` (exact
   patch). Exception per wave in §4 (named single owner).
3. **One worktree per task:** `E:\intochat\wt\<TASK>` on branch `wt/<TASK>`, cut from the current
   `delivery/integration` head at dispatch.
4. **E2E throttle:** at most 2 concurrent AppHost-based E2E runs (named semaphore in
   `ops/orchestrate/run-e2e.ps1`); workers run unit tests freely and only *their* E2E classes.
   The full E2E suite runs only at merge checkpoints, by the orchestrator.
5. **Timebox per task: 60 min.** At the timebox a worker commits what is green and writes
   `REMAINING.md`; the orchestrator re-dispatches the remainder (fresh worker, same worktree).
6. **Max 8 concurrent workers** (32 cores / 62 GB; each worker builds its own `bin/obj`).

## 4. Waves

```
T+0:00 ─ W0 seams (orchestrator) ───────┐
       ─ W1a  P1.3fix P1.5 P1.7 P2.4 ────┼─ (no seams needed)
T+0:30 ─ W1b  P1.4 P1.6 P2.1 P2.2 ───────┘
T+1:30 ═ M1 merge + build + full unit + E2E
T+1:40 ─ W2   P1.9 P2.3 P2.6 P2.7 P2.8 P2.9 P3.1 P4.1
T+2:40 ═ M2 merge + build + full unit + E2E
T+2:50 ─ W3   P2.10 P3.2 P4.2 P4.3 P5a.1 P5b.1 P2.5 (+P1.8 if key)
T+3:40 ═ M3 final: aspire build → run → full test → journeys → code review → ledger
```

| Wave | Task | Builds against | Hot-file exception |
|---|---|---|---|
| W1a | **P1.3 fix** — test-host `supabase` database provisioning; rerun J1 E2E | master code | — |
| W1a | **P1.5** Form neuron + `show_form`/`show_view` | P1.1 | owns `WorkspaceNeuron.cs` in W1 |
| W1a | **P1.7** workspace scoping, remove unscoped `/ui` routes | P1.1 | — |
| W1a | **P2.4** persistence off local disk, T1 ADR, failed-write rollback | kernel | — |
| W1b | **P1.4** Receipts + the single price book (Compute/PriceBook) | seams | — |
| W1b | **P1.6** My Data vault + canary-secret test | seams, `SecretRef` | — |
| W1b | **P2.1** accounts, caller context, `ICallFilter` impl | seams | owns `Program.cs` auth wiring in W1 |
| W1b | **P2.2** meters, ledger (Postgres), storage sampler | seams (`IMeterSink`, `ICallFilter` contract) | owns `AIClients.cs` in W1 |
| W2 | **P1.9** renderer registry, window states, window reference | P1.5 | owns `WorkspaceNeuron.cs` in W2 |
| W2 | **P2.3** token vault, MCP bridge, Connect flow, web research | P1.6, P2.1 | — |
| W2 | **P2.6** app.json v1, Save as app, generated manifests, launcher | P1.5, P1.6, P2.1 | — |
| W2 | **P2.7** automations on durable jobs; delete Timer schedule | P2.2, `AppManifest` contract | — |
| W2 | **P2.8** Inbox v1; delete volatile inbox + banner polling | `IInboxItems`, `IJobs` contracts | — |
| W2 | **P2.9** discovery on Qdrant + gate set (60 prompts / 50 ops) | `AppManifest` contract | — |
| W2 | **P3.1** allowances, limits, alerts, statements; Stripe **fake** adapter | P2.1, P2.2 | — |
| W2 | **P4.1** broker HTTP gateway for `remote` apps, egress rules | P2.1, `AppManifest` | — |
| W3 | **P2.10** grants, consent sheet, LeadGenerator (scripted/fake sources) | P2.6–P2.9 | — |
| W3 | **P3.2** first paid capability (BackgroundRemover on a fake) | P3.1, P2.10 contract | — |
| W3 | **P4.2** catalog, certification on fakes, kill switch | P4.1, P3.1 | — |
| W3 | **P4.3** earnings ledger, payouts on Stripe Connect **fake** | P4.2 contract | — |
| W3 | **P5a.1** creator publishing of declarative apps | P4.2 contract, P2.6 | — |
| W3 | **P5b.1** `process` apps in a per-app container, no gateway/network | P4.1 | — |
| W3 | **P2.5** product profile hosting, OTel collector, backups, runbook | P2.4 | owns `AppHost.cs` in W3 |
| W3 | **P1.8** golden prompts J1/J2a (only with a live key, G-7) | P1.3, P1.5 | — |

Wave-3 tasks that depend on a same-wave task build against its **contract** and a fake; the merge
checkpoint wires them for real.

## 5. Worker prompt (identical frame, task-specific middle)

```
You are an implementation worker in worktree <WT> on branch wt/<TASK>.
Task: <TASK> from docs/superpowers/plans/2026-09-23-intochat-product-delivery.md (read §2 and the
task card; files, tests and acceptance there are binding). Working assumptions: master plan §0.1.
Frozen contracts: <list>. Do NOT edit: <hot files>, except <exception>. Request hot-file or
contract changes in HOTFILES.md / CONTRACT-CHANGE.md with the exact patch.
Rules: test-first; latest NuGet versions; no /// <summary> boilerplate, self-explanatory names,
comments only in exceptional cases; external gates stay fakes behind an adapter; name every
deletion; name the T1 migration or cite the clean-break ADR.
Verify: dotnet build DigitalBrain.slnx -p:CodeGraphRefresh=false; your unit project(s);
your E2E classes only via pwsh ops/orchestrate/run-e2e.ps1 -Filter <classes>.
Timebox 60 min. Finish: commit on wt/<TASK> ("<TASK>: <summary>"), write DONE.md (commands +
results + deletions + open items) or REMAINING.md. Never push, never touch master.
```

Dispatch (flags from `opencode run --help`, CLI 1.18.30; Context7 quota exhausted):

```powershell
git worktree add E:\intochat\wt\<TASK> -b wt/<TASK> delivery/integration
opencode run --dir E:\intochat\wt\<TASK> -m <provider/model> --auto --format json `
  --title <TASK> -f E:\intochat\wt\<TASK>\.task\<TASK>.md "Execute the attached task." `
  > E:\intochat\wt\_logs\<TASK>.jsonl
```

Each dispatch runs as a background job; the orchestrator is notified on exit.

## 6. Merge checkpoint protocol (M1, M2, M3)

1. Merge finished `wt/*` branches into `delivery/integration` in dependency order; apply
   `HOTFILES.md` / `CONTRACT-CHANGE.md`; resolve conflicts.
2. `dotnet build DigitalBrain.slnx` 0 errors → all unit projects → full IntoChat E2E.
3. Red → bisect to the offending merge, hand back to that task's worker (same worktree), continue
   the rest of the wave.
4. Code review of the wave diff (bugs, naming, boilerplate comments); fixes dispatched as micro-tasks.
5. Ledger row per task; next wave cut from the new head.

M3 additionally: `aspire do build` → `aspire run --detach` → `aspire wait IntoChat --status healthy`
→ full test → J1–J6 journey run in the browser → final review. `delivery/integration` is **not**
merged to master; the owner decides.

## 7. Risks to the 4 hours

| Risk | Handling |
|---|---|
| Big tasks (P2.6, P4.2, P3.1) overrun 60 min | MVP to acceptance tests on fakes; remainder re-dispatched |
| Merge conflicts in shared UI (Flutter shell) | one owner per hot file per wave; UI files listed per task |
| E2E contention (Docker, ports, owner's running stack) | semaphore of 2; owner's containers stopped before M3 |
| Contract churn across waves | contracts frozen in W0; changes only via orchestrator |
| Worker quality variance | review at each checkpoint; failing task goes back, not forward |
