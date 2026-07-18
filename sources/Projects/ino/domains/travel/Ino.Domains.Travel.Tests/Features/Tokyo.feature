Feature: Tokyo plan demo storyboard
  The Claude-Design ino-shell prototype's 6-second Tokyo demo, expressed
  as a Reqnroll feature. This file is the single source of truth: the
  scenarios assert real cluster firings against TestCluster, and the
  storyboard exporter emits tokyo.json which the Flutter DemoRunner
  replays for the visual demo.

  @export-storyboard:tokyo
  Scenario: Plan a 5-day Tokyo trip in late October
    Given a fresh ino brain with the v0.1 cluster set
    When the user says "Plan a 5-day Tokyo trip in late October, rain-friendly, mid-budget, leave from Kyiv."
    Then the persona is "listening" at +0.00s
    And the persona is "thinking" at +1.20s
    And "Cortex" synapses to "PlanTrip" at +1.20s with payload { "intent": "plan_trip", "city": "Tokyo" }
    And "PlanTrip" synapses to "FindFlights" at +1.60s with payload { "from": "KBP", "to": "NRT", "when": "2026-10-22..27", "tier": "mid" }
    And "PlanTrip" synapses to "FindHotels" at +1.62s with payload { "city": "Tokyo", "tier": "mid", "constraints": ["rain-friendly"] }
    And "PlanTrip" synapses to "FindPlaces" at +1.64s with payload { "city": "Tokyo", "mood": "rain-friendly" }
    And "Preferences" synapses to "PlanTrip" at +2.00s gold with payload { "ryokanBias": 0.62, "hotelChainBias": -0.38, "source": "recall.priorTrips" }
    And "Forecast" synapses to "PlanTrip" at +2.40s with payload { "tokyo_oct": { "d1": 0.22, "d2": 0.61, "d3": 0.78, "d4": 0.30, "d5": 0.18 } }
    And the "flights" card enters at +3.00s from "travel"
    And the "hotels" card enters at +3.80s from "travel"
    And the "itinerary" card enters at +4.60s from "travel"
    And "PlanTrip" synapses to "VisaReminder" at +5.40s with payload { "topic": "visa", "remindIn": "3 days" }
    And the "reminder" card enters at +5.50s from "reminders"
    And the persona is "celebrating" at +6.00s
    And the persona is "idle" at +6.20s

  @export-storyboard:tokyo-replan
  Scenario: Make day 3 cheaper replan
    Given the previous Tokyo plan is on screen
    When the user says "Make day 3 cheaper."
    Then the persona is "thinking" at +0.10s
    And "Cortex" synapses to "PlanTrip" at +0.30s with payload { "intent": "refine", "dim": "day3.budget" }
    And "PlanTrip" synapses to "FindHotels" at +0.55s with payload { "day": 3, "max": "mid-low", "swap": true }
    And the "hotels" card morphs at +1.20s
    And the persona is "idle" at +1.40s
