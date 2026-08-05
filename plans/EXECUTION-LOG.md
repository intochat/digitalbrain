# Execution log

## Wave 0 — planning
- Generated PLAN-2000.md (2000 items), BATCHES.md (40×50), TRACE.md (50 RED).
- Policy: max ~10 concurrent agents per wave; not 1000 concurrent.

## Wave B — Stage-1 mock platform skeleton (Batch 05+)
- **Status:** GREEN
- **Projects:** `src/DigitalBrain.Mocks/`, `src/DigitalBrain.Mocks.Tests/` (added to `DigitalBrain.slnx`)
- **Mocks (Neuron + sealed synapses, no network):**
  - `MockX` ← `ObserveXPost` → emits `XPostObserved`
  - `MockGmail` ← `ObserveEmail` → emits `EmailReceived`
  - `MockCrypto` ← `ObserveSpot` → emits `SpotSnapshot`
  - `MockSalesforce` ← `ProposeAccountEnrichment` → emits `AccountEnrichmentProposed`
- **Composition helper:** `DigitalBrainTestBuilder.ComposeMocks()` in `MockComposition.cs`
- **E2E smoke:** `MockXSmokeTests` — session `Emit(ObserveXPost)` → `MockX` → declared `XPostObserved` → `MockDashboard` hears; journals prove declared delivery + Cause chain (Planner/Diary pattern).
- **Gates (quoted):**
  - `dotnet build DigitalBrain.slnx -c Release` → 0 errors
  - `dotnet test src/DigitalBrain.Mocks.Tests -c Release` → **1 passed**
  - `dotnet test src/DigitalBrain.Core.Tests -c Release` → green (count grew with parallel proofs; scenario 02 later unblocked — see below)
- **Note:** scenario 02 initially red (empty dashboard journal) under concurrent work; fixed under Scenario 02 entry (catalog listeners for ambient emits). Local `XPostObserved` in Core.Tests scenarios still duplicates MockX vocabulary — merge later if shared composition is required.
- **Core untouched** by mock skeleton (no Core API changes).

## Scenario 02 — Elon X post → crypto dashboard
- **Status:** GREEN
- **Files:**
  - `src/DigitalBrain.Core.Tests/Scenarios/ElonXPostCryptoDashboardModule.cs` — inline mock neurons + sealed synapses (2-coin track set BTC/ETH)
  - `src/DigitalBrain.Core.Tests/Scenarios/ElonXPostCryptoDashboardTests.cs` — DisplayName from scenario title; journals are the proof
- **Choreography proven:** session `Emit(XPostObserved)` → `TopicRouter` (declared) classifies → `DashboardAnnotateAsked` → `CryptoDashboard` asks `SpotSnapshotAsked` → `CryptoMarket` answers → dashboard says 2× `ChartPointAppended` + `ChartAnnotationAdded` (description includes post excerpt + moves) → `ChartRenderer` hears ambient chart facts.
- **Root cause of earlier empty-dashboard timeout:** ambient facts with no `INeuron<>`/`IAnswers` declaration are not in the catalog; `Emit` throws on `KindOfFact` mid-turn, so the router journals nothing. Fix: bodiless catalog listeners (`MarketSignalLedger`, `ChartRenderer`).
- **Note:** vocabulary is local to Core.Tests (not yet folded into `DigitalBrain.Mocks`); MockX smoke already owns a separate `XPostObserved` shape — merge later if shared composition is required.
- **Gates (quoted):**
  - `dotnet test src/DigitalBrain.Core.Tests -c Release` → **20 passed / 0 skipped**
  - `dotnet test DigitalBrain.slnx -c Release` → **21 passed / 0 failed** (Core.Tests 20 + Mocks.Tests 1)
- **TRACE:** scenario 02 → GREEN

## Scenario 01 — Gmail → web search → Salesforce enrichment
- **Status:** GREEN
- **Mocks extended (DigitalBrain.Mocks):**
  - `MockWebSearch` — `WebSearchRequested` / `WebSearchCompleted` (IAnswers, deterministic, no network)
  - `MockSalesforce` — also emits optional `AccountEnriched` after `AccountEnrichmentProposed`
  - `MockComposition.ComposeMocks` includes `MockWebSearch`
- **Scenario modules:** `AccountEnricher` (EmailReceived → Ask web search → ProposeAccountEnrichment), `EnrichmentDesk` (catalog sink for CRM ambient facts)
- **Proof (journals):** session `Emit(ObserveEmail)` → MockGmail `EmailReceived` (Cause=ObserveEmail) → AccountEnricher `WebSearchRequested` (Cause=EmailReceived, via=ask) → MockWebSearch `WebSearchCompleted` (Answers=ask) → `ProposeAccountEnrichment` (Cause=WebSearchCompleted) → MockSalesforce `AccountEnrichmentProposed` + `AccountEnriched` (Cause=Propose) → desk hears both declared
- **DisplayName:** "Gmail inbound -> web research -> Salesforce account enrichment"
- **Files:** `src/DigitalBrain.Mocks/MockWebSearch.cs`, `MockSalesforce.cs`, `MockComposition.cs`; `src/DigitalBrain.Core.Tests/Scenarios/GmailWebSearchSalesforceEnrichment{Module,Tests}.cs`
- **TRACE:** scenario 01 → GREEN

## Scenario 03 — What did I do last week?
- **Status:** GREEN
- **Modules:** `WeekRecall` (`Neuron` with `WeekRecallState`) hears `WeekEmailLogged` / `WeekMeetingLogged` / `WeekTaskLogged` into durable State; answers `WeekSummaryAsked` → `WeekSummary`
- **Proof (journals = source of truth):** seed 4 domain facts; `Brain.ReadAsync` builds item list from heard journal structure; Ask range "last week" returns 3 items; summary items equal journal-derived in-range items (titles/kinds/timestamps); out-of-range email excluded
- **DisplayName:** "What did I do last week? - timeline from journaled domain facts"
- **Files:** `src/DigitalBrain.Core.Tests/Scenarios/WhatDidIDoLastWeek{Module,Tests}.cs`
- **TRACE:** scenario 03 → GREEN

## Scenario 04 — Why did you do it this way?
- **Status:** GREEN
- **Modules:** `InstructionalAgent` (UserInstruction → State; PerformOutboundAction → AgentActionTaken; WhyAsked → WhyAnswer); `ActionLedger` catalog sink
- **Proof (journals):** UserInstruction heard first; later AgentActionTaken with Cause=PerformOutboundAction; instruction position before action position on agent + session journals; WhyAnswer.InstructionText/Scope equal re-read of journaled UserInstruction body (no free-form rationale without that fact)
- **DisplayName:** "Why did you do it this way? - cite prior journaled user instruction"
- **Files:** `src/DigitalBrain.Core.Tests/Scenarios/WhyDidYouDoItThisWay{Module,Tests}.cs`
- **TRACE:** scenario 04 → GREEN

## Gate after S01/S03/S04
- **Quoted:** `dotnet test DigitalBrain.slnx -c Release` → **34 passed / 0 failed** (Core.Tests 33 + Mocks.Tests 1)
- Note: concurrent WIP previously `Compile Remove`d these scenarios from the csproj; S01/S03/S04 are live (not excluded). NestedAsks needs `AssistantLedger` for ambient `AssistantSaid` Emit (S02 catalog trap).

## Wave 1 — Batch 01 (items 1–50, Core P0–P17 proofs)
- Started: parallel agents on Connect, DeliveryFailed, self-proxy, Schedule, zero-receiver, AskExpired.

### P03 connect wiring (items 7–9)
- **Status:** GREEN — Connect path, table, and StageSaid routing already correct; no Core change required
- **Files:** `src/DigitalBrain.Core.Tests/ConnectionModule.cs`, `ConnectionWiringTests.cs` (`ConnectWiresInstanceAndSuppressesGhost`)
- **Proof:** edge `Send(Connect("stagesaid", stageaudience/dashboard))` → emitter connection table holds the row → `Emit(PlanDay)` → speaker says `StageSaid` with `to:[{stageaudience/dashboard, via:connected}]` only → connected instance hears; same-context ghost journal empty.
- **Timeout root cause (prior multi-module attempt):** not grain addressing or drain — proof must (1) use catalog fact kind strings (`stagesaid`, not type names), (2) wait on committed `Connections` before Emit, (3) assert said `to[]` + receiver journals, not a single Wait that races the drain.

### P04 ghost-suppress target kind only (items 10–12)
- **Status:** GREEN — ghost rule is kind-scoped (`redirectedKinds` from connection targets only)
- **Files:** `ConnectionWiringTests.cs` (`GhostSuppressesOnlyConnectedKind`); modules `StageAudience` + `StageArchive` both hear `StageSaid`
- **Proof:** Connect F→`stageaudience/foreign` suppresses only kind `stageaudience` from declared fan-out; `stagearchive@context` still receives `via:declared`; ghost `stageaudience@context` silent; foreign hears `via:connected`.

### P28 ConnectionRefused bad kind (items 82–84)
- **Status:** GREEN — `ConnectRefusalOf` + directed `ConnectionRefused` already correct
- **Files:** `ConnectionWiringTests.cs` (`ConnectionRefusedOnBadKind`); `SilentPeer` (catalog kind, no `INeuron<StageSaid>`)
- **Proof:** `Send(Connect(stagesaid, silentpeer/nowhere))` → session hears `ConnectionRefused` (reason: does not declare) → emitter said refusal `via:ask` to session → emitter `Connections` empty (table untouched).

### P13 schedule tick self-deliver (items 37–39)
- **Status:** GREEN
- **Files:** `src/DigitalBrain.Core.Tests/PulseModule.cs`, `ScheduleTests.cs` (`ScheduleTickTests`)
- **Proof:** `StartPulse` → in-turn `Schedule(Tick, period)` → `Clock.AdvanceAsync(period)` → heard `Tick` (self-sourced, Cause = schedule said ref) → handler `Emit(PulseBeat)` reaches `PulseObserver` via declared — ordinary turn, no second bus.
- **Gate:** `dotnet test DigitalBrain.slnx -c Release` green (20 pass / 1 explicit skip).

### P15 ScheduleFailed consecutive (items 43–45)
- **Status:** GREEN — consecutive-failure mechanism already in `Neuron.Schedule.cs` (`DeliveryPolicy.ScheduleFailureLimit = 5`)
- **Files:** `ScheduleTests.cs` (`ScheduleFailedTests`); bonus `ScheduleUnscheduleTests` proves `Unschedule` stops further ticks
- **Proof:** throwing tick handler → exactly one said `ScheduleFailed` with `ConsecutiveFailures == 5` and reason; further advances mint no second terminal (row removed).
- **No gap:** did not fall back to survive+Unschedule-only path.

### P16 AskExpired horizon (items 46–48)
- **Status:** GREEN — horizon machinery existed (`ExpireAsksAsync`, `AskHorizon = 2×RetryHorizon`); one Core fix required for live pin sweep
- **Gap found:** `ScheduleDrain` armed only while `unsettled.Count > 0`, so after the question settled the 50ms drain stopped and `ExpireAsksAsync` waited solely on the 1-minute `OutboxWakeup` reminder — `TestClock.AdvanceAsync(AskHorizon)` alone could not surface `AskExpired` while activated.
- **Minimal fix:** `Neuron.Dispatch.cs` — keep/arm drain timer while `journal.HasAskPins`; dispose only when outbox empty **and** no pins. Expire path, pin release, late-reply no-dispatch already correct.
- **Files:** `AskHorizonModule.cs`, `AskExpiredTests.cs`; Core `Neuron.Dispatch.cs`
- **Proofs:** (1) neuron `Ask` → advance past horizon → said `AskExpired` → late `ProbeReply` journals heard, no `ProbeContinued`; (2) session `AskAsync` throws `AskFailedException` with `AskExpired` body.
- **Explicit skip:** none for P16.

### P09 DeliveryFailed no-answerer (items 25–27)
- **Status:** GREEN — path already in `Neuron.StageSaid` (`AskLacksAnswerer` → said `DeliveryFailed` reason `no-answerer`, Attempts 0)
- **Files:** `src/DigitalBrain.Core.Tests/DeliveryFailedTests.cs` (`AskWithNoAnswererJournalsDeliveryFailed`)
- **Proof:** empty composition (Core vocabulary only); session `AskAsync` of a non-answered Core fact → `AskFailedException` whose `Fact` is `DeliveryFailed`; session journal holds said question with `to:[]` and exactly one said `DeliveryFailed` with `Fact == askRef`, reason `no-answerer`, attempts 0 (no horizon burn).
- **No Core change.**

### P10 DeliveryFailed unknown kind terminal attempt 1 (items 28–30)
- **Status:** GREEN — drain catalog pre-check already terminal on attempt 1
- **Files:** `DeliveryFailedTests.cs` (`UnknownKindFailsTerminalOnAttemptOne`)
- **Proof:** session `SendAsync` to `NeuronId("ghost", …)` → wait for said `DeliveryFailed` on sender; `Attempts == 1`, reason names missing kind + catalog; original said retains `via=ask` to ghost.
- **No Core change.**

### P12 self-proxy throw (items 34–36)
- **Status:** GREEN — `OutgoingSynapseFilter` already throws naming the self-delivery rule
- **Files:** `src/DigitalBrain.Core.Tests/SelfDeliveryFilterTests.cs`
- **Proof:** filter unit with stub `IOutgoingGrainCallContext` (same `GrainId` source/target → `InvalidOperationException` containing "proxied self-call" / "self-delivery", `Invoke` never runs; different target proceeds). Catalog notes allow filter unit when fixture has no grain-factory surface for a non-Neuron probe.
- **No Core change.**

### P17 zero-receiver Emit legal (items 49–51)
- **Status:** GREEN — empty fan-out already legal in `StageSaid` / drain unsettled index
- **Files:** `src/DigitalBrain.Core.Tests/ZeroReceiverEmitTests.cs`
- **Proof:** session `EmitAsync` of a Core fact with no listeners → said entry with empty `To`, no throw, no hang, no `DeliveryFailed`.
- **Gate:** `dotnet test src/DigitalBrain.Core.Tests -c Release` — 19 pass / 1 explicit skip (unrelated scenario WIP) / 0 fail.
- **No Core change.**

### P08 / handler-throw zero durable trace (catalog P07 physics)
- **Status:** GREEN — DeliverCoreAsync catch ClearTurn + rethrow; commit never runs; sender retry lands once after recovery
- **Files:** `HandlerThrowModule.cs`, `HandlerThrowTests.cs` (`FragilityGate` DI singleton, `FragileReceiver` stages Emit then throws)
- **Proof:** session `Send(FragileWork)` while refuse=true → receiver journal empty (no heard, no side-effect said), session holds unsettled said via=ask, no DeliveryFailed; refuse=false → exactly one heard FragileWork + one said FragileSideEffect (declared to observer); still one after settle window
- **No Core change.**

### P11 DeliveryFailed listenable self-heal spine
- **Status:** GREEN — StageSaid fans Core DeliveryFailed to INeuron listeners; alternate path is composition
- **Files:** `DeliveryFailedHealModule.cs` (`FailureHealer`, `FailureHealObserver`, `HealedPath`), `DeliveryFailedHealTests.cs`
- **Proof:** session `Send` to missing kind → said DeliveryFailed via=declared to failurehealer → healer hears + says HealedPath → observer hears; journals prove Source/Sequence and failure payload
- **No Core change.** (Grain name FailureHealer avoids collision with scenario HealRouter.)

### P35 Session directed Send exact receiver
- **Status:** GREEN — Session.SendAsync AppendSaid with single via=ask receiver
- **Files:** `DirectedSendTests.cs` (reuses StageSpeaker/Audience/Archive)
- **Proof:** Send StageSaid to stageaudience only → said.To Single via=ask; archive silent; Emit PlanDay contrast → speaker said.To has both declared listeners; archive hears only Emit path
- **No Core change.**

### P23 RequireSerializedTurns refuses Reentrant/MayInterleave
- **Status:** GREEN — unit against NeuronConcurrency.RequireSerializedTurns
- **Files:** `NeuronConcurrencyTests.cs` (file-local attribute carriers, not Neuron grains — avoids Orleans GrainType collision)
- **Proof:** Reentrant and MayInterleave types throw InvalidOperationException naming the attribute and "serialized turns"
- **No Core change.**

### Gate (quoted)
- `dotnet test DigitalBrain.slnx -c Release` → **34 passed / 0 failed** (Core.Tests 33 + Mocks.Tests 1)

### Collateral (gate hygiene, not fake green)
- NestedAsks compose must include `AssistantLedger` (S02 catalog trap for Emit AssistantSaid)
- ScheduleUnschedule race: sample tick count only after Unschedule is durable

## Nested asks (sc37 spine) + P32 locus isolation
- **Status:** GREEN
- **Owned files:**
  - `src/DigitalBrain.Core.Tests/NestedAsksModule.cs` — RecallChat (INeuron UserAsked + INeuron MemoryHit) + EpisodicMemory (IAnswers MemoryQuery→MemoryHit); no Answer<>
  - `src/DigitalBrain.Core.Tests/NestedAsksTests.cs` — DisplayName states the law: open pin (via=ask), Answers stamp, INeuron continuation, AssistantSaid journals on both neurons
  - `src/DigitalBrain.Core.Tests/LocusIsolationTests.cs` — P32 DisplayName exact: parallel sessions a/b never deliver declared fan-out into each other's journals; empty journal on diary@b after emit on a
- **Physics proven:**
  1. session Emit(UserAsked) → chat Ask<MemoryHit>(MemoryQuery) → memory answers → chat HandleAsync(MemoryHit) Emit(AssistantSaid); Answers ↔ ask pin; both journals
  2. PlanDay@a → diary@a only; diary@b and planner@b empty; mutual isolation after emit@b
- **No Core change** — Ask/IAnswers/continuation and Name-locus fan-out already correct
- **Gates (quoted):**
  - `dotnet test DigitalBrain.slnx -c Release` → **34 passed / 0 failed** (Core.Tests 33 + Mocks.Tests 1)
- **TRACE:** scenario 37 → GREEN (spine proof; full nested vector/transcript product remaining); P32 GREEN


## Scenarios S09 / S35 / S36 / S43 — journal-proof acceptance
- **Status:** GREEN
- **Files (`src/DigitalBrain.Core.Tests/Scenarios/`):**
  - **S35** `SelfHealDeliveryFailedModule.cs` + `SelfHealDeliveryFailedTests.cs` — MeetingSummarizer Ask`PostSlackSummary` (catalogued via SlackUnavailable INeuron, no IAnswers) → Core `DeliveryFailed(no-answerer)` → HealRouter `INeuron<DeliveryFailed>` → RecoveryAttempted + EmailSummaryReady → EmailFallback → EmailDispatched + RouteHealed
  - **S36** `ScriptReactAllEmailModule.cs` + `ScriptReactAllEmailTests.cs` — MockGmail `ObserveEmail` → `EmailReceived` → InvoiceCatcher (Invoice subject → TaskCreated + BehaviorNudge); non-invoice silent
  - **S09** `CrossModuleCorrelationModule.cs` + `CrossModuleCorrelationTests.cs` — one session `OpenWorkThread` → WorkThreadOpened → OpportunityLinked → EmailThreadAttached → ThreadTimelineReady; Cause Source/Sequence chain asserts one thread
  - **S43** `AdversarialPromptInjectionEmailModule.cs` + `AdversarialPromptInjectionEmailTests.cs` — injection body → ContentUntrusted (Source=TrustTagger unforgeable) → InjectionFollower EgressSendRequested → CapabilityDenied; owner-confirmed EgressSendRequested still EgressDispatched
- **Core change:** none for these four (composition + journals only). Core.Tests references DigitalBrain.Mocks for MockGmail.
- **Gates (quoted):**
  - `dotnet test DigitalBrain.slnx -c Release` → **34 passed / 0 failed** (Core.Tests 33 + Mocks.Tests 1)
- **TRACE:** 09, 35, 36, 43 → GREEN

## Scenarios S05 / S29 / S38 / S50 — journal-proof acceptance
- **Status:** GREEN
- **Files (`src/DigitalBrain.Core.Tests/Scenarios/`):**
  - **S05** `InstallCsharpBehaviorScript{Module,Tests}.cs` — Stage-1 honest (composition includes `VipEmailToTask`, not ALC hot-load): `BehaviorInstallProposed` → `BehaviorCatalog` says `BehaviorActivated` → VIP `ObserveEmail`/`EmailReceived` → `TaskCreated` + `BehaviorNudge`; non-VIP silent. Reuses S36 `TaskCreated`/`TaskStore`/`UiProjector` vocabulary.
  - **S50** `DayInLifeMorningBrief{Module,Tests}.cs` — four section mocks hear `MorningBriefRequested` and emit `WeatherReady`/`CalendarReady`/`InboxReady`/`PortfolioReady`; `MorningBriefAssembler` joins in durable `TState` → `MorningBriefReady` with all four sections. Incomplete proof: directed `Send` pins BriefId without mock fan-out; three legs leave Ready empty; fourth leg closes join.
  - **S29** `LongRunningResearchProgressiveUi{Module,Tests}.cs` — `ResearchJobRequested` → `ResearchJobStarted` + sequential Ask pipeline then mentions → ≥2 ordered `ResearchJobProgress` → `ResearchJobCompleted`; `ResearchJobUiProjector` hears Progress in position order. Types prefixed `ResearchJob*` to avoid collision with S30 `ResearchStarted`/`ResearchProgress` shapes.
  - **S38** `RichMultimodalAssistantResponse{Module,Tests}.cs` — one `MultimodalUserAsked` turn emits four separate synapses (`AssistantText`, `ChartSpec`, `ImageRef`, `ButtonOffer`) sharing Cause; `ShellMultimodalLedger` hears all four kinds.
- **Core change:** none (composition + journals only). Ambient Emit requires catalog listeners (S02 trap).
- **Gates (quoted):**
  - `dotnet test` four classes → **6 passed / 0 failed**
  - `dotnet test DigitalBrain.slnx -c Release` → **44 passed / 0 failed** (Core.Tests 43 + Mocks.Tests 1)
- **TRACE:** 05, 29, 38, 50 → GREEN

## Scenarios S07 / S30 / S46 / S47 — journal-proof acceptance
- **Status:** GREEN
- **Files (`src/DigitalBrain.Core.Tests/Scenarios/`):**
  - **S07** `MultitoolTurnApprovalGate{Module,Tests}.cs` — DisplayName: *"Multi-tool turn: tools complete only after UserApproved; Cause chain holds"*. **Law:** side-effect tools do not complete before a deferred `UserApproved` turn; after grant both tools complete and Cause chains from the approval said row (never same-turn await of approval).
  - **S30** `MidstreamCorrectCancelReplan{Module,Tests}.cs` — DisplayName: *"Midstream cancel: old-generation Progress freezes; ReplanStarted new generation"*. **Law:** `TState` generation counter + scheduled pulse; cancel journals `CancelableResearchCancelled(gen1)` + `CancelableReplanStarted(gen2)`; gen1 Progress count freezes; further Progress only for gen2 (positions after cancel).
  - **S47** `PubSubManyDashboards{Module,Tests}.cs` — DisplayName: *"Pub-sub: many dashboards hear the same IncidentOpened Source/Sequence by declaration"*. **Law:** one ambient `IncidentOpened` fans to ≥2 declared kinds at the same locus; both journals share identical Source + Sequence; producer names nobody.
  - **S46** `ReminderWakesDormant{Module,Tests}.cs` — DisplayName: *"Reminder wakes dormant neuron after 30d: schedule survives deactivation"*. **Law:** `Schedule(ContractReviewDue, 30d)` survives `DeactivateAsync`; after `TestClock.AdvanceAsync(30d)` reactivation delivers self-sourced due tick (Cause = schedule said ref) → `ContractReminderSurfaced`; one-shot `Unschedule` prevents re-fire.
- **Core change:** none (composition + journals only). Ambient Emit requires catalog listeners (S02 trap). S30 types prefixed `Cancelable*` to avoid collision with S29 `ResearchJob*` vocabulary.
- **Collateral compile fixes (not new claims):** `DayInLifeMorningBriefTests` Ambiguous Assert.Contains; `InstallCsharpBehaviorScriptTests` unused using.
- **Gates (quoted):**
  - four methods → **4 passed / 0 failed** (1.764s)
  - `dotnet test DigitalBrain.slnx -c Release` → **44 passed / 0 failed** (Core.Tests 43 + Mocks.Tests 1)
- **TRACE:** 07, 30, 46, 47 → GREEN

## Scenarios S13 / S31 / S42 / S48 — journal-proof acceptance
- **Status:** GREEN
- **Files (`src/DigitalBrain.Core.Tests/Scenarios/`):**
  - **S31** `CryptoStopLossSocialSignal{Module,Tests}.cs` — DisplayName: *"Crypto stop-loss: XPostObserved panic → StopLossArmed → PriceTick breach → StopLossTriggered → OrderFilled"*. Reuses S02 `XPostObserved`. RiskPolicy TState arms on panic+asset; journals `StopLossArmed`; breach `PriceTick` → one `StopLossTriggered` (journal gate blocks double-sell); PortfolioBroker → `OrderFilled`. Adversarial: benign social + low price alone never arms/triggers.
  - **S48** `WhySalesDroppedMultichart{Module,Tests}.cs` — DisplayName: *"Why sales dropped: seed MetricObserved first → SalesDropAsked → ≥2 ChartSpec + WhySalesAnswer cites journaled metrics"*. S04 pattern: metrics journaled before ask; 3× `ChartSpec` (reuses S38 type) + `WhySalesAnswer` whose CitedMetricIds/Narrative equal re-read `MetricObserved` bodies; metric positions precede charts/answer.
  - **S13** `MultideviceSessionHandoff{Module,Tests}.cs` — DisplayName: *"Multi-device handoff: phone + desktop sessions at different names ReadAsync the same work neuron and see the same facts — not forked work identities"*. Work at `workthread/northwind-renewal`; phone/desktop sessions directed `Send` only; both `Brain.ReadAsync` identical journal; session journals hold said rows not forked work copies; `workthread@device-*` empty.
  - **S42** `SharePaneNotJournals{Module,Tests}.cs` — DisplayName: *"Share pane not journals: SharePaneRequested → SharedProjection redacted; guest never hears OwnerPrivateNote"*. Owner secrets journal on gateway+audit; guest declares only `SharedProjection`; projection body is headline metric only (no secret); guest journal single kind; `OwnerPrivateNote` To[] never includes guest.
- **Core change:** none (composition + journals only). Ambient Emit needs catalog listeners (S02 trap). S48 reuses `ChartSpec` from S38; S31 reuses `XPostObserved` from S02.
- **Gates (quoted):**
  - `dotnet test src/DigitalBrain.Core.Tests -c Release` → **53 passed / 0 failed**
  - `dotnet test DigitalBrain.slnx -c Release` → **54 passed / 0 failed** (Core.Tests 53 + Mocks.Tests 1)
- **TRACE:** 13, 31, 42, 48 → GREEN

## Scenarios S08 / S11 / S21 / S32 — journal-proof acceptance
- **Status:** GREEN
- **Files (`src/DigitalBrain.Core.Tests/Scenarios/`):**
  - **S08** `CalendarConflictEmailSend{Module,Tests}.cs` — `MeetingScheduleAsked` → `CalendarConflictDetected` + `ConflictResolutionsProposed` (Reschedule|DeclineEmail); deferred `ConflictResolutionChosen`: DeclineEmail → `DeclineEmailDrafted` → mock `ConflictDeclineMailer` → `DeclineEmailSent`; Reschedule path → `CalendarRescheduleProposed` (no mail). Surface/sent catalog ledgers for ambient Emit.
  - **S11** `VoiceNoteTasksCalendar{Module,Tests}.cs` — mock STT `MockVoiceTranscriber`: `VoiceNoteReceived` → `VoiceTranscriptReady`; `VoiceActionExtractor` → 2× `TaskCreated` (Tag=voice, reuses S36 shape/TaskStore) + `CalendarBlockProposed`. Types: `VoiceNoteReceived`/`VoiceTranscriptReady`/`CalendarBlockProposed` (no collision with meeting transcript vocabulary).
  - **S21** `MeetingTranscriptActionFanout{Module,Tests}.cs` — `MeetingTranscriptReady` → `ActionItemCreated`×3 (tasks/email/crm lanes); three distinct handlers hear every item with **identical Source+Sequence**; each emits lane-filtered `ActionLaneAcknowledged`.
  - **S32** `MeetingNotesTasksSlack{Module,Tests}.cs` — `MeetingNotesReady` → `NotesTaskCreated`×2 + `SlackPostRequested` → `MockSlackPoster` → `SlackMessagePosted`; Cause chain session→extractor→poster. Prefixed `NotesTaskCreated` (not S36 `TaskCreated`).
- **Core change:** none (composition + journals only). Ambient Emit requires catalog listeners (S02 trap).
- **Gates (quoted):**
  - four classes → **5 passed / 0 failed** (1.924s)
  - `dotnet test DigitalBrain.slnx -c Release` → **54 passed / 0 failed** (Core.Tests 53 + Mocks.Tests 1)
- **TRACE:** 08, 11, 21, 32 → GREEN

## Scenarios S06 / S10 / S12 / S14 / S15 / S16 / S17 / S18 / S19 / S20 — journal-proof acceptance
- **Status:** GREEN
- **Files (src/DigitalBrain.Core.Tests/Scenarios/):**
  - **S06** `RichChatImageSalesChart{Module,Tests}.cs` — multi-ask vision→SF stage stats → `ChartSpec` (reuse S38) + `FunnelTableProduced` + `RichChatAssistantSaid`; shell ledger catalog sinks
  - **S10** `LiveDashboardStreamSubscription{Module,Tests}.cs` — subscription snapshot then ambient `OpportunityClosedWon`/`InvoicePaid`/`PurchaseOrderEmailDetected` revise KPI tiles + chart points; UI edge hears
  - **S12** `McpToolsIdeFederation{Module,Tests}.cs` — `McpToolInvoked` → Ask `ActiveNeurons` → `McpToolCompleted`; mutating path deferred `McpApprovalRequired`/`McpUserApproved`
  - **S14** `ComplianceLegalHold{Module,Tests}.cs` — `LegalHoldPlaced` → `DestructiveActionBlocked`; Contoso inbound still `RetentionExtended`; lift allows delete
  - **S15** `TravelBookingMultiApproval{Module,Tests}.cs` — offers+policy → selection → approval gate → hold → book → calendar; no hold/book before manager approve
  - **S16** `InvoiceOcrAccountingPayment{Module,Tests}.cs` — dual gates bill then pay; `PaymentExecuted` uses approved amount not spoofed pay amount
  - **S17** `TeamStandupSynthesis{Module,Tests}.cs` — TState join of four section legs → `StandupBriefBuilt`; incomplete waits
  - **S18** `OpportunityCloseGmailSequence{Module,Tests}.cs` — ClosedWon fan-out sequence + completed; open-runner cancel on stage revert; dup ClosedWon no-op
  - **S19** `ShellWidgetBehaviorLiveAuthor{Module,Tests}.cs` — install→activate+bind; calendar title pattern → `WidgetPropsPatched` → `WidgetRendered`
  - **S20** `WebResearchBriefCitations{Module,Tests}.cs` — multi `MockWebSearch` asks; claims cite only journaled URLs; `UnsupportedClaimDropped` for invented URL
- **Core change:** none (composition + journals only). Ambient Emit needs catalog listeners (S02 trap). Unique type names where colliding with prior scenarios.
- **Collateral (gate hygiene, concurrent WIP):** fixed locus assertions in S24/S26 hot-reload (declared fan-out is same-Name); analyzer/timeout hygiene in CryptoWalletTax, OwnerIsolation, ImplicitStreamWake, Replay, Whiteboard, NightlyBatch (deactivate+advance like S46).
- **Gates (quoted):**
  - priority ten classes → **14 passed / 0 failed**
  - `dotnet test DigitalBrain.slnx -c Release` → **83 passed / 0 failed** (Core.Tests 82 + Mocks.Tests 1)
- **TRACE:** 06, 10, 12, 14, 15, 16, 17, 18, 19, 20 → GREEN

## Scenarios S22–S28 / S33–S34 / S39–S41 / S44–S45 / S49 — journal-proof acceptance
- **Status:** GREEN
- **Files (src/DigitalBrain.Core.Tests/Scenarios/):**
  - **S22** CryptoWalletTaxJournal{Module,Tests}.cs — OnChainTransferObserved → HistoricalPrice ask → TaxLotOpened; outbound FIFO TaxLotDisposed
  - **S23** CustomerChurnAlertCascade{Module,Tests}.cs — ambient ticket+usage score threshold → ChurnCaseOpened + SavePlayProposed; no double-open
  - **S24** BehaviorHotReloadInflightAsks{Module,Tests}.cs — Stage-1 honest **Connect rewiring (not ALC)**; rev1 open AccountLookup drains after supersede; new mail → rev2
  - **S25** OwnerIsolationSharedSilo{Module,Tests}.cs — Stage-1 **context Names (not OwnerId in NeuronId)**; A mail never in B; JournalSlice B-only
  - **S26** BehaviorHotReloadLive{Module,Tests}.cs — Stage-1 honest **Connect rewiring (not ALC)** under live traffic; v1 freeze after v2 wire
  - **S27** MultiOwnerIsolation{Module,Tests}.cs — Stage-1 ada/beau context Names; RunwayAnswer + mail never cross
  - **S28** ImplicitStreamWake{Module,Tests}.cs — Stage-1 **no Core streams**: ExternalStreamTick ingress adapter journals first → SlackReactionAdded wakes GratitudeNotes
  - **S33** WhiteboardPhotoTasks{Module,Tests}.cs — ImageAttached → OCR ask → TasksProposed → Confirm → TaskCreated×3
  - **S34** ReplayLastTuesdayJournal{Module,Tests}.cs — JournalRangeQuery slice equals Brain.ReadAsync journal structure; timeline cites only those titles
  - **S39** NightlyBatchGmailCalendarCrm{Module,Tests}.cs — Schedule NightlyReconcileDue → 3 section asks join → NightlyMorningPackReady
  - **S40** VoiceTranscriptCrmEmail{Module,Tests}.cs — CallEnded → Transcript → Summarized + ContactResolved → CrmNoteLogged + FollowUpEmailDrafted
  - **S41** OauthRefreshMidWorkflow{Module,Tests}.cs — Drive auth fail → AuthorizationRequired pause; grant completes pending upload; ExpenseFiled; Gmail once
  - **S44** RollingModuleGrainVersion{Module,Tests}.cs — Stage-1 honest **ModuleVersionChanged on same grain (not Orleans interface swap)**; HandlerVersion 1→2
  - **S45** StatelessWorkerEmbeddings{Module,Tests}.cs — Stage-1 honest **pure service in turn (not Orleans worker)**; EmbeddingBatchDone×N → NotesIndexReady
  - **S49** MarketplaceInstallHandlers{Module,Tests}.cs — Stage-1 honest **second Compose module type = N+1** EmailReceived listeners after BehaviorActivated
- **Core change:** none (composition + journals only).
- **Gates (quoted):**
  - dotnet build DigitalBrain.slnx -c Release → 0 errors
  - dotnet test DigitalBrain.slnx -c Release → **83 passed / 0 failed** (Core.Tests 82 + Mocks.Tests 1)
- **TRACE:** 22, 23, 24, 25, 26, 27, 28, 33, 34, 39, 40, 41, 44, 45, 49 → GREEN

## Milestone — 50/50 scenarios + physics suite (meaningful only)
- Gate: `dotnet test DigitalBrain.slnx -c Release` → **83 passed / 0 failed**
- TRACE: **50 GREEN / 0 RED**
- Quality bar enforced: every scenario DisplayName states the law; Stage-1 honesty in hot-reload/multi-owner/streams/ALC/marketplace
- Mocks: DigitalBrain.Mocks (X, Gmail, Salesforce, WebSearch, Crypto, …) + scenario-local neurons
