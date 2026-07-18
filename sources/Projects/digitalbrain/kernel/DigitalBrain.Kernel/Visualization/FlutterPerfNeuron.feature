@neuron:kernel/flutter-perf
@stage:fast
@telemetry:counter:flutterperf.samples_ingested
@telemetry:counter:flutterperf.hints_emitted
@telemetry:counter:flutterperf.cards_broadcast
@telemetry:histogram:flutterperf.p95_ms
@telemetry:histogram:flutterperf.window_size
Feature: FlutterPerfNeuron projects frame health into a FlutterPerfCard and
         emits a VisualLoadHint when a client's tier crosses and holds.

  Scenario: No samples means no card and no hint
    Given the perf neuron has no clients
    When Tick is called
    Then no FlutterPerfCard is broadcast
    And no VisualLoadHint is emitted

  Scenario: Smooth samples broadcast a card with smooth tier
    Given client "web-1" has 5 samples with p95 12ms
    When Tick is called
    Then a FlutterPerfCard is broadcast with summary tier "smooth"
    And no VisualLoadHint is emitted because tier did not change

  Scenario: Sustained red crossing emits exactly one hint
    Given client "windows-1" has been on tier "smooth"
    When 2 ticks arrive with p95 40ms across 1.5 seconds
    Then a VisualLoadHint with tier "red" is emitted for "windows-1"
    And the next tick with another p95 40ms sample emits no further hint

  Scenario: Transient crossing does not emit a hint
    Given client "web-2" has been on tier "smooth"
    When 1 tick arrives with p95 40ms and the next tick at p95 10ms
    Then no VisualLoadHint is emitted

  Scenario: Per-client isolation
    Given client "web-3" with p95 12ms and client "windows-3" with p95 40ms sustained
    When 2 ticks arrive 1.5 seconds apart
    Then one VisualLoadHint with tier "red" is emitted for "windows-3" only
    And the broadcast FlutterPerfCard summary tier equals "red"

  Scenario: Idle client is dropped
    Given client "web-4" sent samples 9 seconds ago and nothing since
    When Tick is called
    Then "web-4" is not present in the next broadcast clients array
