Feature: MVP chart over the kernel edge
  # Tier 3 of the section-9 flow: the same corpus that scripts the mock LLM
  # (tests/corpus/mvp-chart.feature) answers this scenario through the production
  # AppHost -- kernel HTTP edge, SSE deltas, chart entity, story fact -- amended
  # with the C4 "check chart" resolve.

  Scenario: plot request reaches the chart and the brain resolves it
    Given an activated owner session
    When the user chats "plot these values 1 and 3"
    Then the assistant replies "Plotted 2 points on demo."
    And the chart "demo" holds 2 points
    And the memory holds the story fact "Plotted 2 points on demo."
    And resolving "chart" finds "demo"
