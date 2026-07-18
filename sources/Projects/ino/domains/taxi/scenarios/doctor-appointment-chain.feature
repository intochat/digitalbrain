Feature: Doctor's appointment chain — trust + reversibility demo
  Scenario 3 of the v0.1 demo strip. Short loop, high-trust. Every step is
  reversible; anything that costs money is gated through the Inspector's
  Proposals tab so the user has a single confirmation surface.

  Primary domain: Taxi (the binding action).
  Secondary domains: Recall, Reminders, Calendar (stub), Travel.find_places.
  Primitive emphasised: human-in-the-loop approval for irreversible steps.

  @neuron:health.book-followup
  @tool:recall.lookup
  @tool:calendar.find_slot
  @tool:reminders.set
  @tool:taxi.book_round_trip
  @tool:travel.find_places
  Scenario: Book Dr Chen follow-up for Tuesday afternoon with bracketing rides
    Given the user says "book me a follow-up with Dr Chen next Tuesday afternoon"
    And recall remembers "doctor:Chen at 450 Sutter, last visit was prescription refill"

    When the agent calls calendar.find_slot with provider="Dr Chen", window="next Tuesday 13:00-17:00"
    Then the bubble emits a SlotPicker card with three open slots

    When the user picks 14:30
    Then the agent emits a Proposal "book Dr Chen 2026-05-12 14:30" tagged ApprovalRequired
    And the Inspector → Proposals tab shows the proposal pending

    When the user approves the proposal
    Then the booking call fires
    And the agent calls reminders.set with text="meds before visit", at="2026-05-12 06:00"
    And the agent calls reminders.set with text="fasting reminder if blood-work", at="2026-05-11 22:00"
    And the agent calls taxi.book_round_trip with pickup_at="2026-05-12 14:00", return_wait_minutes=90

    When the user asks "anything I should know about that area?"
    Then the agent calls travel.find_places with location="450 Sutter", category="pharmacy"
    And the bubble emits a Place list ordered by walking distance from the clinic

  Scenario: Cancellation cascade is fully reversible
    Given the user has the Dr Chen appointment booked with bracketing rides + reminders

    When the user says "cancel that"
    Then the agent emits a Proposal "cancel Dr Chen + taxi pair + reminders" tagged ApprovalRequired

    When the user approves the cancel
    Then all four prior side-effects are reversed in journal order
    And the bubble confirms each reversal individually
