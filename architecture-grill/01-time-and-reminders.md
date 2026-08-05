# 01 — Timers, reminders, Schedule: who owns them?

**Status:** RATIFIED  
**Date:** 2026-04-05  
**Inputs:** CORE-DESIGN Time section; `Neuron.Schedule.cs` / `OutboxWakeup.cs` / `NeuronConcurrency.cs`;
v1 `CountdownNeuron` (`IRemindable`); FLOWS.md flow 7; scenarios 31, 35, 39, 46; user correction
*"timers and reminders might actually come from modules; core might not expose those timers and
reminders."*

---

## 0 · What is actually being argued

Three different things share English words. Collapse them and every option looks wrong.

| Layer | What it is | Who may own it |
|---|---|---|
| **Orleans timers / reminders** | Grain timer callbacks; `IRemindable.ReceiveReminder` | Core only, never module-visible |
| **Deferred self-delivery** | “Later, deliver fact *F* into *this* neuron as an ordinary turn” | Contested — this grill |
| **Product time** | Countdown UI, cron, local TZ, snooze, “remind me in 30 days” UX | Modules only |

v1 mixed all three: `CountdownNeuron` implements `IRemindable`, registers reminders by generation/
revision, and `ReceiveReminder` stages + `SendAsync` **outside** the Deliver pipeline. That is the
trap named in CORE-DESIGN and sealed by `NeuronConcurrency` (“reminders are Core wakeup machinery;
schedule facts instead”).

The user correction is **not** “let modules call `RegisterOrUpdateReminder`.” It is: **do not put
product timers/reminders in Core vocabulary; do not surface Orleans reminder concepts to modules.**
That is compatible with Core owning deferred self-delivery under a different name.

---

## 1 · Options

### A — Core exposes Schedule verbs + “reminders”

- Public: `Schedule(fact, period)` / `Unschedule<TFact>()` verbs; facts `Schedule`, `Unschedule`,
  `ScheduleFailed`.
- Internal: grain timers + companion `OutboxWakeup` (`IRemindable`) for idle backstop.
- Modules: no `IRemindable`, no `RegisterGrainTimer`, no reminder table API.
- **This is what v2 code and CORE-DESIGN already implement.**

### B — Core-internal only for outbox

- Core arms timers/reminders **only** to drain the journal-as-outbox and ask pins.
- No schedule table. No `Schedule` verb. No product path to “wake me later with a fact.”
- Modules that need delay must stay activated, poll externally, or invent illegal second wires.

### C — Time module owns all scheduling via synapses

- Product Time module is the sole scheduler: countdown, pulse, cron, 30-day remind.
- Other modules speak Time vocabulary (`StartCountdown`, `ArmPulse`, …); never Core Schedule.
- Core still must either (C1) grant Time neurons `IRemindable`/timers, or (C2) hide a Core
  schedule primitive that only Time may use, or (C3) keep a non-neuron special grain.

### D — Hybrid (physics vs product)

- **Core:** outbox wakeup + deferred self-delivery (`Schedule` table/verbs/facts/`ScheduleFailed`).
- **Modules:** product time (countdown, cron, TZ, snooze, NL “remind me”) built **on** Core
  Schedule and ordinary synapses — never on Orleans timers/reminders.
- **Forbidden:** module timers, module `IRemindable`, module-visible reminder grains, any emission
  path that is not a committed turn.

---

## 2 · Grill — scenarios

Scenarios force different layers. An option that wins one and dies on another is dead.

### 2.1 Crypto sample windows (scenario 31)

Need: continuous or periodic recheck (trailing stop, post-signal window), serialized turns so two
ticks cannot double-sell, delayed retry after `DeliveryFailed` / broker flake.

| Option | Verdict |
|---|---|
| **A** | RiskPolicy / MarketPulse `Schedule(Recheck, 5min)` on self. Tick = ordinary turn. Failures → `ScheduleFailed` or heal listener. **Works.** |
| **B** | No legal deferred recheck once the grain deactivates between ticks. Streams can push price, but “re-eval in 5 min after social signal with no ticks” dies. **Fails idle recheck.** |
| **C** | MarketPulse Asks/Emits to Time; Time later Sends `Recheck` back. Extra hop + correlation + Time becomes SPOF for every desk. **Works only if Time has durable wake (C1/C2).** |
| **D** | Same as A for the recheck; Time module unused unless product wants countdown chrome. **Works.** |

**Strongest attack on A/D:** high-frequency price should be a stream/ingress fact, not Core Schedule
at 100ms. **Defense:** Schedule is for *cadence and delayed intent*, not market data firehose.
Streams journal into neurons; Schedule is not the bus. **Hold.**

### 2.2 Nightly batch at 02:00 local (scenario 39)

Need: calendar-aware due, DST, owner offline, idempotent day key, optional second fire at 07:00 push.

| Option | Verdict |
|---|---|
| **A alone** | Fixed `Period` is **not cron**. `Schedule(NightlyDue, 24h)` drifts across DST and does not mean “02:00 local.” **A without a module is insufficient for product nightly.** |
| **B** | No wake after idle night. **Fatal.** |
| **C** | Time module owns cron/TZ; emits `NightlyReconcileDue`. **Correct product home** — but still needs durable wake under Time. |
| **D** | NightlyScheduler neuron: compute next UTC due, `Schedule(NightlyReconcileDue, delayToNext)` one-shot style (handler reschedules next night). Cron math in module; physics in Core. **Works.** |

**Attack:** Core should grow cron expressions. **Defense:** cron/TZ/calendar are unbounded product
surface; Core schedule stays “due + period + fact.” Prefer delete Core cron. **Fold toward D.**

### 2.3 30-day dormant wake (scenario 46)

Need: grain deactivated for a month; absolute UTC due; fire once; rich card; snooze = new schedule.

| Option | Verdict |
|---|---|
| **A** | Schedule table + reminder backstop while `HasSchedules`; tick delivers `ContractReviewDue` as self-turn. One-shot = handler `Unschedule`. **Works.** Companion wakeup already keys by neuron id. |
| **B** | Empty outbox + no schedule ⇒ disarm forever. 30-day due **never fires**. **Fatal.** |
| **C** | Time/Countdown neuron holds the reminder (v1 shape). Target receives `CountdownElapsed` / directed due fact. **Works product-wise** if Time may use durable wake; multiplies grains per reminder. |
| **D** | Either self-Schedule on ContractReview, or Time countdown that ends by emitting/sending a fact the target handles — both sit on Core deferred delivery. **Works.** |

**Attack on A:** Orleans reminder minimum resolution / long due-times. **Defense:** due is in the
durable `ScheduleEntry.NextDue`; reminder cadence is a *backstop poll*, not the due clock (v1
countdown already re-armed when `observedAt < DueAt`). Same pattern. **Hold.**

### 2.4 Schedule failed → self-heal (scenario 35 + ScheduleFailed)

Need: loud terminal failure; listener re-arms or escalates; no silent infinite retry.

| Option | Verdict |
|---|---|
| **A/D** | After N consecutive tick failures Core journals `ScheduleFailed` and removes the entry. `Hear(ScheduleFailed f) => Schedule(...)` is one line. **Works by design.** |
| **B** | No schedule failures to heal. DeliveryFailed heal still exists for outbox; delayed “retry Slack later” **has no primitive**. **Partial fatal.** |
| **C** | Time must invent its own failure facts; every consumer learns Time’s dialect. Heal becomes module-coupled. **Worse unless Time is mandatory OS.** |

**Attack:** `ScheduleFailed` is Core vocabulary bloat. **Defense:** physics #4 (never silent loss)
against timer-swallowing forces *some* journaled terminal. Outcome facts are listenable; that is the
self-heal product. Deleting `ScheduleFailed` recreates silent death. **Hold.**

### 2.5 Behavior wants “every 5 minutes”

Need: ingestion pollers (XAccount), Pulse, health probes.

| Option | Verdict |
|---|---|
| **A/D** | `Hear(Watch) => Schedule(new PollX(), 5min)`. Tick runs pipeline; Emit results. **Canonical.** |
| **B** | Illegal or always-on activation. **Fatal for idle pollers.** |
| **C** | Every poller depends on Time/Pulse as a service neuron. Possible, but Core still implements wake *somewhere*; you only moved the call site and added fan-in load on Time. **No deletion of complexity — relocation.** |

**Attack on A:** FLOWS.md still says Pulse uses a “private grain timer.” **Defense:** that sentence
is stale; CORE-DESIGN already amended flow 7 — timer is Core Schedule, Tick is a fact. Fix the
prose, don’t reintroduce private timers. **Hold.**

---

## 3 · Attack / defense per option

### A — Core Schedule + “reminders”

**Strongest attack:** Abstractions claim is “only Synapse, INeuron, NeuronId, SynapseMetadata.”
`Schedule`/`Unschedule`/`ScheduleFailed` expand Core public synapses; schedule table expands the
durable schema; “reminders” in docs sound like product features.

**Defense:** Core synapses are already a closed append-only set for *physics* (Connect, DeliveryFailed,
AskExpired, …). Deferred self-delivery is physics: without it, turn-only emission cannot produce
future work after deactivation. The word “reminder” must not appear on the module API — only
Schedule.

**Residual weakness:** period-only API under-serves one-shot and cron *product* shapes (not a reason
to delete Schedule; reason to keep product Time as a module).

### B — Outbox-only Core

**Strongest attack:** Prefer delete. Schedule table, ScheduleFailed, SyncScheduleTimers, remote
Schedule interception — all gone. Core shrinks.

**Defense fails on 46 and 39 and idle 31 recheck.** Outbox wakeup answers “I said something that has
not landed,” not “I intend to think again in 30 days with an empty outbox.” Conflating them is a
category error. **B is deleted.**

### C — Time module owns all scheduling

**Strongest attack (user-shaped):** Product timers and reminders *are* modules; Core should not
expose them. v1 already shipped Time as a module. Aligns with “prefer delete” from Core.

**Counter-attack:** C does not delete wakeup physics — it **hides** them. Three bad subcases:

1. **C1 — Time neurons are `IRemindable`:** reopens turn-pipeline bypass, interleaving, and the
   exact v1 recovery maze (generation/revision reminder names, arm-before-save, retire races).
   `NeuronConcurrency` forbids this for good reason.
2. **C2 — Core schedule is private, only Time may call it:** a second, privileged module API.
   Every poller becomes a Time client or Time becomes a mandatory kernel dependency. Not thinner —
   more ceremony.
3. **C3 — Non-neuron scheduler grains:** second durability system beside journals; facts are no
   longer the only bus for “why did this fire.”

**Defense of C for product only:** countdown UX, cron, snooze cards **should** be a Time module.
That is not ownership of *all* deferred delivery.

**C-as-sole-scheduler is deleted.** C-as-product-façade survives inside D.

### D — Hybrid

**Strongest attack:** “Hybrid” is where designs go to avoid deciding; two mechanisms forever.

**Defense:** there is **one** mechanism for deferred delivery (Core schedule table → timer tick →
ordinary turn). Outbox wakeup is a **different job** (unsettled said entries / ask pins), sharing
only the companion reminder grain and arm-before-commit discipline. Product Time is **not** a second
wakeup path; it is handlers that call `Schedule` / emit Time facts. One physics, layered product.

**Residual risk:** authors schedule 100ms busy-loops via Schedule. Mitigate with catalog/docs and
maybe a floor later — not by deleting Schedule. High-rate ingress stays streams → journal.

---

## 4 · Decision — what Core implements / modules may use / forbidden

### Core implements (internal or closed physics)

| Piece | Role | Module-visible? |
|---|---|---|
| Journal-as-outbox drain timer | Fast in-activation retry | No |
| `OutboxWakeup` grain + `IRemindable` | Idle backstop while unsettled outbox, ask pins, **or schedules** | No |
| Durable `schedule` table (`ScheduleEntry`) | NextDue, period, body blob, failures, Cause | No (schema is Core) |
| Timer re-arm from table at activate/commit | In-activation ticks | No |
| Tick → `DeliverToSelf` ordinary turn | Every emission inside a turn | No |
| `ScheduleFailed` after consecutive failures | Terminal, journaled | Yes as listenable fact |
| Arm-before-commit / disarm when empty | Survive deactivation | No |
| Keyed `TimeProvider` (`NeuronTime`) | Test clock ≠ Orleans runtime clock | Injection only |

### Core exposes to modules (the only “do something later” surface)

See §5. Verbs + three facts. **Not** Orleans timers, **not** “reminder” types, **not** cron.

### Modules may

- Call `Schedule` / `Unschedule` inside a turn on **self**.
- Emit/receive remote `Schedule` / `Unschedule` (same table; reserved kinds Core-intercepted).
- Implement `INeuron<ScheduleFailed>` and re-arm or escalate.
- Ship a **Time module** for countdown, cron, TZ, snooze, NL reminders — implemented with Core
  Schedule + ordinary synapses (not `IRemindable`).
- Drive high-frequency signals via ingress → journaled facts / streams; use Schedule for cadence
  and delayed intent only.
- Model one-shot as: schedule once, `Unschedule` (or finish state) on first successful Hear; model
  cron as: module computes next UTC due, schedules delay, reschedules in handler.

### Forbidden

| Ban | Why |
|---|---|
| `IRemindable` on any `Neuron` | Second wire; bypasses Deliver; breaks serialization contract |
| Module `RegisterGrainTimer` / `RegisterTimer` / `RegisterOrUpdateReminder` | Interleaves; unenlisted side effects; timer-swallowing vs physics #4 |
| Module-visible wakeup grains or reminder tables | Second bus; forges wake authority |
| Emitting from timer callbacks without going through Core tick→turn | Breaks “every emission inside a turn” |
| Treating outbox wakeup as product scheduler | Wrong job; disarms when backlog empty |
| Core cron / TZ / countdown product types | Unbounded product surface; belongs in Time module |
| Silent infinite reschedule after tick failure | Physics #4; must hit `ScheduleFailed` |

---

## 5 · Exact API surface modules see for “do something later”

### 5.1 In-turn verbs (`Neuron`)

```csharp
// Arm or replace the sole schedule entry for fact.GetType()'s kind on this neuron.
// Requires this kind declares INeuron<TFact> for the scheduled fact type (else throw in-turn → retract).
// period > 0; first due = commit_time + period; after each successful tick, next = now + period.
protected void Schedule(Synapse fact, TimeSpan period);

// Remove the schedule entry for TFact's kind. Unknown/unscheduled → no-op at commit of a said Unschedule.
protected void Unschedule<TFact>() where TFact : Synapse;
```

No `ScheduleAt`, no cron string, no reminder name, no `IGrainTimer` handle. Deliberate.

### 5.2 Public Core facts (`DigitalBrain` / Abstractions)

```csharp
// RESERVED kinds — module INeuron<Schedule|Unschedule> fails boot; Core intercepts on the target.
public sealed record Schedule(Synapse Fact, TimeSpan Period) : Synapse;
public sealed record Unschedule(string Fact) : Synapse;  // Fact = fact kind string

// Ordinary listenable outcome — modules may Hear and re-arm or escalate.
public sealed record ScheduleFailed(string Fact, string Reason, int ConsecutiveFailures) : Synapse;
```

Remote `Schedule`/`Unschedule` mutate the **same** table as the verbs (one mechanism). Refusal
(unknown kind, target does not listen, non-positive period) → journaled `ScheduleFailed` toward
requester where applicable; no silent accept.

### 5.3 What modules do **not** see

- `IOutboxWakeup`, `ArmAsync` / `DisarmAsync`
- `RegisterGrainTimer`, `IRemindable`, reminder names, `TickStatus`
- `ScheduleEntry`, `DeliveryPolicy.WakeupCadence`, `ScheduleFailureLimit` (constants are Core-internal;
  failure count appears only on the `ScheduleFailed` fact)
- Any “Reminder” type in Abstractions

### 5.4 Idioms (not new API)

```csharp
// Periodic poller
public void Hear(WatchAccount fact) => Schedule(new PollX(), TimeSpan.FromMinutes(1));
public async Task HandleAsync(PollX fact, CancellationToken ct) { /* poll; Emit */ }

// One-shot 30-day
public void Hear(ReminderRequested fact)
    => Schedule(new ContractReviewDue(fact.ContractId, fact.ContextRef), TimeSpan.FromDays(30));
public void Hear(ContractReviewDue fact)
{
    Unschedule<ContractReviewDue>();
    Emit(new UiSurface(/* card */));
}

// Self-heal after terminal schedule death
public void Hear(ScheduleFailed f)
{
    if (f.Fact == nameof(PollX) && f.ConsecutiveFailures >= 5)
        Emit(new ManualInterventionRequired(f.Reason));
    else
        Schedule(new PollX(), Backoff(f.ConsecutiveFailures));
}

// Cron-like (module math; Core remains period/delay)
public void Hear(NightlyReconcileDue fact)
{
    // ... batch work ...
    Schedule(new NightlyReconcileDue(nextDayKey), DelayUntilNextLocal(new TimeOnly(2, 0)));
}
```

### 5.5 Product Time module (optional, not Core)

Builds on §5.1–5.2, does not replace them:

- Countdown lifecycle, generation/revision, destination cards (v1 product value).
- Cron calendars, owner TZ, snooze buttons → new `Schedule` with new delay.
- NL “remind me” → facts into a neuron that self-Schedules (or receives remote `Schedule`).

If Time is absent, pollers and self-Schedule still work. Time is not a mandatory chokepoint.

### 5.6 Non-goals (delete if proposed)

- `protected void Delay(TimeSpan, Action)` — action is not a fact; not journalable.
- Per-entry named schedules beyond fact kind key — kind key is the name; one entry per kind.
- Module-settable failure limit / wakeup cadence — Core constants.
- Same-turn “ride” of a timer fire into another neuron’s handler — delivery is post-commit drain /
  self-turn only.

---

## 6 · Mapping to existing code (no surprise rewrites)

| Artifact | Keep / change |
|---|---|
| `Neuron.Schedule.cs`, schedule table, `ScheduleFailed` | **Keep** — this is the ratified physics |
| `OutboxWakeup` | **Keep** — outbox + ask pins + schedules backstop; never public |
| `NeuronConcurrency` ban on `IRemindable` | **Keep** — absolute |
| Core facts `Schedule`/`Unschedule`/`ScheduleFailed` | **Keep** |
| FLOWS.md flow 7 “private grain timer” | **Amend prose** — Core Schedule, Tick as fact |
| v1 `CountdownNeuron` : `IRemindable` | **Do not port** — reimplement on Core Schedule if product Time returns |
| Cron / TZ / countdown UX in Core | **Do not add** |

---

## 7 · Why not pure A naming?

Option A in the brief says “Schedule verbs+**reminders**.” Ratified surface is **Schedule verbs and
facts only**. Reminders exist solely as Core-internal Orleans machinery on `OutboxWakeup` (and
implicitly as the idle backstop while schedules exist). Modules never schedule a “reminder”; they
schedule a **fact**. That is the user correction, honored without deleting deferred delivery.

Why not pure C? Because “Time owns scheduling” either reintroduces illegal neuron reminders or
smuggles Core Schedule under a privileged module — complexity moved, not deleted, and every five-
minute poller pays a hop.

Why not B? Because empty-outbox dormant intent is real (46, 39, idle 31) and outbox wakeup
correctly forgets when settled.

---

## RATIFIED DECISION

```
DECISION: D (hybrid) with A-shaped deferred-delivery physics and C-shaped product time.

CORE IMPLEMENTS
  - Outbox/ask drain timer + OutboxWakeup (IRemindable companion grain) — internal only
  - Durable per-neuron schedule table; grain timers re-armed from table
  - Tick = ordinary self-sourced turn (Cause = schedule journal position)
  - Terminal ScheduleFailed after consecutive tick failures; then unschedule
  - Arm wakeup while outbox unsettled OR ask pins OR schedules; disarm when all clear

CORE EXPOSES TO MODULES (only "later" API)
  - protected Schedule(Synapse fact, TimeSpan period)
  - protected Unschedule<TFact>()
  - facts: Schedule, Unschedule (reserved), ScheduleFailed (listenable)
  - no reminder/timer types, no cron, no countdown product API

MODULES MAY
  - Self-schedule / unschedule; Hear ScheduleFailed; build Time product on top
  - Use streams/ingress for high-frequency signals

FORBIDDEN
  - IRemindable / RegisterGrainTimer / RegisterOrUpdateReminder on neurons
  - Module-visible wakeup/reminder control planes
  - Outbox wakeup used as product scheduler
  - Silent infinite tick retry
  - Core ownership of cron/TZ/countdown UX

DELETED OPTIONS
  - B (outbox-only) — fatal for dormant intent
  - C as sole scheduler — either illegal reminders or fake deletion
  - A as "expose reminders" — wrong name; reminders stay Core-private

ONE-LINE RULE
  Modules schedule facts; Core owns waking the neuron into a turn; Orleans timers/reminders
  are never a module API; product "reminders" are a Time module, not Core.
```
