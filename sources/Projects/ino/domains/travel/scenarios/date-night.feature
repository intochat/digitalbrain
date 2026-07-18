Feature: Last-minute date night — sub-second composition demo
  Scenario 5 of the v0.1 demo strip. Proves the orb moves at human speed.
  No 6-hop walk; one composed answer with everything stitched. Showcases
  weather correlation and recall-as-personalisation in a single round-trip.

  Primary domain: Travel.
  Secondary domains: Recall, Events, Weather, Taxi, Reminders.
  Primitive emphasised: sub-second multi-tool composition (the LlmNeuron
  agentic loop fires multiple tools in parallel where data dependencies allow,
  then composes a single RFW card).

  @neuron:travel.compose-evening
  @tool:recall.lookup
  @tool:weather.get_forecast
  @tool:travel.find_places
  @tool:events.find_events
  @tool:taxi.book_round_trip
  @tool:reminders.set
  Scenario: Take Anya somewhere nice tonight
    Given the user says "take Anya somewhere nice tonight"
    And recall remembers "person:Anya vegetarian, dislikes loud places, last-3-likes=[rooftop bar, contemporary art, walking tours]"
    And the local time is 17:42

    When the agent calls recall.lookup with subject="Anya"
    And in parallel calls weather.get_forecast with location="here", time="20:00"
    Then both calls return inside 600ms

    When weather.get_forecast returns condition="rain"
    Then the agent drops "rooftop bar" from the candidate set per Anya's first preference
    And the agent calls travel.find_places with category in {restaurant.vegetarian, art.gallery}, openAt="20:00"
    And in parallel calls events.find_events with category="art", date="today"

    Then the bubble emits a single composed Date Night card with:
      | section          | content                                              |
      | restaurant       | one vegetarian-friendly pick within 10 min walk     |
      | activity         | one contemporary art opening or gallery still open  |
      | weather          | rain advisory + suggested indoor framing            |
      | taxi             | one-tap round-trip booking                          |
      | personalisation  | three citation chips ("she likes X (recall)")        |

    When the user taps "book it"
    Then the agent calls taxi.book_round_trip with pickup_at="19:30", return_wait="open"
    And the agent calls reminders.set with text="leave for restaurant", trigger="location:home AND time:19:00"
    And the orb persona switches to "happy" until the leave-now nudge fires
