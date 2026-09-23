# IntoChat parallel delivery — report (2026-09-23)

Plan: `2026-09-23-intochat-orchestration-4h.md` (execution) over `2026-09-23-intochat-product-delivery.md`
(master plan). Product spec: `docs/product/highlevel.md` v0.2. Branch: `delivery/integration`.

## How it was delivered

- Claude Code orchestrated headless OpenCode workers, one git worktree per task, up to 9 in parallel.
- Wave 0 (orchestrator): 11 module skeletons plus frozen cross-module contracts (`CallerContext`,
  `ICallFilter`, `IReceipt`, `MeterEvent`/`IMeterSink`/`IPriceBook`, `ISecretResolver`, `Grant`,
  `AppManifest`, `IInboxFeed`, `ICapabilityCatalog`, `ConnectionRecord`, `JobRun`), so workers could
  build in parallel without touching shared files.
- 23 planned tasks (P1.3–P5b.1) plus 13 follow-up tasks (P2.3b, P2.6b, FX1–FX13) found in merge review.
- Every merge: build, affected unit suites, targeted E2E, orchestrator code review; checkpoint runs of
  every unit and E2E project.

## Capability coverage (highlevel §8–9)

| # | Capability | Delivered on this branch | Not delivered / gated |
|---|---|---|---|
| C01 | Assistant | single `AgentNeuron`; tools selected per intent (≤ 8); plan cards for paid/app actions; receipts | plan card for every boundary kind |
| C02 | Workspace & instant UI | declarative Form neuron (date, masked secret), `show_form`/`show_view`, renderer registry with declared fallbacks, window states, first-run prompts | chart/video/browser/expander have fallbacks only |
| C03 | Receipts & traces | durable per-intent receipt with shadow Compute; idle 0 spans/min; J1 ≤ 25 spans | tail sampling only configured (hosted) |
| C04 | My Data | vault, envelope encryption, `SecretRef`, erasure (crypto-shred), owner-only endpoints, canary-secret test | real Key Vault adapter (fake) |
| C05 | Semantic types | v0 catalog, generated tool schemas, parity test | v1 types |
| C06 | Identity & consent | accounts, cookie session, caller context at trusted edges, one call filter (grants → allowances), membership filter, grants once/this chat/always with revoke, consent sheet, first-use grant prompt | — |
| C07 | Compute | meters (batched, idempotent), single price book, dedicated Postgres ledger, allowances, limits, alerts, statements, failure policy | invoicing/payments on a Stripe **fake** (G-1, G-2, G-3, G-9) |
| C08 | Discovery | manifest catalog (first-party + saved apps, workspace-scoped), `find_capability`, change-driven rebuild, unmet-intent board | semantic quality with a real embedding model unmeasured |
| C09 | Connections | Connect flow + window, Salesforce/Gmail tokens in the vault, MCP bridge allowlist, metered web research | web research/company lookup on fakes |
| C10 | Apps | `app.json`, generated manifests for every product module, install/uninstall, versions, Save as app (HTTP + assistant), reopen | — |
| C11 | Automations | durable jobs on reminders, paid steps never retried, coarse developer tools with server-owned ids, delete/uninstall, console behind server developer mode | — |
| C12 | Isolation | broker gateway for remote apps, egress per data class, declared-vs-observed diff, signed process apps in a sandbox **disabled** | enabling the sandbox needs G-8 (pen test) |
| C13 | Marketplace | catalog, certification on fakes, kill switch, governance, creator publishing, earnings ledger | payouts on a Stripe Connect **fake** (G-1, G-2, G-3) |
| C14 | Runtime | persistence in cluster storage, rollback on failed write, T1 ADR 0003 | — |
| C15 | Dev kit | ADRs, manifest guard, golden-prompt suite (env-gated), durable E2E storage option | golden prompts need a live key (G-7) |
| C16 | Inbox | structured items, grouping, SSE, approval digest | periodic digest scheduler |
| C17 | Operations | product profile, container module list generated from AppHost, OTel collector with tail sampling, backup jobs, runbook, report-a-problem | restore drill not run against a hosted deployment |

## Journeys (highlevel §10), scripted model

| Journey | Evidence |
|---|---|
| J1 Show me all customers | `AgentTableJourneyFacts` (open, refine same window, count without rows) |
| J2a Draw a card | `FormHttpFacts`, `FormFacts`, form renderer tests |
| J2b Remember my date of birth | `VaultFacts`, `CanarySecretFacts` |
| J3 LeadGenerator | `AgentAppJourneyFacts` (from chat), `LeadGeneratorFacts` |
| J4 Paid capability | `AgentAppJourneyFacts` (from chat), `PaidCapabilityFacts`, `ChaosChargeFacts` |
| J5 Save as app + grant | `SaveAsAppFacts`, `GrantRevokeFacts` |
| J6 Invited developer publishes | `PublishRemoteFacts` |

No journey has run against a live model (G-7 skipped by the owner).

## Gates that code cannot close

G-1 selling entity/markets · G-2 Stripe confirmation · G-3 counsel · G-4 data-protection pack ·
G-5 design partners · G-6 LeadGenerator source review · G-7 live-model key · G-8 sandbox pen test ·
G-9 VAT advice · G-10 demand tests A1–A9. Everything they gate runs on fakes behind flags.
Decisions D1–D17 and T1–T11 were implemented at their recommended values and are not ratified.

## Test status at the head of this branch

Head `226e082f0`, run 2026-09-23:

| Gate | Result |
|---|---|
| `dotnet build DigitalBrain.slnx` | 0 errors |
| `aspire do build` | pipeline succeeded |
| `aspire run` + `aspire wait IntoChat --status healthy` | healthy (all 52 resources running; on-demand `azure-environment` / rebuilder not started) |
| Unit, 27 projects | 578 total · 576 passed · 1 skipped (Postgres-gated ledger) · **1 failed = pre-existing** (`TimerProcessFacts`, also fails on `master`) |
| E2E, 6 projects | 110 total · 103 passed · 4 skipped (live-model golden journeys, restore drill: env-gated) · **3 failed = pre-existing** (Flutter module browser facts below; `master` fails 6 in that project) |
| `TraceBudgetFacts` | idle 0 spans/min, J1 ≤ 25 spans |
| Flutter `dart analyze` / `flutter test` (shell, ui) | 0 errors / all passed |

Regressions found and fixed during the final gates: P2.4 idle span storm (FX11), J1 span budget
(FX13), behavior crash retries stalling under the signal-driven supervisor (`226e082f0`), behavior
restart recovery in the E2E harness (FX9, FX12), first-run view hiding the chat, merge-resolution
damage to the shell and the Dockerfile module list.

## Known pre-existing failures (also fail on `master`)

- `TimerProcessFacts.FileBasedProductionClientReportsOneRealTimerAndExits` (Time unit).
- `ButtonWebFacts`, `ExpanderWebFacts`, `ComponentRenderingWebFacts` (Flutter module E2E): the module
  host has no workspace/app surface, and chart/video/browser/expander render as declared fallbacks.
  **Owner decision:** build generic neuron-window rendering, or retire these facts.

## For the code review and refactoring round

- Keyword heuristics in `AgentToolSelection` (tool selection per intent) are brittle.
- Two certification/kill-switch implementations: `Marketplace` and `Marketplace.Creators` (P4.2 vs P5a.1).
- `HttpRemoteAppTransport` is JSON, not a full MCP `tools/call` handshake.
- `ITimer`/`TimerNeuron` still live in the Time assembly (the product composes `TimeModule` for reminders).
- `GrainDocumentStore` index update is not atomic with the document write.
- Grants: `CanAccess` trusts member records; account-wide ownership inference is implicit.
- Full follow-up ledger with dispositions: see the orchestration notes (F1–F26) summarised above.
