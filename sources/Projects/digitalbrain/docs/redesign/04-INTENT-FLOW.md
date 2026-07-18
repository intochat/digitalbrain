# 04 — Intent → Neuron → Card → Panel

How a spoken/typed intent becomes a live widget, with three worked examples. Each
neuron is a **single `.ino` file** (V5-1) with an `rfw:` block composed from the
palette (Tier 2 — no rebuild). The `.ino` snippets below are illustrative shape,
not frozen syntax — align with `docs/v5plan/INO.md` when authoring.

## The pipeline (already mostly wired)

```
user intent ("set a clock")
   │  voice/text → Ino → Creator/dispatch
   ▼
neuron activates (ClockNeuron)
   │  emits RfwCardEnvelope{ library_name, root_widget, data_json }
   ▼
DigitalBrainGateway.WatchHomeFeed  (stream — wire this in slice 1)
   ▼
Flutter client: PanelManager.add(envelope)
   │  RfwRuntimeHost.render(library, root, data)
   ▼
draggable panel on the canvas
   │  user actions → RFW event → RemoteEventHandler → synapse back to kernel
   ▼
neuron reacts (e.g. ReminderNeuron fires at zero)
```

The only new client work is subscribing `WatchHomeFeed` and routing each envelope
through `PanelManager` (see `02-WINDOW-MANAGER.md`).

## Example A — "set a clock"

```ino
neuron ClockNeuron {
  on intent "set a clock" {
    emit card {
      surface: AnalogClock { tz: $user.timezone, showSeconds: true, face: "minimal" }
    }
  }
}
```
Result: an analog clock panel fades in. The tick is client-local (the
`AnalogClock` primitive self-drives) — no per-second gRPC traffic.

## Example B — "remind me in 10 minutes"

```ino
neuron ReminderNeuron {
  on intent "remind me in {minutes} minutes" {
    let started = now_utc()
    emit card {
      surface: CountdownClock {
        durationSeconds: $minutes * 60,
        startedAtUtc: $started,
        onZeroEvent: "reminderFired"
      }
    }
    after $minutes minutes {
      emit Reminder.Fired { text: "Time's up" }   // domain reaction
    }
  }
}
```
Result: a countdown panel, hands running **backward**. At zero the panel pulses
and plays a `LottiePlayer` celebration (UI reaction, fired by `onZeroEvent`),
*and independently* `Reminder.Fired` lands in the synapse trace (domain reaction).
UI-is-data keeps the two decoupled.

## Example C — "show flight BA286"

```ino
neuron FlightNeuron {
  on intent "show flight {code}" {
    let route = lookup_flight($code)            // origin/dest coords + label
    emit card {
      surface: EarthGlobe {
        autoRotate: true,
        points: [ { lat: $route.from.lat, lng: $route.from.lng, label: $route.from.iata },
                  { lat: $route.to.lat,   lng: $route.to.lng,   label: $route.to.iata } ],
        arcs:   [ { from: $route.from, to: $route.to, style: "dashed" } ]
      }
    }
  }
}
```
Result: a globe panel spins up and animates a dashed origin→destination arc.

## What makes this zero-rebuild

All three neurons only *compose* palette primitives that already exist after the
one Tier-1 rebuild (`03-WIDGET-PALETTE.md`). Adding a fourth, fifth, hundredth
widget-driven intent is more `.ino` data — never a new Flutter build. That is the
"users/AI create their own layouts" promise, made concrete.

## Event round-trip (interactivity)

A panel button/slider fires an RFW `event "name" { args }` →
`RemoteEventHandler` (existing `event_table.dart`) → a capability that sends a
synapse to the kernel. Example: a "snooze" button on the reminder panel fires
`event "snooze" { minutes: 5 }` → `Reminder.Snooze` synapse → neuron re-arms and
emits an updated card.
