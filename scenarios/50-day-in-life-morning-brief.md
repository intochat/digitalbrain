# Scenario 50: Full day-in-life: morning brief combining weather, calendar, email, portfolio, open tasks

## User intent
Each morning the owner opens the shell (or receives a push) and sees one coherent brief: weather, today’s calendar, urgent email, portfolio movers, open tasks, and any overnight automation outcomes—with actions to start the day (navigate, reply draft, snooze task). This is the flagship multi-module composition.

## Trigger
`MorningBriefDue` from schedule (e.g., 07:00) and/or shell open `SessionStarted` that Asks for latest brief.

## Imagined modules
- Weather
- Calendar
- Gmail
- Portfolio
- Tasks
- NightlyReconcile output (scenario 39)
- Assistant narrator
- Ui multi-pane home
- Notifier

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| MorningBrief / today | Fan-out orchestrator; join journal |
| Weather / local | Forecast answerer |
| Calendar / personal | Agenda answerer |
| GmailQuery / inbox | Urgent threads |
| Portfolio / default | Movers |
| TaskStore / personal | Open tasks |
| NightlyPack / store | Overnight reconciliation |
| Assistant / today | Optional narrative summary |
| UiHome / shell | Composed surfaces |
| Notifier / push | Wake |

## Synapse choreography
1. Schedule delivers `MorningBriefDue` to MorningBrief (wake if dormant).
2. Parallel Asks: `GetForecast`, `GetDayAgenda`, `FindUrgentEmail`, `GetPortfolioMovers`, `FindOpenTasks`, `GetNightlyPack`.
3. Progressive `UiSurface` sections fill as answers arrive (broadcast to home).
4. When join complete (or timeout with degraded sections): Emit `MorningBriefReady` + optional Ask Assistant `NarrateBrief(structured)`.
5. `AssistantResponded` short spoken/text narrative with citations to sections.
6. Buttons: `OpenMeeting`, `DraftReply(emailId)`, `SnoozeTask`, `DisarmStopLoss`—each a typed synapse into the right module.
7. Owner interactions continue as normal directed flows; brief correlation remains for “why is this here?” replay.

## Orleans / Core surface exercised
Reminders/timers; fan-out/fan-in join on DurableGrain journals; AskExpired partial completion; broadcast UiSurface; request context day key; outbox; placement; module catalog; serialized turns on MorningBrief; streams/SSE to shell; no reentrancy from button handlers into open brief turn (buttons are new facts).

## Rich experience
Home scene multi-pane: weather hero, calendar timeline, email list with VIP tags, portfolio sparkline + table, task checklist, overnight automation strip; voice read-aloud; pull-to-refresh re-runs brief with new correlation; dark morning theme.

## Failure / adversarial cases
- One provider down → section-level SourceDegraded, brief still useful.
- Double morning fire → day-key idempotent MorningBriefReady.
- Stale portfolio cached across owners → grain keys owner-scoped.
- Narrative invents a meeting not in agenda → citations mandatory; UI prefers structured agenda facts.
- Reentrancy deadlock if DraftReply is handled by a neuron that Emits back into MorningBrief awaiting itself → facts flow one way; replies are new turns.

## Capability claim
DigitalBrain can express a full morning nervous system—time, many data domains, progressive UI, and actions—as one journaled composition of modules, which no single-threaded chatbot session can own as durable daily infrastructure.
