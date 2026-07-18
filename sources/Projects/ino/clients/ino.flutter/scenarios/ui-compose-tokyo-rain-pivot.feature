Feature: UIComposer — Tokyo rainy-day pivot (slice U.4)
  Mid-trip adjustment: weather forecast updated, the user wants the
  itinerary reshuffled toward indoor activities. Demonstrates the
  composer's *replace-on-context-change* path — same skeleton, brand new
  delta sequence overwriting prior tiles, plus a persona mood shift to
  "thoughtful" so the inline orb reflects the recalibration.

  Wires recall (prior trip plan) → weather (climatology lookup) → places
  (indoor-rated filter) → ui.compose. Cortex routes this prompt through
  the travel.adjust-trip plan grain (Phase 4 Slice A pattern), which then
  fans out to the supporting tools and hands the consolidated payload to
  UIComposer.

  Source of truth shared with:
    1. clients/ino.flutter/lib/screens/rfw_v3_demo/_demo_script.dart
    2. src/Ino.Kernel.Tests/Features/UIComposer.feature (slice U.4)

  Tagged with @intent:travel.adjust-trip so cortex search ranks it ahead
  of plan-trip when an existing trip is already in working memory.

  Tweenable numerics opt in via paired sibling fields
  `<name>AnimDurMs` + `<name>AnimCurve` (schema v1; see
  ui-compose-tokyo-trip-plan.feature for the full contract). This scenario
  exercises Spacer.height tweening to introduce the rain motif region.

  @neuron:ui.compose
  @intent:travel.adjust-trip
  @stream-pattern:skeleton-then-data
  @widget-set:hero,tile,badge,persona-inline,spacer
  @anim-schema:v1
  @scenario-id:tokyo-rain-pivot
  Scenario: Rainy Tokyo pivot composition
    Given the user is on the v3 chat surface
    And the kernel ino-design palette exposes Hero, Tile, Badge, PersonaInline, Spacer artboards
    And the prior turn's working memory contains a Tokyo trip plan

    When the user says "it's raining — pivot to indoors"
    Then the composer emits a skeleton frame within 250ms with rfwtxt:
      """
      import ino.rive;
      import core.widgets;
      widget root = Column(children: [
        PersonaInline(domain: "kernel", mood: data.persona.mood, energy: data.persona.energy, energyAnimDurMs: data.persona.energyAnim.durMs, energyAnimCurve: data.persona.energyAnim.curve),
        Hero(domain: "kernel", title: data.hero.title, subtitle: data.hero.subtitle, mood: data.hero.mood),
        Spacer(domain: "kernel", height: data.spacer.height, motif: data.spacer.motif, heightAnimDurMs: data.spacer.heightAnim.durMs, heightAnimCurve: data.spacer.heightAnim.curve),
        Tile(domain: "kernel", kind: data.tiles.0.kind, line1: data.tiles.0.line1, line2: data.tiles.0.line2, line3: data.tiles.0.line3),
        Tile(domain: "kernel", kind: data.tiles.1.kind, line1: data.tiles.1.line1, line2: data.tiles.1.line2, line3: data.tiles.1.line3),
        Tile(domain: "kernel", kind: data.tiles.2.kind, line1: data.tiles.2.line1, line2: data.tiles.2.line2, line3: data.tiles.2.line3),
        Badge(domain: "kernel", label: data.badge.label, value0to1: data.badge.value0to1, value0to1AnimDurMs: data.badge.value0to1Anim.durMs, value0to1AnimCurve: data.badge.value0to1Anim.curve),
      ]);
      """
    And the seed DynamicContent payload is:
      """
      {
        "persona": {"mood": "discovering", "energy": 0.6},
        "hero":    {"title": "Reshuffling…", "subtitle": "", "mood": "rethinking"},
        "spacer":  {"motif": "rain", "height": 0},
        "tiles":   [
          {"kind": "place", "line1": "", "line2": "", "line3": ""},
          {"kind": "place", "line1": "", "line2": "", "line3": ""},
          {"kind": "place", "line1": "", "line2": "", "line3": ""}
        ],
        "badge":   {"label": "Indoor coverage", "value0to1": 0}
      }
      """

    When 120ms elapse
    Then a delta frame updates "spacer.heightAnim" to {"durMs": 600, "curve": "easeOutCubic"}
    And a delta frame updates "spacer.height" to 48

    When 250ms elapse
    Then a delta frame updates "hero.subtitle" to "Rain through Wednesday — switching to indoor pivots"

    When 450ms elapse
    Then a delta frame updates "persona.mood" to "thoughtful"
    And a delta frame updates "persona.energyAnim" to {"durMs": 200, "curve": "easeOut"}
    And a delta frame updates "persona.energy" to 0.62

    When 700ms elapse
    Then a delta frame replaces "tiles.0" with:
      """
      {"kind": "place", "line1": "teamLab Borderless", "line2": "indoor • ★4.9", "line3": "3h"}
      """

    When 900ms elapse
    Then a delta frame replaces "tiles.1" with:
      """
      {"kind": "place", "line1": "Edo-Tokyo Museum", "line2": "indoor • ★4.5", "line3": "2h"}
      """

    When 1100ms elapse
    Then a delta frame replaces "tiles.2" with:
      """
      {"kind": "place", "line1": "Tsukiji Outer Market", "line2": "covered • ★4.7", "line3": "1.5h"}
      """

    When 1300ms elapse
    Then a delta frame updates "badge.value0to1Anim" to {"durMs": 600, "curve": "easeOutCubic"}
    And a delta frame updates "badge.value0to1" to 0.92

    When 1500ms elapse
    Then a delta frame updates "hero.title" to "Tokyo — Rain pivot"
    And the composer signals stream-complete

    Then the rendered widget tree includes exactly:
      | widget        | count |
      | PersonaInline | 1     |
      | Hero          | 1     |
      | Spacer        | 1     |
      | Tile          | 3     |
      | Badge         | 1     |
    And every Tile.line2 contains "indoor" or "covered"
