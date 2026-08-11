# CODEX HANDOFF — DigitalBrain Stage 1 "Stabilize and strangle" (continue implementation)

You are taking over as orchestrator + implementer of an in-progress refit on branch
`stage1-stabilize-strangle`. An external orchestrator drove grok CLI worker sessions through most
of Stage 1 with a RED (characterize) → GREEN (replace) → GRILL (adversarial review) → gate →
commit protocol. You continue with the same discipline.

## Read first, in this order
1. `GROK.md` (repo root) — standing orders: commands, the 9 kernel traps, banned patterns,
   report format. These are YOUR standing orders too — ignore the filename.
2. `plans/RATIFIED-PRODUCT-DEFINITION.md` — binding scope (IS / IS-NOT, ratified decisions,
   §1.18 SDK webhook-rail amendment, §4 items 1–4 ratified: no-ambient-authority scripts,
   replay-pinned-artifact recovery, FIFO turns, Orleans spike as exit test).
3. `plans/GROK-ORCHESTRATION-STAGE1.md` — stage plan (§3 iteration map + exit criteria, §4 Stage 2–4 outline).
4. `plans/stage1/janitor-backlog.md` — living backlog incl. deferred GRILL MAJORs + flake tickets.
5. `plans/stage1/reports/` — every seam has RED/GREEN/GRILL reports; the newest truth is there.

## Hard rules that already burned sessions
- Build: `dotnet build DigitalBrain.slnx` → must be 0 warnings (warnaserror).
  Test: `& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe`.
  **NEVER `dotnet test`** — its testing-platform handshake fails against this xunit.v3 exe.
  Full gate: `pwsh scripts/gate.ps1` (`-Flutter` to include the Flutter packages).
- Run tests WITHOUT the aspire stack. STOP any running AppHost before building (file locks).
- Known ticketed flakes (single occurrence + clean rerun = not a failure):
  `ChartVocabularyProofs.EmittedChartPointLandsOnItsBoundChart` (25s graph-lookup under load)
  and the silo-restart proofs under parallel pressure.
- Per change: pin current behavior → minimal green → ADVERSARIAL self-review of your own diff
  against GROK.md traps + the ratified constraints (independent grills rejected 3 of 5 seams on
  first try — hold that bar) → gate → commit on `stage1-stabilize-strangle`.
- Never touch wire aliases; no new packages unless a brief explicitly grants it; TDD always.
- Vlad is merge authority to master. If a rule conflicts with reality: STOP, write the conflict
  into a report — a written refusal beats silent improvisation.

## State (all committed; last clean full run 165/165 green)
- **S1.0**: harness (GROK.md, `scripts/gate.ps1`, briefs/reports scaffolding); unrestorable
  Aspire pins fixed (26405.3 → cached 26376.5; `Aspire.Azure.Storage.Queues` → 13.4.6 stable).
- **J1**: `WantsTimeButton`/`ShowTime` keyword god-switch + demo transform deleted;
  `Responded.Author` threaded on every reply path.
- **S1.2 (identity)**: ASP.NET Identity cookie auth at the Host (table-storage user store,
  `/auth/bootstrap|login|logout|me|users`), `WorkspaceNeuron` `db.workspace.*` (Owner/Admin/
  Builder/Viewer, last-Owner invariant, actor-stamped audit), every endpoint authenticated
  (fallback policy), principal-scoped chat AND surface identity (client names only select among
  the caller's own), unconditional HTTPS-beyond-loopback, Development-only loopback bypass,
  durable actor stamps. **P0-3, P0-4 dead.**
- **S1.3 (OAuth/Integration rail)**: single PKCE S256 mint (manual non-PKCE path deleted),
  durable bounded one-shot expiring principal-bound states (no static dict), tokens keyed
  `integration/user/{provider}/{principal:N}`, Begin/Claim/Open principal-bound with uniform
  not-pending refusals, secrets removed from every ClientEntryPoint surface
  (`IMcpAuthorizationCodes` host-only), forged model/client Actor stripped + verified principal
  stamped (RequestContext hop Chat→Agent→fire), destructive-tool blanket rejection removed with
  actor+IntegrationSubject audit. **P0-1, P0-5, P0-7 dead.**
- **S1.4 (Execution kernel)**: `DigitalBrain.Modules.Tasks` → `DigitalBrain.Modules.Execution`
  (no compat layer), hybrid `Apply`/`Read` (+versioned Cancel), attempt-stable operation keys,
  bounded receipts/ledger, `OutcomeUncertain` never auto-retries on ANY admission path,
  `ResolveOperation` reconciliation, worker liveness. Spike matrix green (restart, blocker
  wait/resume, cancel, duplicate submission, uncertain write). **P0-8 dead.**
- **S1.5 (durable turns)**: chat turn = Execution (first production adapter), FIFO one-active
  per conversation, POST streams as pure observer (abort detaches, never cancels — **P0-2
  dead**), versioned idempotent `chat.cancel-turn`, MAF session safe-point persistence (**P0-6
  dead**), Execution→origin terminal bridge (wake-up only, chat re-Reads the kernel; forged
  `ExecutionTerminal` ignored settled; revision-idempotent re-apply), activation reconcile +
  DelayDeactivation, pure worker-death liveness (`WorkerAbandoned`), Waiting surfaced + durable
  policy deadline unfreezes FIFO, worker grain-type allow-list.
- **S1.6 (Gmail strangler)**: typed Gmail path fully deleted (GmailAuthRail, planner,
  DurableGoogleTokenStore, typed contracts, `Google.Apis.*` pins), Gmail = `McpServerDefinition`
  `google.gmail` through the generic rail (Google PKCE defaults), dual-key parity theories green
  for salesforce + google.gmail, live-endpoint verification deferred to the exit smoke.

## Step 0 — the in-flight janitor (DO THIS FIRST)
A grok JANITOR session (brief `plans/stage1/briefs/J-batch.md`) was mid-flight at handoff with
UNCOMMITTED working-tree changes (empty Salesforce.Contracts deletion + slnx edit, stale
docker-compose module env vars, .gitignore hygiene, ChatButtons dead helpers, ChatTools
'show-time' string, Streams-usage verdict, restart-proof/chart-lookup flake hardening, two
`Responded.Author` coverage pins). Check `Get-Process grok`: if still running, let it finish
writing `plans/stage1/reports/J-batch.md` (kill only if >20 min with zero file writes). Then
audit `git status` / `git diff` against the brief, finish or correct whatever is incomplete
YOURSELF, run the gate TWICE consecutively (stability proof), commit as `J-batch: ...`.

## Remaining Stage-1 work (in order)
1. **Flutter lane**: `flutter analyze` + `flutter test` for core/kit/shell; fix the
   pre-existing core `activateControl` test drift; replace `show-time` sample ids in kit
   fixtures; kit never imports core/shell.
2. **Docs pass**: update `CLAUDE.md` (Tasks→Execution, W2 done, god-switch gone, test-exe
   command + gate script, identity/auth reality, pointer to GROK.md + plans/) and stale
   `UNIFIED-ARCHITECTURE.md` mentions. Honest, short, current.
3. **Stage-1 exit audit** against §3 exit criteria in `plans/GROK-ORCHESTRATION-STAGE1.md`:
   all nine P0s closed with regression tests (grep `PIN-DEFECT` = zero), spike matrix green
   through the chat surface, gate stable twice, backlog resolved-or-reasoned, zero
   god-switches / unauthenticated endpoints / client-trusted identity.
4. **AppHost smoke** (the one live-stack item): `dotnet run --project
   src/Kernel/DigitalBrain.AppHost` (Docker: Azurite/Qdrant/Ollama), verify health,
   bootstrap → login → two users → isolated chats over real HTTP+SSE, Salesforce + Gmail MCP
   sign-in reachable (real OAuth needs the owner present — record verified vs deferred).
   STOP the stack before any further builds.
5. Write `plans/stage1/STAGE1-EXIT.md`: checklist with evidence links, the LTS-vs-preview
   recommendation (open item §4.6 of the ratified definition), residual backlog carried forward.
6. **Then Stage 2** per orchestration plan §4, one seam per iteration, briefs+reports under
   `plans/stage2/`: Conversation module extraction (ratified D1–D6: durable canonical messages,
   `UI ─► Conversations ◄─ AI`, one MAF session per Conversation×Agent, provider-neutral
   `IConversationResponder`, exactly one `role:responder`, Chat dissolves into ConversationNeuron
   + Flutter projection), SDK rails formalization (Authorization/OAuth folder, **webhook ingress
   rail** per amendment §1.18 — the kept webhook slice is its seed, X/Twitter is the eventual
   first consumer, Behavior work itself stays Stage 3), then the deferred consolidation and
   graph-rename decisions.

## Backlog you must not lose (full list in janitor-backlog.md)
S1.2 deferred MAJORs (bootstrap atomicity via ETag if-not-exists, invitations = admin-create
MVP, MCP `ChatTools`/`ReadChatTranscript` bare chatName principal scoping, `/orleans` dashboard
ACL stance, `"dev"` owner constant rename), S1.5 riding findings (observer `afterSequence: 0`
full journal scan, tool safe-point residual double-execute window, `AttemptAccepted` outbox
lag), S1.3 residuals (failure audit, pin strength), Streams/PubSub removal after the J5 verdict.
