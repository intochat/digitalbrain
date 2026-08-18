Feature: mvp chart
  # Scripts BddMockChatClient for the full-turn safety net (ChatTurnTests): every When
  # becomes a real 'fire' tool call through the production pipeline, so the points target
  # the chart instance the closing card names (the corpus grammar's chart-card invariant).

  Scenario: plot request
    Given the user says "plot these values.*"
    When the assistant fires "ui.chart-point" at "chart:demo" with {"series":"demo","label":"a","value":1}
    When the assistant fires "ui.chart-point" at "chart:demo" with {"series":"demo","label":"b","value":3}
    When the assistant fires "ui.chart-card" at the chat with {"title":"demo"}
    Then the assistant replies "Plotted 2 points on demo."
