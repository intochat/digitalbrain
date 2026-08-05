# Scenario 19: Live-author a shell widget behavior

## User intent
Owner wants a home-screen widget "Countdown to board meeting" that listens for `CalendarEventCreated` matching a title pattern and shows remaining time, authored as a small C# behavior bound to a shell composition — edit, hot-activate, see it live.

## Trigger
Behavior Studio + Shell composition editor: bind behavior outputs to widget props; activate.

## Imagined modules
- Behaviors + BehaviorHost
- Shell (scenes, widgets, compositions)
- Calendar
- Time/Countdown
- Chat (optional status)
- Module catalog

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| behaviorstudio/owner | Author/compile |
| behaviorhost/worker | Run `BoardCountdownBinder` neuron |
| shell/primary | Widget host |
| calendar/owner | Event facts |
| countdown/board | Optional time module integration |
| behaviorcatalog/owner | Activation |

## Synapse choreography
1. Owner writes behavior: `INeuron<CalendarEventCreated>`, `INeuron<CountdownTicked>` → emits `WidgetPropsPatched` (widgetId, props).
2. `BehaviorCompileAsked` → `BehaviorCompileAnswered`; `BehaviorActivateAsked` → `BehaviorActivated`.
3. Shell composition **broadcasts** `WidgetBound` (widgetId, listensTo props patches from behavior kind).
4. Calendar event "Board meeting" created → **broadcast** `CalendarEventCreated`.
5. Behavior hears → **directs** `CountdownStartAsked` → countdown module → ticks **broadcast** `CountdownTicked`.
6. Behavior **broadcasts** `WidgetPropsPatched` (title, remaining, urgency).
7. Shell **broadcasts** `WidgetRendered` (session local projection); UI updates without chat.
8. Owner edits behavior logic (color thresholds), reactivates → `BehaviorSuperseded` old revision; in-flight countdown continues or rebinds per policy journaled.

## Orleans / Core surface exercised
Module catalog hot swap; grain versioning of behavior revisions; timers/reminders for countdown; DurableGrain journals; pub-sub; placement; outbox; serialized behavior turns; UI-as-module (widget action is synapse).

## Rich experience
Home widget live clock; studio split view (code | preview); "inject test event" button; topology edge from calendar to widget.

## Failure / adversarial cases
- Two behaviors patch same widgetId: last writer wins or conflict `WidgetPropsConflicted` — must define; prefer explicit exclusive bind.
- Behavior throw on tick: journal `DeliveryFailed` on sender path; widget shows stale with error badge.
- Hot swap loses subscriptions: catalog must redeclare listeners atomically from activation fact.
- Widget action tap "snooze" → `WidgetActionActivated` directed to behavior; must not reenter illegally.

## Capability claim
UI widgets are synapse listeners fed by user-authored neurons — the shell programs against the same ABI as backend modules.
