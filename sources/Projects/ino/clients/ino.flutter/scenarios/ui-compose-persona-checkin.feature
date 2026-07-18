Feature: UIComposer — persona daily check-in (slice U.4)
  Inverts the trip-planning scenario: the user opens the day asking how
  they're doing, and the composer emits an introspective composition
  centred on the persona orb itself. PersonaInline is wired to the user's
  live BLoC mood + energy; Hero summarises the day; three Tiles enumerate
  recent wins; Badge tracks the streak.

  Same skeleton-then-data pattern α as ui-compose-tokyo-trip-plan, but the
  data lineage is "Recall lookup over today's events" rather than "Travel
  domain plan dispatch." The composer prompt the kernel sends to the LLM
  for this scenario includes the day's journal entries (timeline neuron
  dump) so the Tile lines are grounded in real activity.

  Source of truth shared with:
    1. clients/ino.flutter/lib/screens/rfw_v3_demo/_demo_script.dart
    2. src/Ino.Kernel.Tests/Features/UIComposer.feature (slice U.4)

  Tagged for breadth search so cortex's intent classifier can suggest this
  composition path when the user opens the chat with a check-in style
  prompt instead of a task.

  Tweenable numerics opt in via paired sibling fields
  `<name>AnimDurMs` + `<name>AnimCurve` (schema v1; see
  ui-compose-tokyo-trip-plan.feature for the full contract). Bare value
  writes snap.

  @neuron:ui.compose
  @intent:persona.checkin
  @stream-pattern:skeleton-then-data
  @widget-set:hero,tile,badge,persona-inline,spacer
  @anim-schema:v1
  @scenario-id:persona-checkin
  Scenario: Daily persona check-in composition
    Given the user is on the v3 chat surface
    And the kernel ino-design palette exposes Hero, Tile, Badge, PersonaInline, Spacer artboards
    And the recall neuron has 3 entries from today: PR closed, dinner cooked, 6km walk

    When the user says "how am I today?"
    Then the composer emits a skeleton frame within 250ms with rfwtxt:
      """
      import ino.rive;
      import core.widgets;
      widget root = Column(children: [
        PersonaInline(domain: "kernel", mood: data.persona.mood, energy: data.persona.energy, energyAnimDurMs: data.persona.energyAnim.durMs, energyAnimCurve: data.persona.energyAnim.curve),
        Hero(domain: "kernel", title: data.hero.title, subtitle: data.hero.subtitle, mood: data.hero.mood),
        Spacer(domain: "kernel", height: 16, motif: data.spacer.motif),
        Tile(domain: "kernel", kind: data.tiles.0.kind, line1: data.tiles.0.line1, line3: data.tiles.0.line3),
        Tile(domain: "kernel", kind: data.tiles.1.kind, line1: data.tiles.1.line1, line3: data.tiles.1.line3),
        Tile(domain: "kernel", kind: data.tiles.2.kind, line1: data.tiles.2.line1, line3: data.tiles.2.line3),
        Badge(domain: "kernel", label: data.badge.label, value0to1: data.badge.value0to1, value0to1AnimDurMs: data.badge.value0to1Anim.durMs, value0to1AnimCurve: data.badge.value0to1Anim.curve),
      ]);
      """
    And the seed DynamicContent payload is:
      """
      {
        "persona": {"mood": "centered", "energy": 0.5},
        "hero":    {"title": "Today's pulse", "subtitle": "", "mood": "centered"},
        "spacer":  {"motif": "wave"},
        "tiles":   [
          {"kind": "task", "line1": "", "line3": ""},
          {"kind": "task", "line1": "", "line3": ""},
          {"kind": "task", "line1": "", "line3": ""}
        ],
        "badge":   {"label": "Streak", "value0to1": 0}
      }
      """

    When 220ms elapse
    Then a delta frame updates "hero.subtitle" to "You shipped 3 things and slept 7h"

    When 420ms elapse
    Then a delta frame updates "persona.energyAnim" to {"durMs": 400, "curve": "easeOut"}
    And a delta frame updates "persona.energy" to 0.78

    When 700ms elapse
    Then a delta frame replaces "tiles.0" with:
      """
      {"kind": "task", "line1": "Closed PR #142", "line3": "2h"}
      """

    When 900ms elapse
    Then a delta frame replaces "tiles.1" with:
      """
      {"kind": "task", "line1": "Cooked dinner", "line3": "35m"}
      """

    When 1100ms elapse
    Then a delta frame replaces "tiles.2" with:
      """
      {"kind": "task", "line1": "Walked 6km", "line3": "1h"}
      """

    When 1300ms elapse
    Then a delta frame updates "badge.value0to1Anim" to {"durMs": 500, "curve": "easeOutCubic"}
    And a delta frame updates "badge.value0to1" to 0.71

    When 1500ms elapse
    Then a delta frame fires PersonaInline.pulse trigger
    And the composer signals stream-complete

    Then the rendered widget tree includes exactly:
      | widget        | count |
      | PersonaInline | 1     |
      | Hero          | 1     |
      | Spacer        | 1     |
      | Tile          | 3     |
      | Badge         | 1     |
    And persona.energy ≥ 0.7 indicating the day is going well
