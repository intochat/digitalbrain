Feature: Friend's wedding weekend — synapses-as-memory demo
  Scenario 2 of the v0.1 demo strip. The signature memory demo — a 3-week-old
  conversation snippet ("Sarah is vegetarian and hates loud places") flows
  back into a present-day RSVP gift recommendation through Recall lookups,
  proving "memories ARE the messages."

  Primary domain: Travel.
  Secondary domains: Recall (heavy), Reminders, Taxi.
  Primitive emphasised: decay-weighted memory recall.

  @neuron:travel.plan-trip
  @tool:recall.lookup
  @tool:weather.get_climatology
  @tool:travel.find_flights
  @tool:travel.find_hotels
  @tool:events.find_events
  @tool:reminders.set
  Scenario: Plan attendance for Sarah's wedding in Lisbon on June 14 2026
    Given calendar shows event "Sarah's wedding, Lisbon, 2026-06-14"
    And recall remembers "person:Sarah likes pottery, hates loud places, vegetarian"
    And recall remembers "home airport=SFO"

    When the user says "plan my trip for Sarah's wedding"
    Then the agent calls recall.lookup with subject="Sarah" and surfaces dietary + ambience preferences
    And the agent anchors trip dates to the calendar entry without prompting
    And the agent calls weather.get_climatology with destination="Lisbon", month="June"

    When the agent ranks hotels
    Then quiet-rated venues are surfaced above lively-rated ones
    And the bubble cites "I remembered Sarah dislikes loud places" inline

    When the agent suggests a wedding gift
    Then the suggestion is "ceramics workshop voucher"
    And the bubble cites "I remembered Sarah likes pottery (chat 2026-04-19)"
    And the citation links into the Inspector → Memory tab for verification

    When the user accepts the gift suggestion
    Then the agent calls reminders.set with text="confirm RSVP with vegetarian meal", at="2026-05-10"

  @neuron:recall.search
  Scenario: User directly queries memory mid-flow
    Given the user says "what else do I know about Sarah?"
    Then the agent calls recall.lookup with subject="Sarah" and surface=full
    And the bubble emits a Memory card with decay-weighted snippets (most recent first)
