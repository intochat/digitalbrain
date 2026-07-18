Feature: UIComposer — Tokyo trip plan, generative composition (slice U.4)
  v3 Compose contract: when the user says "plan my Tokyo trip for May 1-7",
  the kernel UIComposer emits a single rfwtxt skeleton over the ino.rive
  library plus a stream of DynamicContent deltas (pattern α —
  skeleton-then-data, locked decision §3.3 of the v3 design spec). The
  Flutter shell mounts a single RemoteWidget; the data deltas land in the
  same DynamicContent so RFW reactivity refreshes the bound Rive VM
  properties without re-parsing the skeleton.

  This .feature is the source of truth for two consumers:
    1. clients/ino.flutter/lib/screens/rfw_v3_demo/_demo_script.dart —
       drives the demo's timed delta playback today.
    2. src/Ino.Kernel.Tests/Features/UIComposer.feature step bindings (when
       slice U.4 ships server-side) — verifies the BddMockChatClient emits
       this exact (skeleton, deltas) sequence for the prompt below.

  Discoverable by ino breadth/cortex search via the @neuron and @intent
  tags below. The kernel registers @neuron:ui.compose as a synthetic
  neuron whose plan grain dispatches to UIComposer; @intent tags drive
  the LLM classifier's example bank.

  Tweenable numeric fields opt into a (durMs, curve) tween via paired
  sibling fields `<name>AnimDurMs` + `<name>AnimCurve`. The composer writes
  both before (or alongside) the value mutation; the LocalWidgetBuilder
  packages them into an AnimSpec passed to ViewModelHandle.writeNumber. A
  bare value write (no anim siblings present) snaps. Curve vocabulary is
  closed: linear|easeIn|easeOut|easeInOut|easeOutCubic.

  @neuron:ui.compose
  @intent:travel.plan-trip
  @stream-pattern:skeleton-then-data
  @widget-set:hero,tile,badge,persona-inline,spacer
  @anim-schema:v1
  @scenario-id:tokyo-trip-plan
  Scenario: Tokyo trip plan composition
    Given the user is on the v3 chat surface
    And the kernel ino-design palette exposes Hero, Tile, Badge, PersonaInline, Spacer artboards

    When the user says "plan my Tokyo trip for May 1-7"
    Then the composer emits a skeleton frame within 250ms with rfwtxt:
      """
      import ino.rive;
      import core.widgets;
      widget root = Column(children: [
        PersonaInline(domain: "kernel", mood: data.persona.mood, energy: data.persona.energy, energyAnimDurMs: data.persona.energyAnim.durMs, energyAnimCurve: data.persona.energyAnim.curve),
        Hero(domain: "kernel", title: data.hero.title, subtitle: data.hero.subtitle, mood: data.hero.mood),
        Spacer(domain: "kernel", height: 24, motif: data.spacer.motif),
        Tile(domain: "kernel", kind: data.tiles.0.kind, line1: data.tiles.0.line1, line2: data.tiles.0.line2, line3: data.tiles.0.line3),
        Tile(domain: "kernel", kind: data.tiles.1.kind, line1: data.tiles.1.line1, line2: data.tiles.1.line2, line3: data.tiles.1.line3),
        Tile(domain: "kernel", kind: data.tiles.2.kind, line1: data.tiles.2.line1, line2: data.tiles.2.line2, line3: data.tiles.2.line3),
        Badge(domain: "kernel", label: data.badge.label, value0to1: data.badge.value0to1, value0to1AnimDurMs: data.badge.value0to1Anim.durMs, value0to1AnimCurve: data.badge.value0to1Anim.curve),
      ]);
      """
    And the seed DynamicContent payload is:
      """
      {
        "persona": {"mood": "discovering", "energy": 0.55},
        "hero":    {"title": "Searching…", "subtitle": "", "mood": "discovering"},
        "spacer":  {"motif": "wave"},
        "tiles":   [
          {"kind": "flight", "line1": "", "line2": "", "line3": ""},
          {"kind": "hotel",  "line1": "", "line2": "", "line3": ""},
          {"kind": "place",  "line1": "", "line2": "", "line3": ""}
        ],
        "badge":   {"label": "Budget", "value0to1": 0}
      }
      """

    When 280ms elapse
    Then a delta frame updates "hero.title" to "Tokyo, May 1–7"

    When 480ms elapse
    Then a delta frame updates "hero.subtitle" to "Cherry blossom finale week"

    When 760ms elapse
    Then a delta frame replaces "tiles.0" with:
      """
      {"kind": "flight", "line1": "ANA NH106 09:50", "line2": "11h direct AMS→HND", "line3": "¥85,400"}
      """

    When 980ms elapse
    Then a delta frame replaces "tiles.1" with:
      """
      {"kind": "hotel", "line1": "Park Hyatt Shinjuku", "line2": "5★ • garden suite", "line3": "¥48,800/night"}
      """

    When 1180ms elapse
    Then a delta frame replaces "tiles.2" with:
      """
      {"kind": "place", "line1": "Shibuya Sky", "line2": "sunset viewing 18:30", "line3": "2h"}
      """

    When 1480ms elapse
    Then a delta frame updates "badge.value0to1Anim" to {"durMs": 500, "curve": "easeOutCubic"}
    And a delta frame updates "badge.value0to1" to 0.62

    When 1700ms elapse
    Then a delta frame updates "persona.energyAnim" to {"durMs": 350, "curve": "easeOut"}
    And a delta frame updates "persona.energy" to 0.85
    And a delta frame updates "persona.mood" to "happy"
    And the composer signals stream-complete

    Then the rendered widget tree includes exactly:
      | widget        | count |
      | PersonaInline | 1     |
      | Hero          | 1     |
      | Spacer        | 1     |
      | Tile          | 3     |
      | Badge         | 1     |
    And no skeleton placeholder remains — every Tile.line1 is non-empty
