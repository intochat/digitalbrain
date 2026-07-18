# Persona orb Rive asset

Drop a `persona_orb.riv` file in this directory to upgrade the persona orb from
the procedural CustomPaint fallback (`lib/persona/persona_widget.dart`) to a
real Rive animation. The CustomPaint renderer keeps working if the file is
absent, empty, or fails to load — the demo ships either way.

## State machine contract

The Flutter client expects the `.riv` file to expose the following surface:

- **Artboard**: default (any name, 1:1 aspect ratio recommended)
- **State machine**: default (any name, picked automatically by `RiveWidgetController`)
- **Inputs** (optional, additive — future phases will wire these):
  - `mood` — Number, 0..1, mapped from `PersonaEmotion` via
    `_moodFor()` in `lib/persona/rive_persona_orb.dart` once the fine-grained
    input wiring lands (sleeping=0.0, idle=0.2, thinking=0.6, acting=0.8,
    celebrating=1.0).
  - `energy` — Number, 0..1, from `PersonaStateModel.energy`.
  - `pulse` — Number, 0..1, momentary spike on synapse signal.
  - `trigger_searching_flights` — Trigger, fired when the active neuron is
    `travel:flight-search`.
  - `trigger_searching_hotels`, `trigger_searching_places`,
    `trigger_composing_itinerary`, `trigger_thinking`, `trigger_idle` — same
    pattern, one trigger per `InoActivityMap` label.

## Load fallback

If the file is missing, invalid, or the state machine contract isn't met,
the widget catches the failure and renders the CustomPaint orb instead. See
`_PersonaOrb` in `lib/persona/persona_widget.dart`.
